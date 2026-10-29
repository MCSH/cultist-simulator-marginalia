using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SecretHistories.UI;
using Steamworks;
using UnityEngine;

namespace MarginaliaMod
{
    // Mirrors the mod's files into one file in the game's Steam Cloud space, so they
    // follow the player between machines. Steam won't pick them up by itself: the game
    // only lists its own saves for that.
    //
    // The local files stay the source of truth, and everything works without Steam. The
    // cloud copy holds each file with the time it last changed. Once Steam is up, each
    // file is compared with its cloud copy and the newer side wins; after that, every
    // save is pushed as it happens. Steam uploads the cloud file when the game closes.
    public static class SteamCloud
    {
        private const string CloudFile = "marginalia_cloud.json";

        // File systems and JSON round times differently; closer than this is the same.
        private static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

        // Asking SteamManager.Initialized before the game has made its own manager makes
        // a stray one, and then Steam never starts. This flag is safe to read any time.
        private static readonly FieldInfo EverInitialized =
            typeof(SteamManager).GetField("s_EverInitialized", BindingFlags.Static | BindingFlags.NonPublic);

        private sealed class Entry
        {
            public DateTime Updated;
            public JToken Data;
        }

        private static string[] _paths = new string[0];
        private static Action _reload;

        // Null until the first sync has happened.
        private static Dictionary<string, Entry> _cloud;
        private static bool _off;
        private static float _nextTry;

        // paths are the files to keep in the cloud; reload re-reads them after the cloud
        // has replaced some with newer copies.
        public static void Watch(string[] paths, Action reload)
        {
            _paths = paths;
            _reload = reload;
        }

        // Called every frame; syncs once, as soon as the game has started Steam.
        public static void Tick()
        {
            if (_cloud != null || _off || Time.unscaledTime < _nextTry)
                return;
            _nextTry = Time.unscaledTime + 1f;
            // Not started (yet, or ever, as on GOG): keep checking, it's cheap.
            if (EverInitialized == null || !(bool)EverInitialized.GetValue(null))
                return;

            try
            {
                if (!SteamRemoteStorage.IsCloudEnabledForAccount() || !SteamRemoteStorage.IsCloudEnabledForApp())
                {
                    TurnOff("Steam Cloud is off for this game or account");
                    return;
                }
                var cloud = ReadCloud();
                if (cloud == null)
                    return;
                _cloud = cloud;

                bool localChanged = false, cloudChanged = false;
                foreach (var path in _paths)
                    Sync(path, ref localChanged, ref cloudChanged);
                if (cloudChanged)
                    WriteCloud();
                if (localChanged)
                    _reload();
                NoonUtility.Log("Marginalia: synced with Steam Cloud");
            }
            catch (Exception ex)
            {
                _cloud = null;
                TurnOff(ex.Message);
            }
        }

        // After a local save: the cloud copy follows it.
        public static void Push(string path, string json)
        {
            if (_cloud == null || !_paths.Contains(path))
                return;
            try
            {
                _cloud[Path.GetFileName(path)] = new Entry { Updated = File.GetLastWriteTimeUtc(path), Data = JToken.Parse(json) };
                WriteCloud();
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not save " + Path.GetFileName(path) + " to Steam Cloud: " + ex.Message);
            }
        }

        private static void Sync(string path, ref bool localChanged, ref bool cloudChanged)
        {
            string name = Path.GetFileName(path);
            _cloud.TryGetValue(name, out var cloud);
            bool local = File.Exists(path);
            var localTime = local ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

            if (cloud != null && (!local || cloud.Updated - localTime > Slack))
            {
                if (!JsonFile.WriteText(path, cloud.Data.ToString(Formatting.Indented)))
                    return;
                // Dated as the cloud copy, so it doesn't look newer next time.
                File.SetLastWriteTimeUtc(path, cloud.Updated);
                localChanged = true;
                NoonUtility.Log("Marginalia: took the newer " + name + " from Steam Cloud");
            }
            else if (local && (cloud == null || localTime - cloud.Updated > Slack))
            {
                try
                {
                    _cloud[name] = new Entry { Updated = localTime, Data = JToken.Parse(File.ReadAllText(path)) };
                    cloudChanged = true;
                }
                catch (Exception ex)
                {
                    NoonUtility.LogWarning("Marginalia: not saving " + name + " to Steam Cloud, it can't be read: " + ex.Message);
                }
            }
        }

        private static Dictionary<string, Entry> ReadCloud()
        {
            if (!SteamRemoteStorage.FileExists(CloudFile))
                return new Dictionary<string, Entry>();
            int size = SteamRemoteStorage.GetFileSize(CloudFile);
            var bytes = new byte[size];
            if (SteamRemoteStorage.FileRead(CloudFile, bytes, size) != size)
            {
                TurnOff("could not read " + CloudFile);
                return null;
            }
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, Entry>>(Encoding.UTF8.GetString(bytes))
                       ?? new Dictionary<string, Entry>();
            }
            catch (Exception ex)
            {
                // Leave it be rather than write over it.
                TurnOff(CloudFile + " can't be read: " + ex.Message);
                return null;
            }
        }

        private static void WriteCloud()
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_cloud));
            if (!SteamRemoteStorage.FileWrite(CloudFile, bytes, bytes.Length))
                NoonUtility.LogWarning("Marginalia: Steam Cloud refused " + CloudFile + " (is the game's cloud space full?)");
        }

        private static void TurnOff(string why)
        {
            _off = true;
            NoonUtility.Log("Marginalia: not using Steam Cloud: " + why);
        }
    }
}
