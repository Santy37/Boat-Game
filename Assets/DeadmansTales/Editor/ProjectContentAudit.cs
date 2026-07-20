using System.Linq;
using System.Text;
using DeadmansTales.WorldGeneration;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Read-only diagnostic dump of scene hierarchies, prefab visuals, and
/// seeded island markers. Output goes to the editor log for headless review.
/// </summary>
public static class ProjectContentAudit
{
    private static readonly string[] ScenePaths =
    {
        "Assets/DeadmansTales/Scenes/Boat_Gameplay_2D.unity",
        "Assets/DeadmansTales/Scenes/Island_After_Ocean_01_2D.unity",
    };

    private static readonly string[] PrefabPaths =
    {
        "Assets/DeadmansTales/Prefabs/basicenemy.prefab",
        "Assets/DeadmansTales/Prefabs/Gameplay/Enemy_CrabSkitter.prefab",
        "Assets/DeadmansTales/Prefabs/Gameplay/Enemy_BoneBrute.prefab",
        "Assets/DeadmansTales/Prefabs/Gameplay/Enemy_SkeletonWarrior.prefab",
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab",
        "Assets/DeadmansTales/Prefabs/Gameplay/NetworkFoodPickup_Apple.prefab",
    };

    public static void RunFromCommandLine()
    {
        StringBuilder report = new StringBuilder();
        report.AppendLine("==== AUDIT BEGIN ====");

        foreach (string prefabPath in PrefabPaths)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            report.AppendLine($"---- PREFAB {prefabPath}");

            if (prefab == null)
            {
                report.AppendLine("  MISSING");
                continue;
            }

            DumpTransform(prefab.transform, report, 1);
        }

        foreach (string scenePath in ScenePaths)
        {
            Scene scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single
            );

            report.AppendLine($"---- SCENE {scenePath}");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                DumpTransform(root.transform, report, 1);
            }

            foreach (SeededSpawnMarker2D marker in scene
                .GetRootGameObjects()
                .SelectMany(root =>
                    root.GetComponentsInChildren<SeededSpawnMarker2D>(true)))
            {
                SerializedObject serialized = new SerializedObject(marker);
                SerializedProperty prefabs =
                    serialized.FindProperty("networkPrefabs");

                string prefabNames = string.Join(
                    ", ",
                    Enumerable.Range(0, prefabs.arraySize)
                        .Select(index =>
                        {
                            Object value = prefabs
                                .GetArrayElementAtIndex(index)
                                .objectReferenceValue;
                            return value == null ? "<null>" : value.name;
                        })
                );

                report.AppendLine(
                    $"MARKER {marker.name} cat={marker.Category} " +
                    $"pos={(Vector2)marker.SpawnPosition} " +
                    $"always={marker.AlwaysSpawn} " +
                    $"chance={marker.SpawnChance:0.00} " +
                    $"prefabs=[{prefabNames}]"
                );
            }
        }

        report.AppendLine("==== AUDIT END ====");
        Debug.Log(report.ToString());
    }

    private static void DumpTransform(
        Transform current,
        StringBuilder report,
        int depth
    )
    {
        StringBuilder line = new StringBuilder();
        line.Append(new string(' ', depth * 2));
        line.Append(current.name);
        line.Append(current.gameObject.activeSelf ? "" : " [INACTIVE]");
        line.Append($" pos={(Vector2)current.position}");
        line.Append($" scale={(Vector2)current.lossyScale}");

        SpriteRenderer renderer = current.GetComponent<SpriteRenderer>();
        if (renderer != null)
        {
            string spriteName = renderer.sprite == null
                ? "<none>"
                : renderer.sprite.name;
            line.Append(
                $" SR(sprite={spriteName} color={renderer.color} " +
                $"order={renderer.sortingOrder})"
            );
        }

        Canvas canvas = current.GetComponent<Canvas>();
        if (canvas != null)
        {
            line.Append(
                $" CANVAS(mode={canvas.renderMode} sort={canvas.sortingOrder})"
            );
        }

        Graphic graphic = current.GetComponent<Graphic>();
        if (graphic != null)
        {
            string extra = graphic is Text text
                ? $" text='{text.text}'"
                : "";
            line.Append(
                $" UI({graphic.GetType().Name} color={graphic.color}{extra})"
            );
        }

        foreach (Behaviour behaviour in
            current.GetComponents<Behaviour>())
        {
            if (
                behaviour != null &&
                !(behaviour is Transform) &&
                behaviour.GetType().Namespace != null &&
                !behaviour.GetType().Namespace.StartsWith("UnityEngine")
            )
            {
                line.Append($" <{behaviour.GetType().Name}>");
            }
        }

        report.AppendLine(line.ToString());

        foreach (Transform child in current)
        {
            DumpTransform(child, report, depth + 1);
        }
    }
}
