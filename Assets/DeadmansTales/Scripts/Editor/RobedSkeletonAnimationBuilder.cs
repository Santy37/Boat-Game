using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class RobedSkeletonAnimationBuilder
{
    private const int Columns = 12;
    private const int ExpectedSpriteCount = 96;

    // The robed skeleton occupies columns 9, 10, and 11.
    private const int FirstSkeletonColumn = 9;

    private const string RootFolder =
        "Assets/DeadmansTales";

    private const string AnimationFolder =
        RootFolder + "/Animations";

    private const string OutputFolder =
        AnimationFolder + "/RobedSkeleton";

    [MenuItem("Dead Man's Tale/Build Robed Skeleton Animations")]
    public static void BuildAnimations()
    {
        Texture2D selectedTexture =
            Selection.activeObject as Texture2D;

        if (selectedTexture == null)
        {
            EditorUtility.DisplayDialog(
                "No sprite sheet selected",
                "Select the complete character sprite-sheet PNG " +
                "in the Project window, then run this command again.",
                "OK"
            );

            return;
        }

        string texturePath =
            AssetDatabase.GetAssetPath(selectedTexture);

        Sprite[] sprites =
            AssetDatabase
                .LoadAllAssetsAtPath(texturePath)
                .OfType<Sprite>()
                .OrderByDescending(sprite => sprite.rect.y)
                .ThenBy(sprite => sprite.rect.x)
                .ToArray();

        if (sprites.Length != ExpectedSpriteCount)
        {
            EditorUtility.DisplayDialog(
                "Unexpected slice count",
                $"Expected 96 sliced sprites, but found " +
                $"{sprites.Length}.\n\n" +
                "Confirm the sheet is sliced as 12 columns by 8 rows.",
                "OK"
            );

            return;
        }

        EnsureFolder(
            RootFolder,
            "Animations"
        );

        EnsureFolder(
            AnimationFolder,
            "RobedSkeleton"
        );

        string[] directions =
        {
            "Down",
            "Left",
            "Right",
            "Up"
        };

        AnimationClip[] createdClips =
            new AnimationClip[8];

        int createdClipIndex = 0;

        for (int row = 0; row < 4; row++)
        {
            int firstFrameIndex =
                row * Columns + FirstSkeletonColumn;

            Sprite frame0 = sprites[firstFrameIndex];
            Sprite frame1 = sprites[firstFrameIndex + 1];
            Sprite frame2 = sprites[firstFrameIndex + 2];

            string direction = directions[row];

            AnimationClip idleClip =
                CreateOrUpdateClip(
                    $"Idle_{direction}",
                    new[]
                    {
                        frame1,
                        frame1
                    },
                    8f
                );

            AnimationClip walkClip =
                CreateOrUpdateClip(
                    $"Walk_{direction}",
                    new[]
                    {
                        frame0,
                        frame1,
                        frame2,
                        frame1,
                        frame0
                    },
                    8f
                );

            createdClips[createdClipIndex++] = idleClip;
            createdClips[createdClipIndex++] = walkClip;
        }

        string controllerPath =
            OutputFolder + "/RobedSkeleton.controller";

        AnimatorController controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(
                controllerPath
            );

        if (controller == null)
        {
            controller =
                AnimatorController
                    .CreateAnimatorControllerAtPath(
                        controllerPath
                    );
        }

        AnimatorStateMachine stateMachine =
            controller.layers[0].stateMachine;

        ChildAnimatorState[] oldStates =
            stateMachine.states;

        foreach (ChildAnimatorState oldState in oldStates)
        {
            stateMachine.RemoveState(oldState.state);
        }

        AnimatorState idleDownState = null;

        foreach (AnimationClip clip in createdClips)
        {
            AnimatorState state =
                stateMachine.AddState(clip.name);

            state.motion = clip;

            if (clip.name == "Idle_Down")
            {
                idleDownState = state;
            }
        }

        if (idleDownState != null)
        {
            stateMachine.defaultState = idleDownState;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = controller;

        EditorUtility.DisplayDialog(
            "Skeleton animations created",
            "Created eight animation clips and the " +
            "RobedSkeleton Animator Controller.",
            "OK"
        );
    }

    private static AnimationClip CreateOrUpdateClip(
        string clipName,
        Sprite[] frames,
        float frameRate
    )
    {
        string clipPath =
            $"{OutputFolder}/{clipName}.anim";

        AnimationClip clip =
            AssetDatabase.LoadAssetAtPath<AnimationClip>(
                clipPath
            );

        if (clip == null)
        {
            clip = new AnimationClip
            {
                name = clipName
            };

            AssetDatabase.CreateAsset(
                clip,
                clipPath
            );
        }

        clip.frameRate = frameRate;

        EditorCurveBinding spriteBinding =
            new EditorCurveBinding
            {
                path = "",
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite"
            };

        ObjectReferenceKeyframe[] keyframes =
            new ObjectReferenceKeyframe[frames.Length];

        for (int index = 0; index < frames.Length; index++)
        {
            keyframes[index] =
                new ObjectReferenceKeyframe
                {
                    time = index / frameRate,
                    value = frames[index]
                };
        }

        AnimationUtility.SetObjectReferenceCurve(
            clip,
            spriteBinding,
            keyframes
        );

        AnimationClipSettings clipSettings =
            AnimationUtility.GetAnimationClipSettings(
                clip
            );

        clipSettings.loopTime = true;

        AnimationUtility.SetAnimationClipSettings(
            clip,
            clipSettings
        );

        EditorUtility.SetDirty(clip);

        return clip;
    }

    private static void EnsureFolder(
        string parentFolder,
        string childFolder
    )
    {
        string completePath =
            $"{parentFolder}/{childFolder}";

        if (!AssetDatabase.IsValidFolder(completePath))
        {
            AssetDatabase.CreateFolder(
                parentFolder,
                childFolder
            );
        }
    }
}