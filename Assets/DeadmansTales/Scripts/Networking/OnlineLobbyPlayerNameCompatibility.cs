using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// The current menu only displays lobby code and player count, so player-name
    /// synchronization is intentionally disabled until the game exposes a real
    /// player-name field. Multiplayer Services 2.2.3 throws during create/join
    /// when WithPlayerName is enabled before Authentication has a player name.
    /// </summary>
    internal static class OnlineLobbyPlayerNameCompatibility
    {
        private static readonly FieldInfo UsePlayerNameField =
            typeof(OnlineLobbyService).GetField(
                "usePlayerName",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.BeforeSceneLoad
        )]
        private static void RegisterSceneCallback()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(
            Scene scene,
            LoadSceneMode loadSceneMode
        )
        {
            DisableUnusedPlayerNameSync();
        }

        private static void DisableUnusedPlayerNameSync()
        {
            OnlineLobbyService service = OnlineLobbyService.Instance;

            if (service == null)
            {
                service = Object.FindFirstObjectByType<OnlineLobbyService>();
            }

            if (service == null)
            {
                return;
            }

            if (UsePlayerNameField == null)
            {
                Debug.LogError(
                    "[Online Lobby] Could not locate the usePlayerName setting.",
                    service
                );
                return;
            }

            bool wasEnabled = (bool)UsePlayerNameField.GetValue(service);

            if (!wasEnabled)
            {
                return;
            }

            UsePlayerNameField.SetValue(service, false);

            Debug.Log(
                "[Online Lobby] Optional player-name synchronization disabled " +
                "until the menu provides a player-name field.",
                service
            );
        }
    }
}
