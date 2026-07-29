using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drop this on a menu level button to tag which BOAT LEVEL it starts. On
/// click it records that level in <see cref="BoatLevelSelection"/>; the boat
/// scene's <c>BoatLegProgress</c> then reads it to decide how many events
/// spawn. The button keeps whatever else it already does (e.g. loading its
/// island) -- this only adds the flag.
/// </summary>
[RequireComponent(typeof(Button))]
public sealed class BoatLevelButtonFlag : MonoBehaviour
{
    [Tooltip(
        "Boat level this button starts. BoatLegProgress maps it to an event " +
        "count via its Events Per Level list. 1-based.")]
    [SerializeField]
    private int boatLevel = 1;

    private void Start()
    {
        // Registered in Start, AFTER MainMenuManager rewires the level buttons'
        // onClick in Awake (all Awakes run before any Start), so this listener
        // isn't wiped. It runs alongside the button's existing action rather
        // than replacing it.
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(RecordSelection);
        }
    }

    private void RecordSelection()
    {
        BoatLevelSelection.PendingLevel = Mathf.Max(1, boatLevel);
    }
}
