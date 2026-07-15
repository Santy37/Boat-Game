namespace DeadmansTales.WorldGeneration
{
    /// <summary>
    /// High-level content groups used to keep seeded random streams independent.
    /// Changing loot markers will not change enemy marker results.
    /// </summary>
    public enum SeededContentCategory : byte
    {
        Enemy,
        Loot,
        Healing,
        Prop,
        Reward,
        Hazard
    }
}
