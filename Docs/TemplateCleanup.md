# Legacy Template Cleanup

This branch removes the imported 3D gameplay samples and multiplayer sample blocks that are not part of Deadman's Tales.

## Intended retained project areas

- `Assets/DeadmansTales`
- Unity packages required by the custom 2D networking and gameplay systems
- Project settings needed by the current game

## Intended removed sample areas

- `Assets/Blocks`
- `Assets/Core`
- `Assets/Platformer`
- `Assets/Shooter`
- Unity tutorial/readme sample content

## Required validation before merging into `systems`

1. Unity opens with zero compile errors.
2. `Deadman's Tales > Run Technical Smoke Tests` reports 4 passed, 0 failed.
3. `Technical_Runtime_Test` reports PASS in Play Mode.
4. The 2D lobby still spawns the custom network player.
5. Host and client can connect and transition together.
6. No missing-script or missing-prefab warnings appear in Deadman's Tales scenes.

Do not merge this branch into `systems` until all checks pass.
