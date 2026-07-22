using UnityEngine;

/// <summary>
/// Turns objects on or off depending on the active <see cref="GameMode"/>.
///
/// Put this in the lobby, ship and island scenes on anything that belongs to
/// only one mode - for example a local-only camera rig, or the networked
/// start UI. Leave <see cref="targets"/> empty to gate the object it sits on.
/// </summary>
public class ModeGate : MonoBehaviour
{
    [Header("Active in which modes?")]
    [SerializeField] private bool activeInLocal = true;
    [SerializeField] private bool activeInMultiplayer = true;

    [Header("What to toggle")]
    [Tooltip("Objects switched on/off. Leave empty to toggle THIS object.")]
    [SerializeField] private GameObject[] targets;

    private void Awake()
    {
        bool shouldBeActive = GameMode.IsLocal
            ? activeInLocal
            : activeInMultiplayer;

        if (targets == null || targets.Length == 0)
        {
            if (!shouldBeActive)
            {
                gameObject.SetActive(false);
            }

            return;
        }

        foreach (GameObject target in targets)
        {
            if (target != null)
            {
                target.SetActive(shouldBeActive);
            }
        }
    }
}
