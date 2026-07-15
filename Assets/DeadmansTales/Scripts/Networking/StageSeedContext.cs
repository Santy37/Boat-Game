using System;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Immutable snapshot of the synchronized values needed to create
    /// deterministic random streams for one stage.
    ///
    /// Gameplay systems should request their own named stream instead of
    /// sharing one System.Random instance. This prevents a change to loot
    /// generation from changing enemy or obstacle generation.
    /// </summary>
    public readonly struct StageSeedContext
    {
        public int MasterSeed { get; }

        public int StageIndex { get; }

        public string ConfigId { get; }

        public int ConfigVersion { get; }

        public bool IsValid =>
            MasterSeed != 0 &&
            StageIndex > 0;

        public StageSeedContext(
            int masterSeed,
            int stageIndex,
            string configId,
            int configVersion
        )
        {
            if (masterSeed == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(masterSeed),
                    "The master seed may not be zero."
                );
            }

            if (stageIndex < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(stageIndex),
                    "The stage index must be at least one."
                );
            }

            MasterSeed = masterSeed;
            StageIndex = stageIndex;
            ConfigId = string.IsNullOrWhiteSpace(configId)
                ? "boat_default"
                : configId.Trim();
            ConfigVersion = Math.Max(1, configVersion);
        }

        /// <summary>
        /// Creates a stable stream name that includes the stage number.
        /// Example: Stage_2_EnemySpawns.
        /// </summary>
        public string BuildStreamName(string systemName)
        {
            string safeSystemName = ValidateSystemName(systemName);

            return $"Stage_{StageIndex}_{safeSystemName}";
        }

        /// <summary>
        /// Returns the deterministic integer seed for one stage system.
        /// Useful for debugging, logging, or APIs that accept an integer seed.
        /// </summary>
        public int DeriveSeed(string systemName)
        {
            return SeedUtility.DeriveSeed(
                MasterSeed,
                BuildStreamName(systemName)
            );
        }

        /// <summary>
        /// Creates an independent deterministic random generator for one
        /// stage system.
        /// </summary>
        public Random CreateRandom(string systemName)
        {
            return SeedUtility.CreateRandom(
                MasterSeed,
                BuildStreamName(systemName)
            );
        }

        public override string ToString()
        {
            return
                $"MasterSeed={MasterSeed}, " +
                $"Stage={StageIndex}, " +
                $"Config={ConfigId} v{ConfigVersion}";
        }

        private static string ValidateSystemName(string systemName)
        {
            if (string.IsNullOrWhiteSpace(systemName))
            {
                throw new ArgumentException(
                    "A seeded system name cannot be empty.",
                    nameof(systemName)
                );
            }

            return systemName.Trim();
        }
    }
}
