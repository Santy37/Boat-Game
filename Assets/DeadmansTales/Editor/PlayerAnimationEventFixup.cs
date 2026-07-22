using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Puts a receiver for the swing clips' animation events onto the player rig.
///
/// The clips fire EnableHitbox/DisableHitbox, and Unity delivers animation
/// events only to components on the Animator's own GameObject. On this prefab
/// that object ("Visual") carried no components, so every attack logged an
/// error — and with Error Pause on, halted play.
///
/// Done as a builder rather than by hand because the player prefab is
/// deliberately kept close to the version the team reverted to: this adds one
/// component and nothing else, and re-running it is a no-op.
/// </summary>
public static class PlayerAnimationEventFixup
{
    private const string MenuPath = "Deadman's Tales/Fix Player Animation Events";

    private const string PlayerPrefabPath =
        "Assets/DeadmansTales/Prefabs/Player_2D_Network.prefab";

    [MenuItem(MenuPath)]
    public static void BuildAll()
    {
        GameObject prefab =
            PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            Animator animator =
                prefab.GetComponentInChildren<Animator>(true);

            if (animator == null)
            {
                Debug.LogWarning(
                    "[Animation Events] The player rig has no Animator."
                );
                return;
            }

            if (animator.GetComponent<PlayerAttackAnimationEvents>() != null)
            {
                Debug.Log(
                    "[Animation Events] Receiver already present on " +
                    $"'{animator.name}'; nothing to do."
                );
                return;
            }

            animator.gameObject.AddComponent<PlayerAttackAnimationEvents>();

            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefabPath);

            Debug.Log(
                "[Animation Events] Added the swing-event receiver to " +
                $"'{animator.name}'. EnableHitbox/DisableHitbox now land."
            );
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void BuildAllFromCommandLine()
    {
        BuildAll();
    }
}
