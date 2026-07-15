# Playtest Logger

Branch: `playtest-logger`

This branch adds telemetry only. It does not modify UI, scenes, prefabs, combat, ship mechanics, enemy AI, inventory, or island content.

## Output

`PlaytestEventLogger` writes newline-delimited JSON files to:

`Application.persistentDataPath/PlaytestLogs`

Each event includes:

- UTC timestamp
- elapsed run time
- local log-session ID
- synchronized run seed
- stage index
- active player count
- actor and target IDs
- cause
- two general numeric values
- optional details text

## Convenience events

The logger includes helpers for:

- player damage
- player downed
- revive start/cancel/complete
- ship damage
- leaks created/repaired
- cannon hits and misses
- enemies defeated
- upgrade selection
- stage completion
- game over

Gameplay programmers call these helpers from their own systems when the event actually occurs. The logger does not decide when damage, death, repair, combat, or upgrades happen.

## Later integration

Do not add the logger to a shared scene yet.

After the technical and UI branches are integrated:

1. Create one persistent `PlaytestEventLogger` GameObject in the startup scene.
2. Add the `PlaytestEventLogger` component.
3. Leave `Begin Session On Start` and `Persist Across Scenes` enabled.
4. Keep `Flush After Every Event` enabled during class playtests.
5. Gameplay systems call the static helper matching their event.

Example:

```csharp
PlaytestEventLogger.LogShipDamage(
    damage: 15f,
    cause: "KrakenTentacle",
    shipHealthAfter: 65f
);
```

## Player questions

Telemetry does not replace post-test questions. Testers should still be asked whether objectives were clear, whether both players had enough to do, what felt overwhelming or slow, why they selected an upgrade, and whether boss attacks were readable.
