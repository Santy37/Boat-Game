using DeadmansTales.Networking;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared Inspector drawing for the boat spawners. Draws every field normally
/// but replaces the "Chosen Spawn Point" int with a dropdown of the assigned
/// spawn points' names, so you pick a point by name instead of guessing an
/// index. The dropdown only shows when "Random Spawn Point" is off.
/// </summary>
internal static class SpawnPointInspectorGUI
{
    public static void Draw(SerializedObject serializedObject)
    {
        serializedObject.Update();

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            // Swap the raw int for a name dropdown, in its normal spot.
            if (iterator.name == "chosenSpawnPoint")
            {
                DrawChosenSpawnPoint(serializedObject);
                continue;
            }

            using (new EditorGUI.DisabledScope(iterator.name == "m_Script"))
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawChosenSpawnPoint(SerializedObject serializedObject)
    {
        SerializedProperty random =
            serializedObject.FindProperty("randomSpawnPoint");

        // Irrelevant while points are chosen at random, so hide it.
        if (random != null && random.boolValue)
        {
            return;
        }

        SerializedProperty chosen =
            serializedObject.FindProperty("chosenSpawnPoint");
        SerializedProperty points =
            serializedObject.FindProperty("spawnPoints");

        if (points == null || points.arraySize == 0)
        {
            EditorGUILayout.HelpBox(
                "Assign Spawn Points to choose one by name.",
                MessageType.Info
            );
            return;
        }

        string[] names = new string[points.arraySize];
        for (int i = 0; i < points.arraySize; i++)
        {
            Object point = points.GetArrayElementAtIndex(i).objectReferenceValue;
            names[i] = point != null
                ? $"{i}: {point.name}"
                : $"{i}: (empty)";
        }

        int index = Mathf.Clamp(chosen.intValue, 0, names.Length - 1);
        index = EditorGUILayout.Popup("Chosen Spawn Point", index, names);
        chosen.intValue = index;
    }
}

[CustomEditor(typeof(BoatObstacleGenerator))]
internal sealed class BoatObstacleGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SpawnPointInspectorGUI.Draw(serializedObject);
    }
}

[CustomEditor(typeof(NetworkEnemyShipSpawner2D))]
internal sealed class NetworkEnemyShipSpawner2DEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SpawnPointInspectorGUI.Draw(serializedObject);
    }
}
