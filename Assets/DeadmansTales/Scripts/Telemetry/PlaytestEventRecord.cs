using System;

namespace DeadmansTales.Telemetry
{
    /// <summary>
    /// One newline-delimited JSON record written by PlaytestEventLogger.
    /// Fields stay generic so gameplay systems can log events without the
    /// logger depending on combat, ship, enemy, inventory, or UI classes.
    /// </summary>
    [Serializable]
    public sealed class PlaytestEventRecord
    {
        public string eventName;
        public string utcTimestamp;
        public float elapsedSeconds;
        public string logSessionId;
        public int runSeed;
        public int stageIndex;
        public int playerCount;
        public string actorId;
        public string targetId;
        public string cause;
        public float value;
        public float secondaryValue;
        public string details;
    }
}
