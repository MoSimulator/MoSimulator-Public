using RobotFramework.Components;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Editor
{
    /// <summary>Enforces roller scale while authoring, without running roller physics in edit mode.</summary>
    [InitializeOnLoad]
    public static class InteractionRollerScaleLock
    {
        static InteractionRollerScaleLock() => EditorApplication.update += Enforce;

        static void Enforce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating) return;

            foreach (InteractionRoller roller in ColliderSelectMode.Rollers())
            {
                if (roller == null || !roller.EnforceUnitWorldScale()) continue;
                EditorUtility.SetDirty(roller.transform);
                if (PrefabUtility.IsPartOfPrefabInstance(roller.transform))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(roller.transform);
                EditorSceneManager.MarkSceneDirty(roller.gameObject.scene);
            }
        }
    }
}
