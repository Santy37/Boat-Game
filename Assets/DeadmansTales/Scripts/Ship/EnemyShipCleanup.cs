using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Watches this enemy ship's health and crew, and despawns the whole
    /// ship -- hull, cannons, and any remaining crew objects under it --
    /// once it's actually been dealt with: either its Health has hit 0, or
    /// every pirate aboard it is dead. Lives on the ship's root, alongside
    /// NetworkShipHealth and EnemyShipApproach.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EnemyShipCleanup : NetworkBehaviour
    {
        [SerializeField]
        [Min(0f)]
        [Tooltip(
            "Delay before the ship actually despawns, so a sink " +
            "animation/effect has time to play."
        )]
        private float despawnDelay = 1.5f;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own EnemyShipApproach.")]
        private EnemyShipApproach shipApproach;

        [SerializeField]
        [Tooltip("Leave empty to find this ship's own NetworkShipHealth.")]
        private NetworkShipHealth shipHealth;

        private const float CrewCheckInterval = 0.5f;

        private Coroutine despawnRoutine;
        private float nextCrewCheckTime;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            Debug.Log(
                $"[Enemy Ship Cleanup] {name} OnNetworkSpawn -- " +
                $"IsServer={IsServer}, IsSpawned={IsSpawned}",
                this
            );

            if (!IsServer)
            {
                return;
            }

            if (shipApproach == null)
            {
                shipApproach = GetComponent<EnemyShipApproach>();
            }

            if (shipHealth == null)
            {
                shipHealth = GetComponent<NetworkShipHealth>();
            }

            if (shipHealth != null)
            {
                shipHealth.ShipSunk += HandleShipDefeated;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (shipHealth != null)
            {
                shipHealth.ShipSunk -= HandleShipDefeated;
            }

            // A scene-placed ship (dragged into the scene for testing,
            // rather than spawned by a future seeded spawner) survives
            // Despawn(false) as an inert GameObject -- hide it so "sinking"
            // still reads as gone, matching Enemy.cs's HideDeathPresentation.
            if (NetworkObject != null && NetworkObject.IsSceneObject == true)
            {
                HideShip();
            }

            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (
                !IsServer ||
                !IsSpawned ||
                despawnRoutine != null ||
                Time.time < nextCrewCheckTime
            )
            {
                return;
            }

            nextCrewCheckTime = Time.time + CrewCheckInterval;

            if (shipApproach != null && !shipApproach.HasLivingCrew)
            {
                HandleShipDefeated();
            }
        }

        private void HandleShipDefeated()
        {
            if (despawnRoutine != null)
            {
                return;
            }

            despawnRoutine = StartCoroutine(DespawnAfterDelay());
        }

        private IEnumerator DespawnAfterDelay()
        {
            Debug.Log(
                $"[Enemy Ship] {name} has been dealt with. Sinking.",
                this
            );

            yield return new WaitForSeconds(Mathf.Max(0f, despawnDelay));

            // Crew are sibling scene objects with their own NetworkObject,
            // not real Transform children of this ship (NGO doesn't support
            // nesting spawned NetworkObjects), so despawning the ship below
            // will not take any surviving pirates with it -- clean them up
            // explicitly first.
            if (shipApproach != null)
            {
                shipApproach.DespawnRemainingCrewServer();
            }

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                // Scene-placed ships must survive as scene objects until the
                // scene unloads; runtime-spawned ones can be destroyed
                // outright. Same rule Enemy.cs follows for individual crew.
                bool destroyRuntimeObject =
                    NetworkObject.IsSceneObject != true;

                NetworkObject.Despawn(destroyRuntimeObject);
            }

            despawnRoutine = null;
        }

        private void HideShip()
        {
            foreach (Renderer shipRenderer in
                GetComponentsInChildren<Renderer>(true))
            {
                shipRenderer.enabled = false;
            }

            foreach (Collider2D shipCollider in
                GetComponentsInChildren<Collider2D>(true))
            {
                shipCollider.enabled = false;
            }
        }
    }
}
