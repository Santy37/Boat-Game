# Online Lobby Backend

Branch: `agent/online-lobby-backend`

This branch intentionally does not modify the `UI` branch files. It only adds the multiplayer backend under `Assets/DeadmansTales/Scripts/Networking`.

## Files this branch must not touch

The UI branch currently owns:

- `Assets/DeadmansTales/Scenes/MainMenu.unity`
- `Assets/DeadmansTales/Scenes/Lobby_Island_2D.unity`
- `Assets/DeadmansTales/Scripts/UI/MainMenuManager.cs`
- `Assets/DeadmansTales/Scripts/UI/PauseMenu.cs`
- `ProjectSettings/EditorBuildSettings.asset`

Merge the backend branch and UI branch before doing the Inspector wiring below.

## What OnlineLobbyService owns

`OnlineLobbyService` handles:

- Unity Services initialization
- Anonymous player authentication
- Relay-backed session creation
- Joining by six-to-eight-character lobby code
- Two-player session enforcement
- Leaving a session
- Session-code updates
- Player-count updates
- Host/client status
- Human-readable status and error messages
- Persistence across scene loads

It does not create or redesign any menus.

## Required objects in MainMenu after both branches are merged

Open `Assets/DeadmansTales/Scenes/MainMenu.unity`.

1. Drag `Assets/Core/Prefabs/[BB] NetworkManager.prefab` into the scene.
2. Create an empty GameObject named `OnlineLobbyService`.
3. Add the `OnlineLobbyService` component.
4. Leave `Session Type` as `dead-mans-tale`.
5. Leave `Session Name` as `Dead Man's Tale`.
6. Leave `Use Player Name` enabled.
7. Leave `Persist Across Scenes` enabled.

Do not add a second NetworkManager to later scenes. The existing GameNetworkManager prefab persists across scene loads.

Do not add the imported UnityServices prefab when using this service. OnlineLobbyService initializes Unity Services and signs the player in anonymously itself.

## UI button and input contract

The UI may keep its existing `MainMenuManager`. Wire the controls to `OnlineLobbyService` through Button/InputField events.

### Host Game button

Add this OnClick listener:

- `OnlineLobbyService.CreateLobby()`

Do not call `NetworkManager.StartHost()` directly.

### Join-code input

Add this TMP_InputField OnValueChanged listener:

- `OnlineLobbyService.SetJoinCode(string)`

The service removes spaces and dashes and converts letters to uppercase.

### Confirm Join button

Add this OnClick listener:

- `OnlineLobbyService.JoinLobby()`

Do not call `NetworkManager.StartClient()` directly.

### Leave Lobby buttons

Add this OnClick listener before returning to the menu panel:

- `OnlineLobbyService.LeaveLobby()`

### Copy Code button

Add this OnClick listener:

- `OnlineLobbyService.CopySessionCode()`

## Output events available in the Inspector

The service exposes serialized UnityEvents:

- `On Status Changed (string)`
- `On Session Code Changed (string)`
- `On Player Count Changed (int)`
- `On Host Changed (bool)`
- `On Busy Changed (bool)`
- `On Session Presence Changed (bool)`
- `On Join Code Validity Changed (bool)`
- `On Connection State Changed (LobbyConnectionState)`

The UI branch can use these without moving networking logic into `MainMenuManager`.

Recommended uses:

- Status text subscribes to `On Status Changed`.
- Host lobby code text subscribes to `On Session Code Changed`.
- Player list/count logic subscribes to `On Player Count Changed`.
- Host-only controls subscribe to `On Host Changed` through a small UI-only handler.
- Join button interactability subscribes to `On Join Code Validity Changed` through a small UI-only handler.
- Loading indicators subscribe to `On Busy Changed`.

## Important panel behavior

The current UI branch has temporary `PreviewHostLobby` and `PreviewClientLobby` methods. Do not open those panels before the online operation succeeds.

The final UI integration should switch panels only after `OnlineLobbyService.IsInSession` becomes true. Host/client selection should use `OnlineLobbyService.IsHost`.

That final panel adapter belongs to the UI branch because it references UI panels. The backend branch should remain unaware of `MainMenuManager`, TMP labels, buttons, or panel objects.

## First test

Use one Editor instance and one standalone Windows build.

1. Start the host copy.
2. Press Host Game.
3. Confirm a join code appears.
4. Start the client copy.
5. Paste the code.
6. Press Join.
7. Confirm both copies report two players.
8. Confirm each copy controls only its own player after entering the lobby scene.
9. Leave from the client.
10. Confirm the host player count returns to one.
11. Leave from the host.
12. Confirm both copies can create or join a new session without restarting the application.

## Expected setup error

If the Console says:

`No NetworkManager exists in the MainMenu scene.`

then `[BB] NetworkManager.prefab` was not added to MainMenu.

## Out of scope for this branch

- Main-menu artwork or layout
- Host/client panel transitions
- Player-ready UI
- Starting the gameplay scene
- Persistent run seed and stage state
- Combat, enemies, inventory, repairs, or upgrades

Those will be added in later branches after create/join/leave works reliably.
