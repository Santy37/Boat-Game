# Enemy Ship Battles (At Sea)

Branch: `enemy-ship-battles` (proposed)

Design plan for adding enemy ships to `Boat_Gameplay_2D`: cannon + sword pirates, a
health/sink-level pair on both boats, boarding and melee, and cleanup on
sink/lose.

## Direct answers first

**Should we duplicate the boat scene for this?** No. `Boat_Gameplay_2D` is the
one sea-stage scene, and the seed system already exists specifically so one
scene can produce different content every run. `BoatObstacleGenerator` is a
working example of exactly that: it waits for `BoatRunDirector.IsRunReady`,
pulls a deterministic `System.Random` for a named stream, and spawns
`NetworkObject` prefabs into a `BoxCollider2D` area. Enemy ships should be
generated the same way, in the same scene, not a second copy of it. Two
copies of the boat scene means every helm/cannon/leak fix has to be ported
twice, and it breaks the "one seed, one scene, different outcomes" model the
rest of the run is built on.

**Should we prefab the boat?** Yes, but only the *enemy* ship. The player's
ship is currently scene-authored (one `Ship` object with `ShipHelm` and
`ShipCannon` wired to scene references directly) — fine, because there's
only ever one. Enemy ships need to spawn N-per-run at seeded positions, so
each one has to be a self-contained `NetworkObject` prefab: hull, cannon
mounts, pirate spawn points, and its own `NetworkShipHealth` all packaged
together, the same way `BoatObstacleGenerator`'s obstacles are prefabs.

**Should the seed setup drive it?** Yes — add a new named stream (e.g.
`"EnemyShips"`) via `BoatRunDirector.CreateRandom("EnemyShips")`, and add
count/spacing/difficulty fields to `boat_default.json` next to
`minimumObstacleCount` etc. Same pattern, new stream name, own config
fields, so it doesn't cross-pollinate with obstacle or loot randomness (that
isolation is the whole point of `StageSeedContext`).

## What already exists to build on

- `NetworkShipHealth` — server-authoritative HP for the player's ship,
  fires `ShipSunk` and fails the run via `NetworkRunState` at 0 HP. Reusable
  as-is for the enemy ship's Health meter (just a second instance on the
  enemy prefab).
- `NetworkShipLeak` / `NetworkShipRepairStation` / `ShipLeakDirector` —
  `NetworkInteractable2D`-based hazard/repair loop, server-validated. This
  is the pattern to copy for "board the enemy ship and destroy it from the
  inside."
- `Enemy` / `EnemyAI` / `EnemyAttack` — full networked melee-enemy stack
  already used on the islands: server-authoritative health with hit-flash
  and a death delay before despawn, and a wander/chase/attack state
  machine. This is the pirate crew, close to unchanged.
- `PlayerHealth` — server-authoritative player HP/death/revive, already
  what a boarding pirate's sword would damage.
- `ShipHelm` / `ShipCannon` / `Cannonball` — **these are client-local, not
  networked.** They're plain `MonoBehaviour`s driven by `PlayerCharacter`
  input, and `Cannonball` just does a client-side `Instantiate`/`Destroy`.
  That's a real gap: enemy-ship damage has to be server-authoritative (like
  everything else in this list) or clients can desync on how much damage a
  shot did, or double-count hits. See the open question below.
- `BoatRunDirector` + `SeedUtility` + `StageSeedProvider` — the seed
  plumbing. `CreateRandom(streamName)` is already there and working.
- `BoatObstacleGenerator` — literally a template for "seeded spawner that
  instantiates and `Spawn()`s NetworkObject prefabs in an area." Copy this
  file's shape for the enemy-ship spawner.

## Open question to settle with the team before building

Cannon fire currently never touches the server. For enemy ships to have a
believable, non-exploitable Health/SinkLevel, player cannon fire needs to
go through a `ServerRpc` that the server validates and resolves (spawn the
ball server-side, or at least compute the hit server-side), matching how
`Enemy.TakeDamage`, `PlayerHealth.TakeDamage`, and `NetworkShipHealth.
TakeDamageServer` all work. Recommend converting `Cannonball` into a
networked projectile (or doing a server-side raycast/overlap check at fire
time) as part of this feature, rather than carrying the client-authority
gap into a new combat system.

