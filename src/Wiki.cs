using System;
using System.Collections;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SecretHistories.UI;
using Steamworks;
using UnityEngine;
using UnityEngine.Networking;

namespace MarginaliaMod
{
    // Opens the community wiki for a card, aspect or verb, in the Steam overlay browser
    // when it's available and the system browser otherwise (GOG, overlay disabled).
    //
    // Fandom's full pages are slow, so we ask the wiki's search API for the best-matching
    // title and open that article with ?action=render: just the article HTML, no skin,
    // scripts or ads. If the lookup fails we fall back to the full site's search, which
    // jumps to an exactly-titled article or shows results.
    public static class Wiki
    {
        private const string Site = "https://cultistsimulator.fandom.com";
        private const int LookupTimeoutSeconds = 4;

        private static readonly Regex Markup = new Regex("<[^>]*>");

        public static void Open(MenuTarget target)
        {
            MarginaliaController.Instance.StartCoroutine(OpenRoutine(Markup.Replace(target.Label, "").Trim()));
        }

        private static IEnumerator OpenRoutine(string name)
        {
            string url = SearchPageUrl(name);
            string lookup = Site + "/api.php?action=opensearch&format=json&limit=1&redirects=resolve&search=" +
                            Uri.EscapeDataString(name);

            using (var request = UnityWebRequest.Get(lookup))
            {
                request.timeout = LookupTimeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    // opensearch replies [query, [titles], [descriptions], [urls]].
                    try
                    {
                        var titles = JArray.Parse(request.downloadHandler.text)[1] as JArray;
                        if (titles != null && titles.Count > 0)
                            url = ArticleUrl((string)titles[0]);
                    }
                    catch (Exception ex)
                    {
                        NoonUtility.Log("Marginalia: unexpected wiki search reply for '" + name + "': " + ex.Message);
                    }
                }
                else
                {
                    NoonUtility.Log("Marginalia: wiki search for '" + name + "' failed (" + request.error + "); opening the full site.");
                }
            }

            OpenUrl(url);
        }

        private static string ArticleUrl(string title)
        {
            return Site + "/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')) + "?action=render";
        }

        private static string SearchPageUrl(string name)
        {
            return Site + "/wiki/Special:Search?go=Go&search=" + Uri.EscapeDataString(name);
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
                Execute = Wiki.Open
            });
        }
    }
}
