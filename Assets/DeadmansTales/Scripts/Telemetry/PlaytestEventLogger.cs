using System;
using System.IO;
using System.Text;
using DeadmansTales.Networking;
using UnityEngine;

namespace DeadmansTales.Telemetry
{
    /// <summary>
    /// Writes gameplay telemetry as newline-delimited JSON under
    /// Application.persistentDataPath/PlaytestLogs.
    ///
    /// This class intentionally has no dependency on combat, ship, enemy,
    /// inventory, island, or UI implementations. Those systems call the
    /// generic LogEvent method or one of the convenience helpers when their
    /// own event occurs.
    /// </summary>
    public sealed class PlaytestEventLogger : MonoBehaviour
    {
        public static PlaytestEventLogger Instance
        {
            get;
            private set;
        }

        [Header("Lifecycle")]
        [SerializeField]
        private bool persistAcrossScenes = true;

        [SerializeField]
        private bool beginSessionOnStart = true;

        [Header("Output")]
        [SerializeField]
        private string filePrefix = "playtest";

        [SerializeField]
        private bool flushAfterEveryEvent = true;

        [SerializeField]
        private bool echoEventsToConsole;

        private readonly object writerLock = new object();
        private StreamWriter writer;
        private float sessionStartRealtime;
        private string logSessionId = string.Empty;
        private string currentFilePath = string.Empty;

        public bool IsSessionOpen => writer != null;

        public string LogSessionId => logSessionId;

        public string CurrentFilePath => currentFilePath;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[Playtest Logger] Duplicate logger destroyed.",
                    this
                );
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start()
        {
            if (beginSessionOnStart)
            {
                BeginSession();
            }
        }

