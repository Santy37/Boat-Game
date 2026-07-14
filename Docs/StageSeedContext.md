# Stage Seed Context

Branch: `agent/stage-seed-context`

This branch is stacked on `agent/network-run-state` and does not modify scenes, UI scripts, prefabs, or build settings.

## Purpose

Every procedural system receives its own deterministic random stream for the current stage.

Examples:

- `EnemySpawns`
- `Loot`
- `Props`
- `Weather`
- `Rewards`
- `ShipObstacles`
- `KrakenPattern`

For a master seed of `12345` and stage `2`, the enemy stream name becomes:

`Stage_2_EnemySpawns`

Changing the number of loot random calls will not change enemy placement because the systems do not share one random generator.

## Future scene setup

After the persistent `NetworkRunState` prefab exists, add one `StageSeedProvider` component to each playable stage scene.

Do not add it to the UI branch yet. Scene integration will happen on a dedicated integration branch.

## Gameplay-system usage

A generator may receive a reference to `StageSeedProvider` and request a stream:

```csharp
System.Random rng = stageSeedProvider.CreateRandom("EnemySpawns");
```

It may also retrieve the exact derived seed for logs or debugging:

```csharp
int seed = stageSeedProvider.DeriveSeed("EnemySpawns");
```

The same master seed, stage index, and stream name always recreate the same result.

## Ownership rule

Only the host chooses and writes the master seed and current stage through `NetworkRunState`.

The provider does not choose seeds and does not generate gameplay objects. It only gives deterministic random streams to the gameplay systems created by the team.
