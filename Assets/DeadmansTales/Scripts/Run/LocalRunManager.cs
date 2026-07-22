using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Local (non-networked) run manager for couch co-op.
///
/// Owns the whole run: seed, map, stage, every player's data, who has joined,
/// and all scene transitions. It is deliberately scene-agnostic - each scene's
/// <see cref="SceneRunProfile"/> says which player prefab to spawn and where,
/// so a 2D island, a 2D boat and a 3D island all work with this same script.
///
/// Players are not forced into the run: they join in the lobby, and only
/// joined players are spawned into later scenes.
/// </summary>
public class LocalRunManager : MonoBehaviour, IRunContext
{
    [Header("Players")]
    [Tooltip("How many seats exist. Players join into these slots.")]
    [SerializeField] private int maxPlayers = 3;
    [SerializeField] private KeyBindings[] bindings = new KeyBindings[3];
    [Tooltip("Player 1 joins automatically so a run can always start.")]
    [SerializeField] private bool autoJoinFirstPlayer = true;

    [Header("Run")]
    [Tooltip("Roll a new seed every run, so the islands in between, their " +
             "kinds, which island scene each uses and the map shape are all " +
             "different each time. Turn OFF to replay a specific seed.")]
    [SerializeField] private bool randomizeEachRun = true;
    [Tooltip("Used only when Randomize Each Run is off, for repeatable tests.")]
    [SerializeField] private int fixedSeed = 12345;

    [Header("Map Shape")]
    [Tooltip("Rows bottom to top: lobby, island rows, then the boss. Min 3.")]
    [SerializeField] private int mapRows = 3;
    [Tooltip("Fewest islands on a middle row.")]
    [SerializeField] private int minIslandsPerRow = 2;
    [Tooltip("Most islands on a middle row.")]
    [SerializeField] private int maxIslandsPerRow = 3;
    [Tooltip("Chance (%) an island also branches to a second island above it.")]
    [Range(0, 100)]
    [SerializeField] private int secondBranchPercent = 55;

    [Header("Scenes")]
    [SerializeField] private string lobbyScene = "Local_Lobby_Island";
    [SerializeField] private string mapScene = "Map";
    [SerializeField] private string boatScene = "Boat_Gameplay_2D";
    [Tooltip("Island scenes. Each map node picks one from this list.")]
    [SerializeField] private string[] islandScenes = { "Island_2D" };
    [Tooltip("The scene used for the boss island at the top of the map.")]
    [SerializeField] private string bossScene = "Island_2D";

    [Header("Ship")]
    [SerializeField] private int maxShipHealth = 100;

    [Header("Startup")]
    [Tooltip("Load the lobby automatically when this object is created.")]
    [SerializeField] private bool loadLobbyOnStart = false;

    [Header("Menu")]
    [SerializeField] private string menuScene = "StartScene";

    public int Seed { get; private set; }
    public int Stage { get; private set; } = 1;

    public int MaxPlayers => maxPlayers;

    public RunMapModel Map { get; private set; }
    public int CurrentNodeId { get; private set; }
    public int PendingDestinationId { get; private set; } = -1;

    public RunOutcome Outcome { get; private set; } = RunOutcome.InProgress;
    public int ShipHealth { get; private set; }
    public int MaxShipHealth => maxShipHealth;

    private string outcomeReason = string.Empty;

    /// <summary>True when the players are standing on the boss island.</summary>
    public bool IsOnBossNode
    {
        get
        {
            RunMapNode node = Map != null ? Map.Get(CurrentNodeId) : null;
            return node != null && node.kind == RunNodeKind.Boss;
        }
    }

    private readonly List<PlayerRunData> playerData = new List<PlayerRunData>();
    private readonly List<PlayerCharacter> livePlayers = new List<PlayerCharacter>();
    private bool[] joined;

    public int JoinedCount
    {
        get
        {
            int count = 0;
            if (joined == null) return 0;
            for (int i = 0; i < joined.Length; i++)
            {
                if (joined[i]) count++;
            }
            return count;
        }
    }

