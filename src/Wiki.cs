using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SecretHistories.UI;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace MarginaliaMod
{
    // Talks to the community wiki's MediaWiki API: finds the article for an in-game
    // name, fetches it, and converts it for the in-game wiki window. Results are cached
    // for the session.
    public static class Wiki
    {
        public sealed class Article
        {
            public string Title;
            public string Body;
        }

        private const string Site = "https://cultistsimulator.fandom.com";
        private const string Api = Site + "/api.php?format=json&formatversion=2";
        private const int TimeoutSeconds = 8;

        // TMP meshes top out around 16k characters; very long articles get cut short.
        private const int MaxBodyLength = 15000;

        private static readonly Regex Markup = new Regex("<[^>]*>");
        private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Article> Articles = new Dictionary<string, Article>(StringComparer.OrdinalIgnoreCase);

        public static string CleanName(string label)
        {
            return Markup.Replace(label ?? "", "").Trim();
        }

        // Best-matching article title for an in-game name, or null if the wiki has none.
        public static IEnumerator FindTitle(string name, Action<string, string> done)
        {
            if (Titles.TryGetValue(name, out var cached))
            {
                done(cached, null);
                yield break;
            }

            JToken reply = null;
            string error = null;
            yield return GetJson(Api + "&action=opensearch&limit=1&redirects=resolve&search=" + Uri.EscapeDataString(name),
                (json, err) => { reply = json; error = err; });
            if (error != null)
            {
                done(null, error);
                yield break;
            }

            // opensearch replies [query, [titles], [descriptions], [urls]].
            var titles = reply is JArray array && array.Count > 1 ? array[1] as JArray : null;
            var title = titles != null && titles.Count > 0 ? (string)titles[0] : null;
            if (title != null)
                Titles[name] = title;
            done(title, null);
        }

        public static IEnumerator Fetch(string title, Action<Article, string> done)
        {
            if (Articles.TryGetValue(title, out var cached))
            {
                done(cached, null);
                yield break;
            }

            JToken reply = null;
            string error = null;
            yield return GetJson(Api + "&action=parse&prop=text&redirects=1&disableeditsection=1&disabletoc=1&page=" + Uri.EscapeDataString(title),
                (json, err) => { reply = json; error = err; });
            if (error != null)
            {
                done(null, error);
                yield break;
            }

            var apiError = reply?["error"]?["info"];
            var parse = reply?["parse"];
            if (apiError != null || parse == null)
            {
                done(null, apiError != null ? "The wiki says: " + apiError : "The wiki sent back something unexpected.");
                yield break;
            }

            var resolvedTitle = (string)parse["title"] ?? title;
            var html = (string)parse["text"] ?? "";

            // A category page's own text is often just a line; its pages are the content.
            string members = null;
            if (resolvedTitle.StartsWith("Category:"))
                yield return CategoryMembers(resolvedTitle, links => members = links);

            Article article;
            try
            {
                var body = WikiHtml.ToRichText(html);
                if (!string.IsNullOrEmpty(members))
                    body = (body + "\n\n" + members).Trim();
                article = new Article { Title = resolvedTitle, Body = Truncate(body) };
            }
            catch (Exception ex)
            {
                NoonUtility.LogWarning("Marginalia: could not render wiki article '" + title + "': " + ex);
                done(null, "This article couldn't be rendered in game. Try opening it in the browser.");
                yield break;
            }
            Articles[title] = article;
            Articles[article.Title] = article;
            done(article, null);
        }

        private static IEnumerator CategoryMembers(string category, Action<string> done)
        {
            JToken reply = null;
            yield return GetJson(Api + "&action=query&list=categorymembers&cmtype=page&cmlimit=200&cmtitle=" + Uri.EscapeDataString(category),
                (json, err) => reply = json);
            var pages = reply?["query"]?["categorymembers"] as JArray;
            if (pages == null || pages.Count == 0)
            {
                done(null);
                yield break;
            }
            var lines = new List<string> { "<size=118%><b><color=#B8944F>Pages in this category</color></b></size>" };
            foreach (var page in pages)
            {
                var title = (string)page["title"];
                if (!string.IsNullOrEmpty(title))
                    lines.Add("• <link=\"" + title.Replace("\"", "'") + "\"><color=" + WikiHtml.LinkColor + ">" +
                              title.Replace("<", "<\u200B") + "</color></link>");
            }
            done(string.Join("\n", lines));
        }

        private static IEnumerator GetJson(string url, Action<JToken, string> done)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    NoonUtility.Log("Marginalia: wiki request failed (" + request.error + "): " + url);
                    done(null, "The wiki could not be reached (" + request.error + ").");
                    yield break;
                }
                try
                {
                    done(JToken.Parse(request.downloadHandler.text), null);
                }
                catch (Exception ex)
                {
                    NoonUtility.Log("Marginalia: unreadable wiki reply from " + url + ": " + ex.Message);
                    done(null, "The wiki sent back something unreadable.");
                }
            }
        }

        private static string Truncate(string body)
        {
            if (body.Length <= MaxBodyLength)
                return body;
            int cut = body.LastIndexOf('\n', MaxBodyLength);
            return body.Substring(0, cut > 0 ? cut : MaxBodyLength).TrimEnd() +
                   "\n\n<i><color=#A89980>The article goes on; open it in the browser to read the rest.</color></i>";
        }

        // ---- Browser fallback

        // The bare ?action=render view skips Fandom's slow site chrome, scripts and ads.
        public static void OpenInBrowser(string title)
        {
            OpenUrl(Site + "/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) + "?action=render");
        }

        public static void SearchInBrowser(string name)
        {
            OpenUrl(Site + "/wiki/Special:Search?go=Go&search=" + Uri.EscapeDataString(name));
        }

        private static void OpenUrl(string url)
        {
            if (SteamOverlayAvailable())
                SteamFriends.ActivateGameOverlayToWebPage(url);
            else
                Application.OpenURL(url);
        }

        private static bool SteamOverlayAvailable()
        {
            try
            {
                return SteamManager.Initialized && SteamUtils.IsOverlayEnabled();
            }
            catch (Exception ex)
            {
                // SteamManager.Initialized throws if the manager never started (e.g. GOG).
                NoonUtility.Log("Marginalia: Steam overlay unavailable (" + ex.Message + "); using the system browser.");
                return false;
            }
        }
    }

    public static class WikiMenuItems
    {
        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "wiki.open",
                Order = 50,
                Label = _ => "Open wiki",
                IsVisible = t => t.Kind == TargetKind.Element || t.Kind == TargetKind.Verb,
                Execute = t => MarginaliaController.Instance.OpenWiki(t)
            });
        }
    }
}
