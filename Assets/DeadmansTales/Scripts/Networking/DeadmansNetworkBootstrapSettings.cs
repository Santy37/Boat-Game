using System;
using UnityEngine;

namespace DeadmansTales.Networking
{
    /// <summary>
    /// Resources-loaded configuration for the project-owned NetworkManager.
    /// This replaces the deleted 3D template NetworkManager prefab and its
    /// DefaultNetworkPrefabs asset.
    /// </summary>
    [CreateAssetMenu(
        fileName = "DeadmansNetworkBootstrapSettings",
        menuName = "Deadman's Tales/Networking/Bootstrap Settings"
    )]
    public sealed class DeadmansNetworkBootstrapSettings : ScriptableObject
    {
        [SerializeField]
        private GameObject playerPrefab;

        [SerializeField]
        private GameObject[] additionalNetworkPrefabs =
            Array.Empty<GameObject>();

        public GameObject PlayerPrefab => playerPrefab;

        public GameObject[] AdditionalNetworkPrefabs =>
            additionalNetworkPrefabs ?? Array.Empty<GameObject>();
    }
}