    public bool IsJoined(int playerIndex)
    {
        return joined != null &&
               playerIndex >= 0 &&
               playerIndex < joined.Length &&
               joined[playerIndex];
    }

    private static LocalRunManager instance;

    private void Awake()
    {
        // StartScene carries its own RunManager, so returning to the menu would
        // otherwise create a second one alongside the persisted copy.
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        DontDestroyOnLoad(gameObject);
        RunContext.SetActive(this);

        BuildRun();

        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void Start()
    {
        if (loadLobbyOnStart)
        {
            SceneManager.LoadScene(lobbyScene);
        }
        else
        {
            SpawnPlayersForCurrentScene();
        }
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        instance = null;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        RunContext.Clear(this);
    }

    private void BuildRun()
    {
        Seed = randomizeEachRun
            ? NewRandomSeed()
            : (fixedSeed == 0 ? 1 : fixedSeed);

        Map = RunMapModel.Generate(
            Seed, mapRows, minIslandsPerRow, maxIslandsPerRow, secondBranchPercent);
        CurrentNodeId = Map.StartNodeId;
        Stage = 1;
        PendingDestinationId = -1;

        playerData.Clear();
        for (int i = 0; i < maxPlayers; i++)
        {
            playerData.Add(new PlayerRunData { playerIndex = i });
        }

        joined = new bool[maxPlayers];

        if (autoJoinFirstPlayer && maxPlayers > 0)
        {
            joined[0] = true;
        }

        ShipHealth = maxShipHealth;
        Outcome = RunOutcome.InProgress;
        outcomeReason = string.Empty;
        Time.timeScale = 1f;

        Debug.Log(
            $"[Local Run] Seed {Seed} | {Map.Nodes.Count} map nodes | " +
            $"{maxPlayers} seats | boss node {Map.BossNodeId}",
            this);
    }

    // -------------------------------------------------------------- joining

    /// <summary>Add a player to the run and drop them into the current scene.</summary>
    public void JoinPlayer(int playerIndex)
    {
        if (joined == null || playerIndex < 0 || playerIndex >= joined.Length)
        {
            return;
        }

        if (joined[playerIndex])
        {
            return;
        }

        joined[playerIndex] = true;
        Debug.Log($"[Local Run] Player {playerIndex + 1} joined.", this);

        SpawnPlayer(playerIndex, SceneRunProfile.Find());
    }

    /// <summary>Remove a player from the run and despawn their body.</summary>
    public void LeavePlayer(int playerIndex)
    {
        if (joined == null || playerIndex < 0 || playerIndex >= joined.Length)
        {
            return;
        }

        joined[playerIndex] = false;

        for (int i = livePlayers.Count - 1; i >= 0; i--)
        {
            if (livePlayers[i] != null &&
                livePlayers[i].PlayerIndex == playerIndex)
            {
                Destroy(livePlayers[i].gameObject);
                livePlayers.RemoveAt(i);
            }
        }
    }

    // ---------------------------------------------------------- IRunContext

    public KeyBindings GetBindings(int playerIndex)
    {
        if (bindings == null || playerIndex < 0 || playerIndex >= bindings.Length)
        {
            return null;
        }

        return bindings[playerIndex];
    }

    public PlayerRunData GetPlayerData(int playerIndex)
    {
        if (playerIndex < 0 || playerIndex >= playerData.Count)
        {
            return null;
        }

        return playerData[playerIndex];
    }

    public System.Random CreateRandom(string streamName)
    {
        return new System.Random(
            SeedUtility.DeriveSeed(Seed, $"Stage_{Stage}_{streamName}"));
    }

    /// <summary>
    /// Rowboat calls this. On the boss island there is nowhere left to sail, so
    /// using the rowboat there means the crew escaped: the run is won.
    /// </summary>
    public void OpenMap()
    {
        if (Outcome != RunOutcome.InProgress)
        {
            return;
        }

        if (IsOnBossNode)
        {
            WinRun();
            return;
        }

        if (JoinedCount == 0)
        {
            Debug.LogWarning("[Local Run] Nobody has joined yet.", this);
            return;
        }

        SceneManager.LoadScene(mapScene);
    }

    // ------------------------------------------------------------- outcome

    public void WinRun()
    {
        if (Outcome != RunOutcome.InProgress)
        {
            return;
        }

        Outcome = RunOutcome.Won;
        outcomeReason = "You escaped the boss island.";
        Time.timeScale = 0f;

        Debug.Log("[Local Run] RUN WON.", this);
    }

    public void LoseRun(string reason)
    {
        if (Outcome != RunOutcome.InProgress)
        {
            return;
        }

        Outcome = RunOutcome.Lost;
        outcomeReason = reason;
        Time.timeScale = 0f;

        Debug.Log($"[Local Run] RUN LOST: {reason}", this);
    }

    public void DamageShip(int amount)
    {
        if (Outcome != RunOutcome.InProgress || amount <= 0)
        {
            return;
        }

        ShipHealth = Mathf.Max(0, ShipHealth - amount);

        if (ShipHealth == 0)
        {
            LoseRun("The ship went down.");
        }
    }

    public void DamagePlayer(int playerIndex, int amount)
    {
        if (Outcome != RunOutcome.InProgress || amount <= 0)
        {
            return;
        }

        PlayerRunData data = GetPlayerData(playerIndex);
        if (data == null)
        {
            return;
        }

        data.Damage(amount);

        // The run is lost only when every joined player is down.
        for (int i = 0; i < maxPlayers; i++)
        {
            if (IsJoined(i))
            {
                PlayerRunData other = GetPlayerData(i);
                if (other != null && other.IsAlive)
                {
                    return;
                }
            }
        }

        LoseRun("The whole crew was lost.");
    }

    /// <summary>Map calls this once every joined player has readied.</summary>
    public void ChooseDestination(int nodeId)
    {
        PendingDestinationId = nodeId;
        Debug.Log($"[Local Run] Sailing to node {nodeId}.", this);
        SceneManager.LoadScene(boatScene);
    }

    /// <summary>The boat leg ended: advance the run and land on the island.</summary>
    public void OnBoatArrived()
    {
        if (PendingDestinationId >= 0)
        {
            CurrentNodeId = PendingDestinationId;
        }

        PendingDestinationId = -1;
        Stage++;

        RunMapNode node = Map.Get(CurrentNodeId);

        Debug.Log(
            $"[Local Run] Arrived at node {CurrentNodeId} " +
            $"({(node != null ? node.kind.ToString() : "unknown")}), stage {Stage}.",
            this);

        SceneManager.LoadScene(SceneForNode(node));
    }

    /// <summary>
    /// Which scene an island node uses. The boss gets its own scene; every
    /// other node picks deterministically from the island list, so the same
    /// seed always gives the same island layout.
    /// </summary>
    private string SceneForNode(RunMapNode node)
    {
        if (node != null && node.kind == RunNodeKind.Boss)
        {
            return bossScene;
        }

        if (islandScenes == null || islandScenes.Length == 0)
        {
            return bossScene;
        }

        int nodeId = node != null ? node.id : 0;
        int pick = Mathf.Abs(
            SeedUtility.DeriveSeed(Seed, $"IslandScene_{nodeId}"))
            % islandScenes.Length;

        return islandScenes[pick];
    }

    /// <summary>Start a brand new run from the menu.</summary>
    public void StartRun()
    {
        BuildRun();
        SceneManager.LoadScene(lobbyScene);
    }

    /// <summary>
    /// Roll a fresh seed, then start. Every run gets a different map, island
    /// mix and layout. Hook this to a "Random Run" button.
    /// </summary>
    public void StartRandomRun()
    {
        randomizeEachRun = true;
        StartRun();
    }

    /// <summary>
    /// Reroll the map without leaving the current scene. Useful in the lobby:
    /// players can keep rerolling until they like the route.
    /// </summary>
    public void RandomizeSeed()
    {
        bool[] previouslyJoined = joined;

        // Lock the rerolled seed in, so it stays put until the next reroll.
        fixedSeed = NewRandomSeed();
        randomizeEachRun = false;
        BuildRun();

        // Keep whoever had already joined; only the run itself is rerolled.
        if (previouslyJoined != null && joined != null)
        {
            int count = Mathf.Min(previouslyJoined.Length, joined.Length);
            for (int i = 0; i < count; i++)
            {
                joined[i] = previouslyJoined[i];
            }
        }

        Debug.Log($"[Local Run] Rerolled map. New seed: {Seed}", this);
    }

    private int NewRandomSeed()
    {
        return Random.Range(1, int.MaxValue);
    }

    /// <summary>
    /// Back to the main menu. The outcome must be cleared or this persistent
    /// object would keep drawing the win/lose overlay on top of the menu.
    /// </summary>
    public void ReturnToMenu()
    {
        Outcome = RunOutcome.InProgress;
        outcomeReason = string.Empty;
        Time.timeScale = 1f;

        SceneManager.LoadScene(menuScene);
    }

    // Drawn by the run manager itself so it appears in every scene.
    private void OnGUI()
    {
        if (Outcome == RunOutcome.InProgress)
        {
            return;
        }

        const float width = 460f;
        const float height = 210f;

        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        GUI.Box(panel, Outcome == RunOutcome.Won ? "YOU WIN" : "YOU LOSE");

        GUI.Label(
            new Rect(panel.x + 20f, panel.y + 42f, width - 40f, 30f),
            outcomeReason);

        GUI.Label(
            new Rect(panel.x + 20f, panel.y + 72f, width - 40f, 30f),
            $"Stage reached: {Stage}     Ship: {ShipHealth}/{maxShipHealth}");

        if (GUI.Button(
            new Rect(panel.x + 20f, panel.y + 118f, 200f, 46f), "Play Again"))
        {
            Time.timeScale = 1f;
            StartRun();
        }

        if (GUI.Button(
            new Rect(panel.x + 240f, panel.y + 118f, 200f, 46f), "Main Menu"))
        {
            ReturnToMenu();
        }
    }

    // ----------------------------------------------------- player spawning

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SpawnPlayersForCurrentScene();
    }

