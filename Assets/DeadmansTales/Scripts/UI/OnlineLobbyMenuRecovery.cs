using System.Collections;
using DeadmansTales.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Repairs the existing generic Back/Cancel buttons without requiring the
/// MainMenu scene YAML to be rewritten. Back now leaves a live online session
/// before returning to the main menu, and Cancel Join clears failed network
/// state so another join/create attempt can run immediately.
/// </summary>
public sealed class OnlineLobbyMenuRecovery : MonoBehaviour
{
    private MainMenuManager mainMenu;

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.AfterSceneLoad
    )]
    private static void InstallForLoadedMenu()
    {
        MainMenuManager menu = Object.FindFirstObjectByType<MainMenuManager>(
            FindObjectsInactive.Include
        );

        if (menu == null || menu.GetComponent<OnlineLobbyMenuRecovery>() != null)
        {
            return;
        }

        menu.gameObject.AddComponent<OnlineLobbyMenuRecovery>();
    }

    private IEnumerator Start()
    {
        mainMenu = GetComponent<MainMenuManager>();

        // MainMenuManager wires its known buttons during Awake. Waiting one frame
        // guarantees these context-aware handlers are the final listeners.
        yield return null;
        WireExitButtons();
    }

    private void WireExitButtons()
    {
        Button[] buttons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        foreach (Button button in buttons)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);

            if (label == null)
            {
                continue;
            }

            string normalized = NormalizeLabel(label.text);

            if (normalized == "BACK")
            {
                ReplaceAction(button, HandleBack);
            }
            else if (normalized == "CANCEL JOIN")
            {
                ReplaceAction(button, HandleCancelJoin);
            }
        }
    }

    private async void HandleBack()
    {
        OnlineLobbyService service = OnlineLobbyService.Instance;

        if (service != null && service.IsInSession)
        {
            await service.LeaveLobbyAsync();
        }
        else if (service != null && !service.IsBusy)
        {
            service.ResetLocalSession();
        }

        mainMenu?.ShowMainMenu();
    }

    private void HandleCancelJoin()
    {
        OnlineLobbyService service = OnlineLobbyService.Instance;

        if (service != null && !service.IsBusy)
        {
            service.ResetLocalSession();
        }

        mainMenu?.ShowConnectionOptions();
    }

    private static void ReplaceAction(Button button, UnityEngine.Events.UnityAction action)
    {
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(action);
    }

    private static string NormalizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim()
            .ToUpperInvariant();
    }
}
