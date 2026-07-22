using Unity.Netcode;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Loose plunder. Unlike the food and chest interactables this is picked
    /// up by walking over it rather than by pressing E — coins are scattered
    /// in handfuls, and making players stop and confirm each one would turn
    /// looting into paperwork.
    ///
    /// Collection is server-only: the server owns player positions through
    /// NetworkTransform, so its trigger callback is the authoritative one,
    /// and the coin despawns for everyone the instant it is taken.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Collider2D))]
    public sealed class NetworkCoinPickup : NetworkBehaviour
    {
        [SerializeField]
        [Min(1)]
        private int coinValue = 5;

        [Tooltip("Gentle bob so loose coins read as pickups, not scenery.")]
        [SerializeField]
        private float bobHeight = 0.08f;

        [SerializeField]
        private float bobSpeed = 2.5f;

        private bool collected;
        private Transform visual;
        private Vector3 visualStartLocalPosition;
        private float bobPhase;

        public int CoinValue => Mathf.Max(1, coinValue);

        private void Awake()
        {
            visual = transform.childCount > 0
                ? transform.GetChild(0)
                : null;

            if (visual != null)
            {
                visualStartLocalPosition = visual.localPosition;
            }

            // Randomised per instance so a scattered pile does not pulse in
            // lockstep. Position-derived rather than Random so every client
            // animates the same coin identically.
            bobPhase = (transform.position.x + transform.position.y) * 1.7f;
        }

        private void Update()
        {
            if (visual == null || bobHeight <= 0f)
            {
                return;
            }

            float offset = Mathf.Sin(
                Time.time * bobSpeed + bobPhase
            ) * bobHeight;

            visual.localPosition = visualStartLocalPosition +
                new Vector3(0f, offset, 0f);
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryCollect(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // A player that spawns already standing on a coin never raises an
            // Enter event, so the Stay pass catches it.
            TryCollect(other);
        }

        private void TryCollect(Collider2D other)
        {
            if (collected || !IsSpawned || !IsServer || other == null)
            {
                return;
            }

            NetworkPlayerLoadout loadout =
                other.GetComponentInParent<NetworkPlayerLoadout>();

            if (loadout == null)
            {
                return;
            }

            PlayerHealth health = loadout.GetComponent<PlayerHealth>();
            if (health != null && !health.IsAlive)
            {
                return;
            }

            if (!loadout.AddCoinsServer(CoinValue))
            {
                return;
            }

            collected = true;

            Debug.Log(
                $"[Coins] Client {loadout.OwnerClientId} picked up " +
                $"{CoinValue} coins (total {loadout.Coins.Value}).",
                this
            );

            NetworkObject.Despawn(true);
        }
    }
}
