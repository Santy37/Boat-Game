using System;
using System.Collections;
using DeadmansTales.Networking;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class BoatRunDirector : NetworkBehaviour
{
    public static BoatRunDirector Instance
    {
        get;
        private set;
    }

    [Header("Run Configuration")]
    [SerializeField]
    private string configId =
        "boat_default";

    [Header("Seed Testing")]
    [Tooltip(
        "Enable this to reproduce the exact same run every time."
    )]
    [SerializeField]
    private bool useFixedSeed =
        true;

    [SerializeField]
    private int fixedSeed =
        12345;

    /// <summary>
    /// The server chooses this value.
    /// Every connected client receives the same value.
    /// </summary>
    public readonly NetworkVariable<int> RunSeed =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    /// <summary>
    /// Becomes true only after the server has selected the seed.
    /// </summary>
    public readonly NetworkVariable<bool> SeedReady =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    /// <summary>
    /// The configuration loaded from JSON on this machine.
    /// </summary>
    public BoatRunConfig Config
    {
        get;
        private set;
    }

    /// <summary>
    /// Full path of the JSON file that was loaded.
    /// Useful for debugging and testing.
    /// </summary>
    public string LoadedConfigPath
    {
        get;
        private set;
    }

    public int CurrentSeed =>
        RunSeed.Value;

    public string ConfigId =>
        configId;

    public bool IsRunReady =>
        IsSpawned &&
        SeedReady.Value &&
        RunSeed.Value != 0 &&
        Config != null;

    /// <summary>
    /// Other systems may subscribe to this event.
    ///
    /// The complete BoatRunDirector is passed to the subscriber,
    /// giving access to:
    /// - CurrentSeed
    /// - Config
    /// - CreateRandom(...)
    /// </summary>
    public event Action<BoatRunDirector>
        OnRunReady;

    private bool runReadyDelivered;
    private Coroutine serverInitializationRoutine;

    private void Awake()
    {
        if (
            Instance != null &&
            Instance != this
        )
        {
            Debug.LogWarning(
                "[Boat Run] More than one BoatRunDirector exists."
            );
        }

        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Register NetworkVariable callbacks here.
        RunSeed.OnValueChanged +=
            HandleRunSeedChanged;

        SeedReady.OnValueChanged +=
            HandleSeedReadyChanged;

        // Every game instance loads the same local config file.
        LoadRunConfig();

        if (IsServer)
        {
            serverInitializationRoutine = StartCoroutine(
                InitializeServerRun()
            );
        }

        // A late client may already have synchronized values
        // by the time OnNetworkSpawn runs.
        TryDeliverRunReady();
    }

    public override void OnNetworkDespawn()
    {
        if (serverInitializationRoutine != null)
        {
            StopCoroutine(serverInitializationRoutine);
            serverInitializationRoutine = null;
        }

        RunSeed.OnValueChanged -=
            HandleRunSeedChanged;

        SeedReady.OnValueChanged -=
            HandleSeedReadyChanged;

        base.OnNetworkDespawn();
    }

    private IEnumerator InitializeServerRun()
    {
        const float maximumRunStateWait = 5f;
        float elapsed = 0f;

        while (
            (
                NetworkRunState.Instance == null ||
                !NetworkRunState.Instance.IsInitialized
            ) &&
            elapsed < maximumRunStateWait
        )
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        NetworkRunState runState = NetworkRunState.Instance;
        bool usedSharedRunState =
            runState != null && runState.IsInitialized;

        int selectedSeed = usedSharedRunState
            ? runState.Seed
            : SelectRunSeed();

        RunSeed.Value = selectedSeed;
        SeedReady.Value = true;

        if (usedSharedRunState)
        {
            runState.SetStatusServer(NetworkRunStatus.Playing);
        }

        Debug.Log(
            $"[Boat Run] SERVER initialized run.\n" +
            $"Seed: {selectedSeed}\n" +
            $"Shared Run State: {usedSharedRunState}\n" +
            $"Config ID: {configId}\n" +
            $"Config Path: {LoadedConfigPath}"
        );

        serverInitializationRoutine = null;
    }

    /// <summary>
    /// Creates an independent deterministic random generator
    /// for one gameplay system.
    ///
    /// Examples:
    ///
    /// CreateRandom("ShipObstacles")
    /// CreateRandom("EnemySpawns")
    /// CreateRandom("Loot")
    /// CreateRandom("Weather")
    /// </summary>
    public System.Random CreateRandom(
        string streamName
    )
    {
        if (!IsRunReady)
        {
            throw new InvalidOperationException(
                "BoatRunDirector.CreateRandom was called " +
                "before the run was ready."
            );
        }

        if (string.IsNullOrWhiteSpace(streamName))
        {
            throw new ArgumentException(
                "Random stream name cannot be empty.",
                nameof(streamName)
            );
        }

        return SeedUtility.CreateRandom(
            RunSeed.Value,
            streamName
        );
    }

    private void LoadRunConfig()
    {
        Config =
            BoatRunConfigLoader.Load(
                configId,
                out string loadedFrom
            );

        LoadedConfigPath =
            loadedFrom;

        Debug.Log(
            $"[Boat Run] Local config ready.\n" +
            $"ID: {Config.id}\n" +
            $"Version: {Config.configVersion}\n" +
            $"Loaded From: {LoadedConfigPath}"
        );
    }

    private int SelectRunSeed()
    {
        if (useFixedSeed)
        {
            // Never allow zero because this project uses
            // zero as "seed not initialized yet."
            if (fixedSeed == 0)
            {
                return 1;
            }

            return fixedSeed;
        }

        return UnityEngine.Random.Range(
            1,
            int.MaxValue
        );
    }

    private void HandleRunSeedChanged(
        int previousValue,
        int currentValue
    )
    {
        TryDeliverRunReady();
    }

    private void HandleSeedReadyChanged(
        bool previousValue,
        bool currentValue
    )
    {
        TryDeliverRunReady();
    }

    private void TryDeliverRunReady()
    {
        if (!IsRunReady)
        {
            return;
        }

        if (runReadyDelivered)
        {
            return;
        }

        runReadyDelivered = true;

        Debug.Log(
            $"[Boat Run] LOCAL RUN READY.\n" +
            $"Seed: {RunSeed.Value}\n" +
            $"Config: {Config.id}"
        );

        OnRunReady?.Invoke(
            this
        );
    }

}
