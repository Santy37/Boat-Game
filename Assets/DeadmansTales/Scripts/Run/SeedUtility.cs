using System;

public static class SeedUtility
{
    /// <summary>
    /// Creates a deterministic sub-seed from a master run seed
    /// and a stable system name.
    ///
    /// Example:
    /// master seed 12345 + "ShipObstacles"
    /// will always produce the same obstacle-system seed.
    /// </summary>
    public static int DeriveSeed(
        int masterSeed,
        string streamName
    )
    {
        unchecked
        {
            // FNV-1a 32-bit offset basis.
            uint hash = 2166136261u;

            // Hash all four bytes of the master seed.
            for (int i = 0; i < 4; i++)
            {
                byte seedByte =
                    (byte)(masterSeed >> (i * 8));

                hash ^= seedByte;
                hash *= 16777619u;
            }

            // Hash the stream name using a stable algorithm.
            if (!string.IsNullOrEmpty(streamName))
            {
                foreach (char character in streamName)
                {
                    hash ^= character;
                    hash *= 16777619u;
                }
            }

            // System.Random accepts signed integers.
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    /// <summary>
    /// Creates an independent deterministic random generator
    /// for one gameplay system.
    /// </summary>
    public static Random CreateRandom(
        int masterSeed,
        string streamName
    )
    {
        int derivedSeed =
            DeriveSeed(masterSeed, streamName);

        return new Random(derivedSeed);
    }
}