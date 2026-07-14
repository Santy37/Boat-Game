using System;
using Unity.Collections;
using Unity.Netcode;

namespace DeadmansTales.Configuration
{
    /// <summary>
    /// Network-serializable copy of BoatRunConfig.
    ///
    /// The host loads JSON once, converts it to this snapshot, and synchronizes
    /// the exact validated values to every client. Clients therefore do not make
    /// authoritative gameplay decisions from their own local override files.
    /// </summary>
    [Serializable]
    public struct NetworkBoatRunConfig :
        INetworkSerializable,
        IEquatable<NetworkBoatRunConfig>
    {
        public int ConfigVersion;
        public FixedString64Bytes Id;
        public int MinimumObstacleCount;
        public int MaximumObstacleCount;
        public float MinimumObstacleSpacing;
        public int MaximumPlacementAttemptsPerObstacle;
        public float EnemySpawnChance;
        public float LootSpawnChance;

        public bool IsValid => ConfigVersion > 0 && Id.Length > 0;

        public static NetworkBoatRunConfig FromRuntimeConfig(
            global::BoatRunConfig config
        )
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            config.Validate();

            return new NetworkBoatRunConfig
            {
                ConfigVersion = config.configVersion,
                Id = new FixedString64Bytes(config.id),
                MinimumObstacleCount = config.minimumObstacleCount,
                MaximumObstacleCount = config.maximumObstacleCount,
                MinimumObstacleSpacing = config.minimumObstacleSpacing,
                MaximumPlacementAttemptsPerObstacle =
                    config.maximumPlacementAttemptsPerObstacle,
                EnemySpawnChance = config.enemySpawnChance,
                LootSpawnChance = config.lootSpawnChance
            };
        }

        public global::BoatRunConfig ToRuntimeConfig()
        {
            global::BoatRunConfig config = new global::BoatRunConfig
            {
                configVersion = ConfigVersion,
                id = Id.ToString(),
                minimumObstacleCount = MinimumObstacleCount,
                maximumObstacleCount = MaximumObstacleCount,
                minimumObstacleSpacing = MinimumObstacleSpacing,
                maximumPlacementAttemptsPerObstacle =
                    MaximumPlacementAttemptsPerObstacle,
                enemySpawnChance = EnemySpawnChance,
                lootSpawnChance = LootSpawnChance
            };

            config.Validate();
            return config;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ConfigVersion);
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref MinimumObstacleCount);
            serializer.SerializeValue(ref MaximumObstacleCount);
            serializer.SerializeValue(ref MinimumObstacleSpacing);
            serializer.SerializeValue(
                ref MaximumPlacementAttemptsPerObstacle
            );
            serializer.SerializeValue(ref EnemySpawnChance);
            serializer.SerializeValue(ref LootSpawnChance);
        }

        public bool Equals(NetworkBoatRunConfig other)
        {
            return
                ConfigVersion == other.ConfigVersion &&
                Id.Equals(other.Id) &&
                MinimumObstacleCount == other.MinimumObstacleCount &&
                MaximumObstacleCount == other.MaximumObstacleCount &&
                MinimumObstacleSpacing.Equals(other.MinimumObstacleSpacing) &&
                MaximumPlacementAttemptsPerObstacle ==
                    other.MaximumPlacementAttemptsPerObstacle &&
                EnemySpawnChance.Equals(other.EnemySpawnChance) &&
                LootSpawnChance.Equals(other.LootSpawnChance);
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkBoatRunConfig other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ConfigVersion;
                hash = hash * 31 + Id.GetHashCode();
                hash = hash * 31 + MinimumObstacleCount;
                hash = hash * 31 + MaximumObstacleCount;
                hash = hash * 31 + MinimumObstacleSpacing.GetHashCode();
                hash = hash * 31 + MaximumPlacementAttemptsPerObstacle;
                hash = hash * 31 + EnemySpawnChance.GetHashCode();
                hash = hash * 31 + LootSpawnChance.GetHashCode();
                return hash;
            }
        }
    }
}
