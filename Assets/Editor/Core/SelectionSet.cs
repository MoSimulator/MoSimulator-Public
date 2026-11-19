using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Editor
{
    /// <summary>How a tool reads its targets out of the current selection.</summary>
    public enum TargetScope
    {
        /// <summary>Only components sitting on the selected objects themselves.</summary>
        Self,

        /// <summary>Components anywhere in or under the selected objects, inactive ones included.</summary>
        Children,

        /// <summary>As <see cref="Children"/>, but starting from the deduplicated selection roots.</summary>
        Roots
    }

    /// <summary>
    /// Reads the current selection the way the authoring tools need it. Every tool that walks the
    /// selection goes through here, so they all agree on what "the selection" means.
    /// </summary>
    public static class SelectionSet
    {
        /// <summary>
        /// The selected objects with any that are already covered by another selected object removed, so
        /// a parent and its child being selected together are treated as one thing.
        /// </summary>
        public static GameObject[] Roots()
        {
            GameObject[] selected = Selection.gameObjects;
            return selected
                .Where(go => !selected.Any(other => other != go && go.transform.IsChildOf(other.transform)))
                .ToArray();
        }

        /// <summary>Deepest transform that contains every root, or null when they sit at the scene root.</summary>
        public static Transform CommonParent(IReadOnlyList<GameObject> roots)
        {
            if (roots == null || roots.Count == 0) return null;

            Transform candidate = roots[0].transform.parent;

            while (candidate != null && !roots.All(r => r.transform.IsChildOf(candidate)))
                candidate = candidate.parent;

            return candidate;
        }

        /// <summary>
        /// The components the selection offers under the given scope. Streamed rather than collected,
        /// so an availability check can stop at the first hit instead of walking the whole hierarchy.
        /// </summary>
        public static IEnumerable<T> Targets<T>(TargetScope scope) where T : Component
        {
            switch (scope)
            {
                case TargetScope.Self:
                    return Selection.gameObjects.SelectMany(go => go.GetComponents<T>());

                case TargetScope.Roots:
                    return Roots().SelectMany(go => go.GetComponentsInChildren<T>(true)).Distinct();

                default:
                    return Selection.gameObjects
                        .SelectMany(go => go.GetComponentsInChildren<T>(true))
                        .Distinct();
            }
        }

        /// <summary>
        /// Everything the Hierarchy window is currently showing: the prefab being edited when a prefab
        /// stage is open, otherwise the roots of every loaded scene.
        /// </summary>
        public static IEnumerable<Transform> HierarchyRoots()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) return new[] { stage.prefabContentsRoot.transform };

            var roots = new List<Transform>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                roots.AddRange(scene.GetRootGameObjects().Select(go => go.transform));
            }

            return roots;
        }
    }
}
