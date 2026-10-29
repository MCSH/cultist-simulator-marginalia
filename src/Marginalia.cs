using System;
using MarginaliaMod;
using SecretHistories.UI;
using UnityEngine;

// Entry point. The game looks for a type named after the mod (sans spaces) with a
// public static Initialise(). It runs once when the app starts, before any scene loads.
public class Marginalia
{
    public static void Initialise()
    {
        try
        {
            NoteStore.Load();
            BoardLabels.Load();
            Rulers.Load();
            PauseWatch.Load();
            SteamCloud.Watch(
                new[] { NoteStore.FilePath, BoardLabels.FilePath, Rulers.FilePath, PauseWatch.FilePath },
                () =>
                {
                    NoteStore.Load();
                    BoardLabels.Load();
                    Rulers.Load();
                    PauseWatch.Load();
                });
            NoteMenuItems.Register();
            BoardMenuItems.Register();
            RulerMenuItems.Register();
            PauseMenuItems.Register();
            WikiMenuItems.Register();

            var host = new GameObject("Marginalia");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<MarginaliaController>();

            NoonUtility.Log("Marginalia: initialised (Alt + right-click a card, verb, aspect or the board).");
        }
        catch (Exception ex)
        {
            NoonUtility.LogWarning("Marginalia: failed to initialise: " + ex);
        }
    }
}
