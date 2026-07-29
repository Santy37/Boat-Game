using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Drop-in "coins randomly around the map" tool. Place an empty
    /// GameObject anywhere in a scene, add this component, and it scatters
    /// a random number of loose coins across a rectangular patch of ground
    /// centred on its own position once the server starts.
    ///
    /// It is the same NetworkCoinPickup an enemy drops on death
    /// (<see cref="CoinDropUtility"/>), just placed by area instead of by
    /// kill, so a level can be seeded with ambient coins on top of whatever
    /// enemies happen to give up. Server-only, like every other spawn path
    /// in this project — clients never decide what appears.
    /// </summary>
    public sealed class NetworkCoinScatterSpawner : MonoBehaviour
    {
        [Header("How Many")]
        [SerializeField]
        [Min(0)]
        private int minimumCoins = 3;

        [SerializeField]
        [Min(0)]
        private int maximumCoins = 8;

        [Header("Where")]
        [Tooltip(
            "Width/height, in world units, of the rectangle centred on " +
            "this object that coins may land in."
        )]
        [SerializeField]
        private Vector2 areaSize = new Vector2(10f, 10f);

        [Tooltip(
            "If set, only positions inside this collider are used — drag " +
            "in the island's ground/obstacle collider so coins cannot " +
            "land in the sea or inside a wall. Left empty, any point in " +
            "the rectangle above is used as-is."
        )]
        [SerializeField]
        private Collider2D allowedArea;

        [Tooltip(
            "How many times to re-roll a position that misses the " +
            "allowed area before giving up on that one coin."
        )]
        [SerializeField]
        [Min(1)]
        private int placementAttempts = 8;

        [Tooltip(
            "Safety cap, in seconds, on how long to wait for Netcode's " +
            "own scene-load confirmation before scattering anyway. Only " +
            "matters on a scene that never raises that confirmation for " +
            "some reason -- everything still waits for it first."
        )]
        [SerializeField]
        [Min(0.5f)]
        private float maxSceneLoadWaitSeconds = 5f;

        private bool hasScattered;
        private bool sceneLoadConfirmed;
        private string ownSceneName;

        private void OnEnable()
        {
            ownSceneName = gameObject.scene.name;
            sceneLoadConfirmed = false;
            StartCoroutine(ScatterWhenServerReady());
        }

        private void OnDisable()
        {
            if (
                NetworkManager.Singleton != null &&
                NetworkManager.Singleton.SceneManager != null
            )
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete -=
                    HandleLoadComplete;
            }
        }

        /// <summary>
        /// Fired once per client when Netcode finishes processing a scene
        /// load -- which is after PopulateScenePlacedObjects has already
        /// run for it. That is the exact "safe to spawn now" moment; see
        /// the comment in <see cref="ScatterWhenServerReady"/> for why.
        /// </summary>
        private void HandleLoadComplete(
            ulong clientId,
            string sceneName,
            LoadSceneMode loadSceneMode
        )
        {
            if (
                sceneName == ownSceneName &&
                NetworkManager.Singleton != null &&
                clientId == NetworkManager.Singleton.LocalClientId
            )
            {
                sceneLoadConfirmed = true;
            }
        }

        private IEnumerator ScatterWhenServerReady()
        {
            while (
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening
            )
            {
                yield return null;
            }

            if (!NetworkManager.Singleton.IsServer || hasScattered)
            {
                yield break;
            }

            // Wait for Netcode's own confirmation that THIS scene's load
            // event has fully finished processing before spawning
            // anything. Spawning any earlier races
            // NetworkSceneManager.PopulateScenePlacedObjects: coin clones
            // created before that pass completes all share their prefab's
            // GlobalObjectIdHash, and it throws on the second one it finds
            // mid-pass ("already contains the same GlobalObjectIdHash"),
            // which corrupts registration for whatever else in that scene
            // had not finished yet. That is what broke the exit rowboat on
            // Level_1_Crab_Beach_2D, and later the rock/pirate-attack
            // sequence and the boat itself on Boat_Gameplay_2D, when this
            // spawner fired before that pass had a chance to finish.
            //
            // A prior fix waited on StageSeedProvider.IsReady instead --
            // that only ever delayed the very first scene load by luck.
            // On every later scene transition the run state is already
            // initialized from the previous scene, so that check passed
            // almost instantly and gave no real protection.
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnLoadComplete +=
                    HandleLoadComplete;

                float deadline =
                    Time.realtimeSinceStartup + maxSceneLoadWaitSeconds;

                while (
                    !sceneLoadConfirmed &&
                    Time.realtimeSinceStartup < deadline
                )
                {
                    yield return null;
                }

                NetworkManager.Singleton.SceneManager.OnLoadComplete -=
                    HandleLoadComplete;
            }

            if (hasScattered)
            {
                yield break;
            }

            hasScattered = true;

            int minimum = Mathf.Min(minimumCoins, maximumCoins);
            int maximum = Mathf.Max(minimumCoins, maximumCoins);
            int count = Random.Range(minimum, maximum + 1);
            int placed = 0;

            for (int index = 0; index < count; index++)
            {
                if (TryPickPosition(out Vector3 position))
                {
                    CoinDropUtility.SpawnAt(position);
                    placed++;
                }
            }

            Debug.Log(
                $"[Coins] {name} scattered {placed} of {count} rolled " +
                "coins around the map."
            );
        }

        private bool TryPickPosition(out Vector3 position)
        {
            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                Vector2 offset = new Vector2(
                    Random.Range(-areaSize.x * 0.5f, areaSize.x * 0.5f),
                    Random.Range(-areaSize.y * 0.5f, areaSize.y * 0.5f)
                );

                Vector3 candidate = transform.position + (Vector3)offset;

                if (allowedArea == null || allowedArea.OverlapPoint(candidate))
                {
                    position = candidate;
                    return true;
                }
            }

            position = transform.position;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
            Gizmos.DrawCube(
                transform.position,
                new Vector3(areaSize.x, areaSize.y, 0.01f)
            );
        }
    }
}
