public enum GameModeKind
{
    Local,
    Multiplayer
}

/// <summary>
/// Which mode this session is running in.
///
/// Chosen once at the main menu, then read by scene scripts (lobby, ship,
/// island) so they know which control path to use: local couch co-op players,
/// or networked players. Static, so it survives every scene load.
/// </summary>
public static class GameMode
{
    public static GameModeKind Current { get; private set; } = GameModeKind.Local;

    public static bool IsLocal => Current == GameModeKind.Local;

    public static bool IsMultiplayer => Current == GameModeKind.Multiplayer;

    public static void Set(GameModeKind mode)
    {
        Current = mode;
        UnityEngine.Debug.Log($"[Game Mode] Session mode set to {mode}.");
    }

    public static void SetLocal()
    {
        Set(GameModeKind.Local);
    }

    public static void SetMultiplayer()
    {
        Set(GameModeKind.Multiplayer);
    }
}
