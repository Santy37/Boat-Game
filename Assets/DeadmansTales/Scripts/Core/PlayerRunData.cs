using System;
using System.Collections.Generic;

/// <summary>
/// Run-scoped data for one player.
///
/// This lives in the run manager, NOT on the player object, so it survives
/// scene changes and works whether a scene spawns a 2D sprite player or a 3D
/// character. Players are re-created per scene and handed their data back.
/// </summary>
[Serializable]
public class PlayerRunData
{
    public int playerIndex;

    public int maxHealth = 100;
    public int health = 100;
    public int gold;

    public List<string> upgrades = new List<string>();

    public bool IsAlive => health > 0;

    public void Damage(int amount)
    {
        health = Math.Max(0, health - Math.Max(0, amount));
    }

    public void Heal(int amount)
    {
        health = Math.Min(maxHealth, health + Math.Max(0, amount));
    }
}
