using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class LegacyRenderPipelineValidator
{
    private const string MenuPath =
        "Deadman's Tales/Validate Cleanup Rendering";

    private const string PipelineGuid =
        "4b83569d67af61e458304325a23e5dfd";

    private const string RendererGuid =
        "f288ae1f4751b564a96ac7587541f7a2";

    private const string ExpectedPipelinePath =
        "Assets/DeadmansTales/Settings/Rendering/PC_RPAsset.asset";

    private const string ExpectedRendererPath =
        "Assets/DeadmansTales/Settings/Rendering/PC_Renderer.asset";

    [MenuItem(MenuPath)]
    public static void ValidateRendering()
    {
        string pipelinePath = AssetDatabase.GUIDToAssetPath(PipelineGuid);
        string rendererPath = AssetDatabase.GUIDToAssetPath(RendererGuid);

        ValidateResolvedAsset(
            "URP pipeline asset",
            pipelinePath,
            ExpectedPipelinePath
        );

        ValidateResolvedAsset(
            "URP renderer asset",
            rendererPath,
            ExpectedRendererPath
        );

        string[] dependencies =
            AssetDatabase.GetDependencies(pipelinePath, true);

        if (!dependencies.Contains(rendererPath))
        {
            throw new InvalidOperationException(
                $"The URP pipeline asset does not reference the expected " +
                $"renderer asset: {rendererPath}"
            );
        }

        foreach (string dependency in dependencies)
        {
            if (
                dependency.StartsWith("Assets/Core/", StringComparison.Ordinal) ||
                dependency.StartsWith("Assets/Platformer/", StringComparison.Ordinal) ||
                dependency.StartsWith("Assets/Shooter/", StringComparison.Ordinal) ||
                dependency.StartsWith("Assets/Blocks/", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    $"The restored URP configuration still depends on deleted " +
                    $"sample content: {dependency}"
                );
            }
        }

        Debug.Log(
            "[Cleanup Rendering Validation] PASS\n" +
            $"Pipeline: {pipelinePath}\n" +
            $"Renderer: {rendererPath}\n" +
            "The project-owned URP configuration is present and complete."
        );
    }

    private static void ValidateResolvedAsset(
        string description,
        string actualPath,
        string expectedPath
    )
    {
        if (string.IsNullOrWhiteSpace(actualPath))
        {
            throw new InvalidOperationException(
                $"The {description} GUID does not resolve to an asset."
            );
        }

        if (!actualPath.Equals(expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The {description} resolved to '{actualPath}', expected " +
                $"'{expectedPath}'."
            );
        }

        if (AssetDatabase.LoadMainAssetAtPath(actualPath) == null)
        {
            throw new InvalidOperationException(
                $"The {description} could not be loaded: {actualPath}"
            );
        }
    }
}
