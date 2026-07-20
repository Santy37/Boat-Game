using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Turns designer-authored enemy positions into server-spawned network
    /// prefab instances. Enemy NetworkObjects are never stored in a scene, so
    /// death can destroy them normally and late joiners cannot revive zombies.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSceneEnemySpawner2D : MonoBehaviour
    {
        [SerializeField]
        private GameObject enemyPrefab;

        [SerializeField]
        private Vector2[] spawnPositions = System.Array.Empty<Vector2>();

        private readonly List<NetworkObject> spawnedEnemies =
            new List<NetworkObject>();

        private Coroutine spawnRoutine;

        public int SpawnPositionCount => spawnPositions?.Length ?? 0;

        public int LiveEnemyCount => spawnedEnemies.Count(enemy =>
            enemy != null && enemy.IsSpawned);

        private void OnEnable()
        {
            spawnRoutine = StartCoroutine(SpawnWhenServerIsReady());
        }

        private void OnDisable()
        {
            if (spawnRoutine != null)
            {
                StopCoroutine(spawnRoutine);
                spawnRoutine = null;
            }
        }

        private IEnumerator SpawnWhenServerIsReady()
        {
            // Scene activation can precede the first listening frame during a
            // synchronized load. Waiting here also makes direct-scene play safe.
            while (
                NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsListening
            )
            {
                yield return null;
            }

            if (!NetworkManager.Singleton.IsServer)
            {
                spawnRoutine = null;
                yield break;
            }

            yield return null;

            if (
                enemyPrefab == null ||
                enemyPrefab.GetComponent<NetworkObject>() == null
            )
            {
                Debug.LogError(
                    "[Scene Enemy Spawner] A registered NetworkObject enemy " +
                    "prefab is required.",
                    this
                );
                spawnRoutine = null;
                yield break;
            }

            Scene targetScene = gameObject.scene;

            foreach (Vector2 spawnPosition in spawnPositions)
            {
                GameObject enemyObject = Instantiate(
                    enemyPrefab,
                    spawnPosition,
                    Quaternion.identity
                );

                SceneManager.MoveGameObjectToScene(enemyObject, targetScene);

                NetworkObject networkObject =
                    enemyObject.GetComponent<NetworkObject>();
                networkObject.Spawn(true);
                spawnedEnemies.Add(networkObject);
            }

            Debug.Log(
                $"[Scene Enemy Spawner] Spawned {LiveEnemyCount} runtime " +
                $"enemies in {targetScene.name}.",
                this
            );

            spawnRoutine = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (spawnPositions == null)
            {
                return;
            }

            Gizmos.color = new Color(0.9f, 0.22f, 0.16f, 0.9f);
            foreach (Vector2 position in spawnPositions)
            {
                Gizmos.DrawWireSphere(position, 0.55f);
            }
        }
#endif
    }
}
