using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Ship
{
    /// <summary>
    /// Server-side pacing for the boat survival loop. Opens dormant
    /// <see cref="NetworkShipLeak"/>s on an escalating timer so the crew has
    /// to keep splitting attention between steering, fighting, and repairs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ShipLeakDirector : NetworkBehaviour
    {
        [SerializeField]
        [Min(1f)]
        private float firstLeakDelay = 18f;

        [SerializeField]
        [Min(2f)]
        private float startingInterval = 24f;

        [SerializeField]
        [Min(2f)]
        private float minimumInterval = 9f;

        [SerializeField]
        [Min(0f)]
        private float intervalDecayPerLeak = 2f;

        [SerializeField]
        [Tooltip("Leave empty to find every leak in the scene on spawn.")]
        private NetworkShipLeak[] leaks;

        [SerializeField]
        private NetworkShipHealth shipHealth;

        private Coroutine directorRoutine;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsServer)
            {
                return;
            }

            if (leaks == null || leaks.Length == 0)
            {
                leaks = FindObjectsByType<NetworkShipLeak>(
                    FindObjectsSortMode.InstanceID
                );
            }

            if (shipHealth == null)
            {
                shipHealth = FindFirstObjectByType<NetworkShipHealth>();
            }

            directorRoutine = StartCoroutine(RunDirector());
        }

        public override void OnNetworkDespawn()
        {
            if (directorRoutine != null)
            {
                StopCoroutine(directorRoutine);
                directorRoutine = null;
            }

            base.OnNetworkDespawn();
        }

        private IEnumerator RunDirector()
        {
            float interval = Mathf.Max(2f, startingInterval);

            yield return new WaitForSeconds(Mathf.Max(1f, firstLeakDelay));

            while (true)
            {
                if (shipHealth != null && shipHealth.IsSunk)
                {
                    yield break;
                }

                TryOpenRandomLeak();

                yield return new WaitForSeconds(interval);

                interval = Mathf.Max(
                    Mathf.Max(2f, minimumInterval),
                    interval - Mathf.Max(0f, intervalDecayPerLeak)
                );
            }
        }

        private void TryOpenRandomLeak()
        {
            if (leaks == null || leaks.Length == 0)
            {
                return;
            }

            List<NetworkShipLeak> dormant = new List<NetworkShipLeak>();

            foreach (NetworkShipLeak leak in leaks)
            {
                if (
                    leak != null &&
                    leak.IsSpawned &&
                    !leak.LeakActive.Value
                )
                {
                    dormant.Add(leak);
                }
            }

            if (dormant.Count == 0)
            {
                return;
            }

            int selected = Random.Range(0, dormant.Count);
            dormant[selected].ActivateServer();
        }
    }
}