## Enemy ship prefab

New prefab, e.g. `Assets/DeadmansTales/Prefabs/EnemyShip.prefab`:

- Root `NetworkObject`, scaled-down hull sprite (reuse/retint the player
  ship art).
- `NetworkShipHealth` — separate `maximumHealth` tuning from the player
  ship (smaller/weaker feels right for a "cleared in one engagement"
  enemy).
- A new `SinkLevel` NetworkVariable (see below) alongside Health.
- 1–2 `EnemyShipCannon` mount points (new script, see below).
- 2–3 pirate spawn points, each spawning an `Enemy` + `EnemyAI` +
  `EnemyAttack` prefab (sword pirates — reuse as-is).
- A new `EnemyShipApproach` script (see below) for closing distance on the
  player's ship.
- A `BoardingPoint` trigger (new `NetworkInteractable2D`) once in range.

## Health vs. Sink Level (both boats)

Two separate meters, matching what you described:

- **Health** — general structural HP. Anything that damages the hull chips
  this down: cannonballs, boarded sabotage. `NetworkShipHealth` already
  does this.
- **Sink Level** — a second `NetworkVariable<float>` (0–100) that rises
  faster on a *direct* cannon hit than a glancing one, and also rises from
  boarded sabotage. At 100, the ship sinks regardless of remaining Health.
  This gives cannons a "how well did you aim" payoff distinct from raw
  damage, and gives boarding parties a second way to finish a ship off
  without needing to out-damage its cannons.

Suggested new component `NetworkShipSinkMeter` (sits next to
`NetworkShipHealth`, same server-authoritative style):

```csharp
public bool ApplyCannonHitServer(float damage, float directness01)
{
    // directness01: 1.0 = dead-center hit, tapering to 0 at the hull edge.
    // Health takes the raw damage; SinkLevel takes damage * directness,
    // so a grazing hit barely floods the ship even if it still hurts HP.
}
```

`directness01` can come from how close the cannonball's impact point was to
the target ship's center vs. its collider bounds — cheap to compute at
impact time, no new aiming mechanics needed for the shooter.

Sinking (either meter maxing out / Health hitting 0) triggers the same
cleanup path on both ships:

- Player ship sinking → already wired: `NetworkShipHealth.ShipSunk` sets
  `NetworkRunState` to `Failed` (game over).
- Enemy ship sinking → new: despawn every pirate on it, despawn the cannons
  and boarding trigger, then despawn the hull itself — mirror `Enemy.
  DespawnAfterDeath()`'s short delay-then-despawn so there's a sink
  animation/beat instead of an instant pop.

## Combat loop

**Player vs. enemy ship, at range:**
- Cannons (server-validated per the open question above) hit the enemy
  ship's `NetworkShipHealth`/`SinkLevel` pair.
- Small-arms/gun fire (new — no existing gun system) can plausibly target
  visible pirates on the enemy deck directly, thinning the crew without
  touching the enemy ship's Health/SinkLevel at all, exactly as you
  described ("shoot enemies with guns"). This reuses `Enemy.TakeDamage`
  unchanged; only the projectile/weapon is new.

**Enemy ship vs. player, at range:**
- `EnemyShipCannon` (new) — a server-side `NetworkBehaviour`, not a copy
  of the player's local `ShipCannon`. On a timer, picks a target point on
  the player's ship (with some spread) and damages `NetworkShipHealth` /
  the sink meter using the same directness-based split as above.
- Only fires once the enemy ship is "engaged" (in range) and stops once
  either its own crew is dead or it's sunk — matching "shoot them or they
  slowly get closer if not dealt with."

**Approach behavior (`EnemyShipApproach`, new):**
- Small state machine, same shape as `EnemyAI`'s Wander/Chase/Return:
  Idle → Approaching (while any crew alive and ship not sunk) → Engaged
  (in cannon/boarding range, stops closing) → Sunk.
