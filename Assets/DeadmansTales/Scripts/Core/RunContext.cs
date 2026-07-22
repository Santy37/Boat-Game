/// <summary>
/// What kind of scene this is, from the run's point of view.
/// </summary>
public enum RunSceneKind
{
    Lobby,
    Map,
    Boat,
    Island,
    Boss
}

/// <summary>How the run ended, if it has.</summary>
public enum RunOutcome
{
    InProgress,
    Won,
    Lost
}

/// <summary>
/// A mode-agnostic view of the active run.
///
/// The local run manager implements this today; a network run manager can
/// implement the same interface later. Gameplay code talks to
/// <see cref="RunContext.Active"/> and never asks which mode is running.
/// </summary>
public interface IRunContext
{
    int Seed { get; }
    int Stage { get; }

    /// <summary>How many player slots exist (seats at the table).</summary>
    int MaxPlayers { get; }

    /// <summary>How many players have actually joined the run.</summary>
    int JoinedCount { get; }

    bool IsJoined(int playerIndex);

    RunMapModel Map { get; }
    int CurrentNodeId { get; }

    /// <summary>Won / lost / still going.</summary>
    RunOutcome Outcome { get; }

    /// <summary>Shared ship hull. The run is lost when it hits zero.</summary>
    int ShipHealth { get; }
    int MaxShipHealth { get; }

    /// <summary>Damage the ship. Zero ends the run in a loss.</summary>
    void DamageShip(int amount);

    /// <summary>Damage one player. All players down ends the run in a loss.</summary>
    void DamagePlayer(int playerIndex, int amount);

    KeyBindings GetBindings(int playerIndex);
    PlayerRunData GetPlayerData(int playerIndex);

    System.Random CreateRandom(string streamName);

    void OpenMap();
    void ChooseDestination(int nodeId);
    void OnBoatArrived();
}

/// <summary>
/// Static hand-off point for the currently active run.
/// </summary>
public static class RunContext
{
    public static IRunContext Active { get; private set; }

    public static bool HasActive => Active != null;

    public static void SetActive(IRunContext context)
    {
        Active = context;
    }

    public static void Clear(IRunContext context)
    {
        if (Active == context)
        {
            Active = null;
        }
    }
}
