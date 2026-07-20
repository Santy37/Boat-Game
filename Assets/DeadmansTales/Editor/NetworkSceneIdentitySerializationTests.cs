using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

internal sealed class NetworkSceneIdentitySerializationTests
{
    private static readonly Regex ZeroGlobalObjectIdHash = new Regex(
        @"(?m)^[ \t]*GlobalObjectIdHash:[ \t]*0[ \t]*\r?$",
        RegexOptions.CultureInvariant
    );

    [Test]
    public void EnabledBuildScenesPersistNonzeroNetworkObjectIdentities()
    {
        List<string> failures = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .Where(path => path.EndsWith(".unity"))
            .Select(FindSerializedIdentityFailure)
            .Where(failure => failure != null)
            .ToList();

        Assert.That(
            failures,
            Is.Empty,
            "Enabled build scenes must persist stable NGO scene identities " +
            "on disk. In-memory OnValidate repair is not sufficient:\n" +
            string.Join("\n", failures)
        );
    }

    private static string FindSerializedIdentityFailure(string scenePath)
    {
        string absolutePath = Path.GetFullPath(scenePath);
        if (!File.Exists(absolutePath))
        {
            return $"Missing enabled scene: {scenePath}";
        }

        string serializedScene = File.ReadAllText(absolutePath);
        return ZeroGlobalObjectIdHash.IsMatch(serializedScene)
            ? $"Zero GlobalObjectIdHash: {scenePath}"
            : null;
    }
}
