# Host-Authoritative Run Configuration

Branch: `host-config-authority`

This branch prevents each computer's local JSON override from becoming a separate source of gameplay truth.

## What it adds

- `NetworkBoatRunConfig`
- `NetworkRunConfigAuthority`

The host loads and validates the selected `BoatRunConfig` JSON file. The validated values are converted into a network-serializable snapshot and synchronized to every client.

Clients should use `NetworkRunConfigAuthority.RequireRuntimeConfig()` or `TryGetRuntimeConfig(...)` instead of loading their own authoritative gameplay configuration.

## Why this is needed

`BoatRunConfigLoader` intentionally allows external overrides beside the executable. Without this authority layer, the host and client could load different obstacle counts, spawn chances, or future balancing values.

With this layer:

- the host chooses the config
- the host loads the JSON
- the host publishes the exact validated values
- clients receive the host copy
- the config ID and version are copied into `NetworkRunState`

## Ownership boundary

This branch does not modify:

- UI scenes or scripts
- island layouts
- enemy logic
- combat
- inventory
- ship mechanics
- the existing JSON loader

## Later integration

Do not add the component to a shared scene yet.

After the technical and UI branches are integrated:

1. Add `NetworkRunConfigAuthority` to a persistent spawned `NetworkObject`.
2. Ensure it spawns before gameplay systems request configuration.
3. Replace gameplay calls to `BoatRunConfigLoader.Load(...)` with the synchronized authority API.
4. Only host/server code may call `SelectConfigServer(...)`.

The current default config remains `boat_default`.