- "Dealt with" = either its `NetworkShipHealth` is 0/`SinkLevel` maxed, or
  every pirate aboard it is dead — either should stop it from continuing to
  close.

**Boarding:**
- Once ships are close enough, a `BoardingPoint` (`NetworkInteractable2D`,
  same base class as the repair station) becomes interactable and moves the
  player onto the enemy deck (reuses the `EnterStation`-style
  teleport-and-reface pattern `ShipCannon`/`ShipHelm` already use for
  seating a player, just without freezing them — they need full movement
  to fight).
- On the enemy deck: sword fights against the pirates use `PlayerAttack`
  vs. `Enemy`/`EnemyAI`/`EnemyAttack` completely unchanged — it's the same
  combat already running on the islands, just relocated.
- "Destroying" the enemy ship while aboard = one or more sabotage
  `NetworkInteractable2D` props (magazine, wheel, hull planking — reuse the
  leak/repair interaction shape) that call `NetworkShipSinkMeter.
  ApplyCannonHitServer`-equivalent damage on interact, on a cooldown so a
  boarding party can meaningfully speed up a sink but not insta-kill a ship
  the moment they land.

## Seed integration

1. Add fields to `boat_default.json`: `minimumEnemyShipCount`,
   `maximumEnemyShipCount`, `minimumEnemyShipSpacing`, and whatever
   difficulty knobs matter (crew count range, cannon damage range).
2. New `EnemyShipSpawner` (new script, sibling to `BoatObstacleGenerator`,
   same wait-for-`BoatRunDirector.IsRunReady`-then-server-only shape):
   - `RandomStreamName = "EnemyShips"`.
   - Picks a count in the configured range, finds seeded positions in a
     `BoxCollider2D` generation area (open water, away from the player's
     start position), instantiates `EnemyShip.prefab`, calls `Spawn()`.
3. Same seed + same config always reproduces the same encounter, exactly
   like the existing obstacle/loot streams — free replayability for
   testing and for the "re-enter this seed" flow `MainMenuManager` already
   logs.

## New scripts, at a glance

| Script | Folder | Role |
|---|---|---|
| `EnemyShipSpawner` | `Ship/` | Seeded spawn of `EnemyShip` prefabs, copies `BoatObstacleGenerator`'s shape |
| `NetworkShipSinkMeter` | `Ship/` | Second meter alongside `NetworkShipHealth`, direct-hit-weighted |
| `EnemyShipCannon` | `Ship/` | Server-authoritative enemy cannon fire at the player's ship |
| `EnemyShipApproach` | `Ship/` | Idle/Approach/Engaged/Sunk state machine, closes distance while not dealt with |
| `EnemyShipCleanup` | `Ship/` | Listens for sink, despawns crew + cannons + hull with a short delay |
| `BoardingPoint` | `Ship/` | `NetworkInteractable2D` that moves a player from one ship's deck to another |
| `ShipSabotageProp` | `Ship/` | `NetworkInteractable2D` sabotage action while boarded, damages Health/SinkLevel |
| *(networked cannon fire)* | `Ship/` | Resolves the client-authority gap in `ShipCannon`/`Cannonball` — needed for enemy ships to take validated damage |

## Suggested build order

1. Settle the cannon-authority question (blocks everything else being
   trustworthy in multiplayer).
2. `NetworkShipSinkMeter` + wire it alongside the existing
   `NetworkShipHealth` on the player's ship first, so the two-meter UI and
   sink-at-either-threshold logic is provable before there's an enemy to
   shoot at.
3. `EnemyShip` prefab with `NetworkShipHealth` + `NetworkShipSinkMeter` +
   pirate spawn points (reusing `Enemy`/`EnemyAI` untouched) but no
   approach/cannon behavior yet — confirms the prefab and cleanup path
   work.
4. `EnemyShipCannon` + `EnemyShipApproach` — makes it a threat.
5. `BoardingPoint` + `ShipSabotageProp` — makes it a boarding target.
6. `EnemyShipSpawner` wired to a new `"EnemyShips"` seed stream and new
   config fields — makes it appear procedurally instead of by hand-placing
   one in the scene for testing.
