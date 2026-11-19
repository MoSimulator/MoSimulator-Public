using System;
using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using RobotFramework.Interfaces;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Editor
{
    /// <summary>
    /// Reduces a hierarchy to what draws it. Everything an object carries is removed — physics, rollers,
    /// simulation behaviours — and only the rendering components, and optionally the colliders, are
    /// left. Kept as a whitelist rather than a list of things to remove, so a component type nobody here
    /// has heard of is stripped rather than quietly surviving.
    /// </summary>
    public static class ComponentStrip
    {
        /// <summary>What a run removed.</summary>
        public struct Result
        {
            /// <summary>Components destroyed.</summary>
            public int Removed;

            /// <summary>Objects at least one component was taken off.</summary>
            public int Objects;
        }

        /// <summary>The component types a stripped object keeps, whatever the settings.</summary>
        static readonly Type[] Rendering =
        {
            typeof(Transform),
            typeof(MeshFilter),
            typeof(Renderer),
            typeof(LODGroup),
            // Text generates its geometry through components that do not inherit Renderer.
            typeof(TextMesh),
            typeof(TMP_Text),
            typeof(TMP_SubMesh),
            typeof(TMP_SubMeshUI),
            // UI text also needs its canvas, graphics, opacity and clipping components.
            typeof(Canvas),
            typeof(CanvasRenderer),
            typeof(CanvasGroup),
            typeof(Graphic),
            typeof(Mask),
            typeof(RectMask2D)
        };

        /// <summary>
        /// Strips the root and everything under it, inactive objects included. No object is destroyed and
        /// nothing is reparented: an emptied object still sits where the model expects it, and Remove
        /// Empty Parents is the tool for clearing what that leaves behind.
        /// </summary>
        public static Result Strip(GameObject root, bool keepColliders, string undoName)
        {
            var result = new Result();
            if (root == null) return result;

            // Counted before anything goes, so an object emptied by the passes below is counted once
            // whichever pass happened to take the last component off it.
            result.Objects = root.GetComponentsInChildren<Transform>(true)
                .Count(t => t.GetComponents<Component>().Any(c => c != null && !IsKept(c, keepColliders)));

            // Hierarchy-wide first, because these reference components on other objects: a joint left for
            // even an instant without the body it connects is a warning Unity logs on the spot, and a
            // RollerGroup is only safe to drop once the rollers it drives are gone.
            result.Removed += Destroy(Find<Joint>(root).Cast<Component>()
                .Concat(Find<Joint2D>(root))
                .Concat(Find<ConstantForce>(root))
                .Concat(Find<ConstantForce2D>(root)), undoName);

            result.Removed += Destroy(Rollers(root).Where(r => !(r is RollerGroup)), undoName);
            result.Removed += Destroy(Rollers(root).OfType<RollerGroup>(), undoName);

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                result.Removed += StripObject(transform.gameObject, keepColliders, undoName);

            return result;
        }

        /// <summary>Whether the hierarchy holds anything a run with these settings would remove.</summary>
        public static bool HasAny(GameObject root, bool keepColliders)
        {
            if (root == null) return false;

            return root.GetComponentsInChildren<Transform>(true)
                .SelectMany(t => t.GetComponents<Component>())
                .Any(c => c != null && !IsKept(c, keepColliders));
        }

        /// <summary>
        /// Empties one object. A component another one still on the object declares it requires cannot be
        /// removed while that other one is there, so the removable set is peeled from the outside in,
        /// taking whatever nothing left depends on until none of it is left.
        /// </summary>
        static int StripObject(GameObject target, bool keepColliders, string undoName)
        {
            List<Component> removable = target.GetComponents<Component>()
                .Where(c => c != null && !IsKept(c, keepColliders))
                .ToList();

            int removed = 0;

            while (removable.Count > 0)
            {
                Component[] free = removable.Where(c => !IsRequiredBy(c, target)).ToArray();

                // A cycle in the RequireComponent graph, which Unity itself cannot resolve either. The
                // object is left as it is rather than half stripped, and the run says nothing about it.
                if (free.Length == 0) break;

                foreach (Component component in free)
                {
                    Undo.DestroyObjectImmediate(component);
                    removed++;
                }

                removable.RemoveAll(c => c == null);
            }

            return removed;
        }

        /// <summary>Whether some other component still on the object declares that it requires this one.</summary>
        static bool IsRequiredBy(Component component, GameObject owner)
        {
            Type type = component.GetType();

            foreach (Component other in owner.GetComponents<Component>())
            {
                if (other == null || other == component) continue;

                foreach (RequireComponent requirement in other.GetType()
                             .GetCustomAttributes(typeof(RequireComponent), true)
                             .Cast<RequireComponent>())
                {
                    if (Requires(requirement, type)) return true;
                }
            }

            return false;
        }

        static bool Requires(RequireComponent requirement, Type type)
        {
            return IsMatch(requirement.m_Type0, type)
                || IsMatch(requirement.m_Type1, type)
                || IsMatch(requirement.m_Type2, type);
        }

        static bool IsMatch(Type required, Type type)
        {
            return required != null && required.IsAssignableFrom(type);
        }

        /// <summary>Whether the component is one the strip leaves in place.</summary>
        /// <remarks>
        /// A WheelCollider is not a collision shape kept for what it is, the way the others are: it is a
        /// suspension and a drive model that needs a Rigidbody to work against, so it goes with the rest
        /// of the simulation even when the colliders stay.
        /// </remarks>
        static bool IsKept(Component component, bool keepColliders)
        {
            if (Rendering.Any(kept => kept.IsInstanceOfType(component))) return true;
            if (!keepColliders || component is WheelCollider) return false;

            return component is Collider || component is Collider2D;
        }

        /// <summary>
        /// Every roller in the hierarchy, whatever kind: interaction rollers, generic rollers, flywheels,
        /// PVFF controllers and the groups over them all implement <see cref="IRoller"/>, so the interface
        /// is what is asked for rather than a list of types that would go stale as kinds are added.
        /// </summary>
        static IEnumerable<MonoBehaviour> Rollers(GameObject root)
        {
            return root.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c is IRoller);
        }

        static IEnumerable<T> Find<T>(GameObject root) where T : Component
        {
            return root.GetComponentsInChildren<T>(true);
        }

        static int Destroy<T>(IEnumerable<T> components, string undoName) where T : Component
        {
            int destroyed = 0;

            // Materialised first: the sequences are read off the live hierarchy, and destroying as it is
            // walked is what makes a pass quietly skip components.
            foreach (T component in components.ToArray())
            {
                if (component == null) continue;
                Undo.DestroyObjectImmediate(component);
                destroyed++;
            }

            return destroyed;
        }
    }
}
