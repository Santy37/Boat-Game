using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local-mode rowboat. Stand in the trigger and press your Interact key to
/// open the map and choose where the run sails next.
///
/// This is the local counterpart to LobbyRowboatInteraction (which is the
/// networked version and stays untouched for online play).
/// </summary>
public class LocalRowboat : MonoBehaviour
{
    [Header("Prompt")]
    [SerializeField] private string prompt = "open the map";

    private readonly List<PlayerCharacter> inRange = new List<PlayerCharacter>();

    private void Awake()
    {
        Collider2D area = GetComponent<Collider2D>();

        if (area == null || !area.isTrigger)
        {
            Debug.LogError(
                $"[Local Rowboat] '{name}' needs a Collider2D with Is Trigger " +
                "enabled on this same object.",
                this);
        }
    }

    private void Update()
    {
        if (inRange.Count == 0 || !RunContext.HasActive)
        {
            return;
        }

        for (int i = inRange.Count - 1; i >= 0; i--)
        {
            PlayerCharacter player = inRange[i];

            if (player == null)
            {
                inRange.RemoveAt(i);
                continue;
            }

            if (player.InteractDown)
            {
                RunContext.Active.OpenMap();
                return;
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        if (player != null && !inRange.Contains(player))
        {
            inRange.Add(player);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();

        if (player != null)
        {
            inRange.Remove(player);
        }
    }

    private void OnGUI()
    {
        if (inRange.Count == 0)
        {
            return;
        }

        const float width = 360f;
        const float height = 44f;

        Rect rect = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height - 90f,
            width,
            height);

        GUI.Box(rect, $"Press Interact to {prompt}");
    }
}