        private void OnApplicationQuit()
        {
            EndSession("ApplicationQuit");
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                EndSession("LoggerDestroyed");
                Instance = null;
            }
        }

        private void OnValidate()
        {
            filePrefix = SanitizeFilePart(filePrefix);
            if (string.IsNullOrWhiteSpace(filePrefix))
            {
                filePrefix = "playtest";
            }
        }

        public void BeginSession()
        {
            if (IsSessionOpen)
            {
                return;
            }

            string directoryPath = Path.Combine(
                Application.persistentDataPath,
                "PlaytestLogs"
            );
            Directory.CreateDirectory(directoryPath);

            logSessionId =
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" +
                Guid.NewGuid().ToString("N").Substring(0, 8);

            currentFilePath = Path.Combine(
                directoryPath,
                $"{filePrefix}_{logSessionId}.jsonl"
            );

            writer = new StreamWriter(
                currentFilePath,
                false,
                new UTF8Encoding(false)
            );
            sessionStartRealtime = Time.realtimeSinceStartup;

            LogEvent(
                "SessionStarted",
                details: $"UnityVersion={Application.unityVersion}; " +
                         $"Platform={Application.platform}"
            );

            Debug.Log(
                $"[Playtest Logger] Writing to: {currentFilePath}",
                this
            );
        }

        public void EndSession(string reason = "Manual")
        {
            if (!IsSessionOpen)
            {
                return;
            }

            LogEvent("SessionEnded", cause: reason);

            lock (writerLock)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        public void LogEvent(
            string eventName,
            string actorId = "",
            string targetId = "",
            string cause = "",
            float value = 0f,
            float secondaryValue = 0f,
            string details = ""
        )
        {
            if (string.IsNullOrWhiteSpace(eventName))
            {
                Debug.LogWarning(
                    "[Playtest Logger] Ignored event with an empty name.",
                    this
                );
                return;
            }

            if (!IsSessionOpen)
            {
                BeginSession();
            }

            NetworkRunState runState = NetworkRunState.Instance;

            PlaytestEventRecord record = new PlaytestEventRecord
            {
                eventName = eventName.Trim(),
                utcTimestamp = DateTime.UtcNow.ToString("O"),
                elapsedSeconds = Mathf.Max(
                    0f,
                    Time.realtimeSinceStartup - sessionStartRealtime
                ),
                logSessionId = logSessionId,
                runSeed = runState != null ? runState.Seed : 0,
                stageIndex = runState != null ? runState.StageIndex : 0,
                playerCount = runState != null
                    ? runState.ActivePlayerCount.Value
                    : 0,
                actorId = actorId ?? string.Empty,
                targetId = targetId ?? string.Empty,
                cause = cause ?? string.Empty,
                value = value,
                secondaryValue = secondaryValue,
                details = details ?? string.Empty
            };

            WriteRecord(record);
        }

        public static void Log(
            string eventName,
            string actorId = "",
            string targetId = "",
            string cause = "",
            float value = 0f,
            float secondaryValue = 0f,
            string details = ""
        )
        {
            if (Instance == null)
            {
                Debug.LogWarning(
                    $"[Playtest Logger] No logger exists for '{eventName}'."
                );
                return;
            }

            Instance.LogEvent(
                eventName,
                actorId,
                targetId,
                cause,
                value,
                secondaryValue,
                details
            );
        }

        public static void LogPlayerDamage(
            string playerId,
            float damage,
            string cause,
            float healthAfter
        )
        {
            Log(
                "PlayerDamage",
                actorId: playerId,
                cause: cause,
                value: damage,
                secondaryValue: healthAfter
            );
        }

        public static void LogPlayerDowned(
            string playerId,
            string cause,
            float shipHealth
        )
        {
            Log(
                "PlayerDowned",
                actorId: playerId,
                cause: cause,
                secondaryValue: shipHealth
            );
        }

        public static void LogRevive(
            string action,
            string reviverId,
            string downedPlayerId,
            float durationSeconds = 0f
        )
        {
            Log(
                $"Revive{action}",
                actorId: reviverId,
                targetId: downedPlayerId,
                value: durationSeconds
            );
        }

        public static void LogShipDamage(
            float damage,
            string cause,
            float shipHealthAfter
        )
        {
            Log(
                "ShipDamage",
                cause: cause,
                value: damage,
                secondaryValue: shipHealthAfter
            );
        }

        public static void LogLeak(
            string action,
            string leakId,
            float shipHealth,
            float durationSeconds = 0f
        )
        {
            Log(
                $"Leak{action}",
                targetId: leakId,
                value: durationSeconds,
                secondaryValue: shipHealth
            );
        }

        public static void LogCannonShot(
            string playerId,
            bool hit,
            int ammoAfter,
            string targetId = ""
        )
        {
            Log(
                hit ? "CannonHit" : "CannonMiss",
                actorId: playerId,
                targetId: targetId,
                value: ammoAfter
            );
        }

        public static void LogEnemyDefeated(
            string enemyType,
            string killerId,
            float combatDurationSeconds = 0f
        )
        {
            Log(
                "EnemyDefeated",
                actorId: killerId,
                targetId: enemyType,
                value: combatDurationSeconds
            );
        }

        public static void LogUpgradeSelected(
            string upgradeId,
            string selectedBy,
            string offeredUpgrades = ""
        )
        {
            Log(
                "UpgradeSelected",
                actorId: selectedBy,
                targetId: upgradeId,
                details: offeredUpgrades
            );
        }

        public static void LogStageCompleted(float completionSeconds)
        {
            Log("StageCompleted", value: completionSeconds);
        }

        public static void LogGameOver(
            string reason,
            float shipHealth,
            string details = ""
        )
        {
            Log(
                "GameOver",
                cause: reason,
                secondaryValue: shipHealth,
                details: details
            );
        }

        private void WriteRecord(PlaytestEventRecord record)
        {
            string json = JsonUtility.ToJson(record);

            lock (writerLock)
            {
                writer.WriteLine(json);

                if (flushAfterEveryEvent)
                {
                    writer.Flush();
                }
            }

            if (echoEventsToConsole)
            {
                Debug.Log($"[Playtest Event] {json}", this);
            }
        }

        private static string SanitizeFilePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = value.Trim();
            foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(invalidCharacter, '_');
            }

            return result;
        }
    }
}