    private void SpawnPlayersForCurrentScene()
    {
        livePlayers.Clear();

        // In multiplayer, Netcode spawns the networked players instead.
        if (GameMode.IsMultiplayer)
        {
            return;
        }

        SceneRunProfile profile = SceneRunProfile.Find();

        if (profile == null || !profile.spawnPlayers)
        {
            return;
        }

        for (int i = 0; i < maxPlayers; i++)
        {
            if (IsJoined(i))
            {
                SpawnPlayer(i, profile);
            }
        }
    }

    private void SpawnPlayer(int playerIndex, SceneRunProfile profile)
    {
        if (GameMode.IsMultiplayer)
        {
            return;
        }

        if (profile == null || !profile.spawnPlayers)
        {
            return;
        }

        if (profile.playerPrefab == null)
        {
            Debug.LogError(
                "[Local Run] SceneRunProfile has no player prefab assigned.",
                profile);
            return;
        }

        Vector3 position = profile.GetSpawnPosition(playerIndex);

        GameObject instance = Instantiate(
            profile.playerPrefab, position, Quaternion.identity);

        PlayerCharacter character = instance.GetComponent<PlayerCharacter>();

        if (character == null)
        {
            Debug.LogError(
                "[Local Run] Player prefab is missing PlayerCharacter.",
                profile.playerPrefab);
            return;
        }

        character.Configure(
            playerIndex, GetBindings(playerIndex), GetPlayerData(playerIndex));

        livePlayers.Add(character);
    }
}
