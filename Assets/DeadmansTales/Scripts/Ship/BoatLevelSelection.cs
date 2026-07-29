/// <summary>
/// Carries the boat "level" chosen from the menu into the boat scene.
///
/// A menu level button (see <see cref="BoatLevelButtonFlag"/>) writes its
/// number here on click; <c>BoatLegProgress</c> reads it when the voyage
/// reaches the boat and maps it to that level's event count. It is a plain
/// static so it survives the scene loads between the menu and the boat without
/// any networked state, and it is overwritten each time a level is chosen.
/// </summary>
public static class BoatLevelSelection
{
    /// <summary>
    /// The chosen level (1-based). 0 means nothing was chosen, so the boat
    /// falls back to its own Default Level.
    /// </summary>
    public static int PendingLevel;
}
