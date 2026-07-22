using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads BoatRunConfig JSON files.
///
/// Load priority:
///
/// 1. External override folder:
///    RunConfigs beside the game executable.
///
/// 2. Built-in config:
///    Assets/StreamingAssets/RunConfigs while developing,
///    or the built game's StreamingAssets folder.
///
/// This allows a tester to replace a JSON file without
/// rebuilding the entire game.
/// </summary>
public static class BoatRunConfigLoader
{
    private const string ConfigFolderName =
        "RunConfigs";

    /// <summary>
    /// Loads a config.
    ///
    /// If loading fails, returns a safe fallback configuration
    /// instead of crashing the entire game.
    /// </summary>
    public static BoatRunConfig Load(
        string configId,
        out string loadedFrom
    )
    {
        loadedFrom = "Built-in fallback";

        if (!IsSafeConfigId(configId))
        {
            Debug.LogError(
                $"[Run Config] Invalid config ID: '{configId}'. " +
                "Config IDs may not contain folder paths."
            );

            return BoatRunConfig.CreateFallback(
                "boat_default"
            );
        }

        string fileName =
            configId + ".json";

        string externalPath =
            Path.Combine(
                GetExternalConfigDirectory(),
                fileName
            );

        string streamingAssetsPath =
            Path.Combine(
                Application.streamingAssetsPath,
                ConfigFolderName,
                fileName
            );

        // First priority:
        // external file beside the executable/project.
        if (File.Exists(externalPath))
        {
            BoatRunConfig externalConfig =
                TryReadConfig(
                    externalPath
                );

            if (externalConfig != null)
            {
                loadedFrom = externalPath;

                Debug.Log(
                    $"[Run Config] Loaded external override: " +
                    $"{externalPath}"
                );

                return externalConfig;
            }

            Debug.LogWarning(
                $"[Run Config] External override exists but " +
                $"could not be loaded. Trying built-in config."
            );
        }

        // Second priority:
        // built-in StreamingAssets version.
        if (File.Exists(streamingAssetsPath))
        {
            BoatRunConfig builtInConfig =
                TryReadConfig(
                    streamingAssetsPath
                );

            if (builtInConfig != null)
            {
                loadedFrom =
                    streamingAssetsPath;

                Debug.Log(
                    $"[Run Config] Loaded built-in config: " +
                    $"{streamingAssetsPath}"
                );

                return builtInConfig;
            }
        }

        Debug.LogError(
            $"[Run Config] Could not load config '{configId}'.\n" +
            $"Checked external path:\n{externalPath}\n\n" +
            $"Checked StreamingAssets path:\n{streamingAssetsPath}\n\n" +
            "Using built-in fallback values."
        );

        return BoatRunConfig.CreateFallback(
            configId
        );
    }

    /// <summary>
    /// Returns the folder where testers can place override
    /// configuration files.
    ///
    /// In the Unity Editor:
    ///     ProjectFolder/RunConfigs
    ///
    /// In a Windows build:
    ///     FolderBesideTheExe/RunConfigs
    /// </summary>
    public static string GetExternalConfigDirectory()
    {
        DirectoryInfo dataDirectoryParent =
            Directory.GetParent(
                Application.dataPath
            );

        string rootDirectory;

        if (dataDirectoryParent != null)
        {
            rootDirectory =
                dataDirectoryParent.FullName;
        }
        else
        {
            rootDirectory =
                Application.dataPath;
        }

        return Path.Combine(
            rootDirectory,
            ConfigFolderName
        );
    }

    private static BoatRunConfig TryReadConfig(
        string filePath
    )
    {
        try
        {
            string json =
                File.ReadAllText(
                    filePath
                );

            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError(
                    $"[Run Config] File is empty: {filePath}"
                );

                return null;
            }

            BoatRunConfig config =
                JsonUtility.FromJson<BoatRunConfig>(
                    json
                );

            if (config == null)
            {
                Debug.LogError(
                    $"[Run Config] JsonUtility returned null " +
                    $"for file: {filePath}"
                );

                return null;
            }

            config.Validate();

            return config;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[Run Config] Failed to read:\n" +
                $"{filePath}\n\n" +
                $"{exception}"
            );

            return null;
        }
    }

    private static bool IsSafeConfigId(
        string configId
    )
    {
        if (string.IsNullOrWhiteSpace(configId))
        {
            return false;
        }

        // Prevent config IDs such as:
        // "../SomeOtherFolder/file"
        return
            Path.GetFileName(configId) ==
            configId;
    }
}