# Seeded Island Content Framework

Branch: `seeded-island-content`

This branch adds backend-only island generation hooks. It does not modify UI scenes, gameplay scenes, prefabs, enemy logic, item logic, or artwork.

## What it adds

- `SeededContentCategory`
- `SeededSpawnMarker2D`
- `SeededIslandContentGenerator`

The server uses the synchronized `StageSeedContext` to decide which markers activate and which registered `NetworkObject` prefab appears at each marker.

Each marker receives its own deterministic stream based on:

- master run seed
- current stage index
- content category
- scene hierarchy location

Adding a loot marker therefore does not change enemy results.

## Teammate boundary

Designers and gameplay programmers own:

- island layout and artwork
- marker placement
- enemy prefabs
- chest and pickup prefabs
- healing behavior
- reward behavior
- hazard behavior

The technical framework only selects and network-spawns the supplied prefabs.

## Later scene setup

Do not perform this setup until the technical branches are integrated with the gameplay scene.

1. Add one `StageSeedProvider` to the island scene.
2. Add one `SeededIslandContentGenerator` to the same GameObject.
3. Place `SeededSpawnMarker2D` components at optional content locations.
4. Assign one or more network prefabs to each marker.
5. Ensure each prefab has a root `NetworkObject` and is registered in the NetworkManager prefab list.
6. Keep required locations such as player spawn, tutorial items, exits, and boss arenas fixed and hand-authored.

## Important authority rule

Only the server generates content. Clients receive the spawned `NetworkObject` instances through Netcode for GameObjects.
