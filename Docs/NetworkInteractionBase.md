# Network Interaction Base

Branch: `network-interaction-base`

This branch adds shared server-authoritative interaction plumbing without implementing any game mechanic or changing a scene, prefab, UI script, or input setup.

## Added scripts

- `NetworkInteractionController2D`
- `NetworkInteractable2D`

## Ownership boundary

The technical/networking layer owns:

- sending an interaction request from the locally owned player
- verifying that the requesting client owns that player
- verifying that the target NetworkObject still exists
- verifying that the interaction is enabled
- verifying server-side interaction distance
- preventing the client from directly changing shared state

Gameplay teammates still own:

- chest rewards
- repair behavior
- cannon behavior
- item pickup effects
- exit requirements
- revive behavior
- animations, sound, prompts, and UI

## Later player-prefab integration

After the current UI work is merged and the team agrees on player input:

1. Add `NetworkInteractionController2D` to the network player prefab.
2. Keep the default maximum distance at 2 units initially.
3. The player input script finds a nearby `NetworkInteractable2D`.
4. On interact input, call:

```csharp
interactionController.RequestInteraction(target);
```

Do not directly call mechanic methods from the client.

## Creating a concrete mechanic

A teammate can create a chest, repair point, cannon, pickup, or exit by inheriting from `NetworkInteractable2D`:

```csharp
public sealed class ExampleChest : NetworkInteractable2D
{
    protected override bool CanInteractServer(
        NetworkInteractionController2D interactor
    )
    {
        return true;
    }

    protected override void PerformInteractionServer(
        NetworkInteractionController2D interactor
    )
    {
        // Server-authoritative chest behavior belongs here.
    }
}
```

For a one-use object, disable `Allow Repeated Interaction` in the Inspector. The base class will disable the interaction after the first accepted use.

## Not done in this branch

- no interact key is assigned
- no player prefab is edited
- no scene is edited
- no UI prompt is created
- no chest, cannon, repair, pickup, revive, or exit mechanic is created

Those integrations happen only after their owning teammate's work is ready.
