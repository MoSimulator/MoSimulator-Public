using UnityEditor.ShortcutManagement;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Fits a primitive collider to each selected mesh and files it under the model's "collider" object.
    /// The renderers are left alone.
    /// </summary>
    public abstract class GuessGenericColliderBase : TargetTool<MeshFilter>
    {
        /// <summary>Whether a mesh that reads as anything but a box is left without a collider.</summary>
        protected abstract bool BoxOnly { get; }

        protected override bool Filter(MeshFilter target) => target.sharedMesh != null;

        protected override bool Apply(MeshFilter target, ToolRun run)
        {
            GameObject source = target.gameObject;
            if (!ShapeFit.TryFit(target.sharedMesh, out ShapeFit.Fit fit)) return false;
            if (BoxOnly && fit.Primitive != PrimitiveType.Cube) return false;

            // Only once the mesh is known to be getting a collider: unpacking is destructive to the
            // prefab connection and irrelevant to a part this pass is going to skip.
            HierarchyOps.Unpack(source);

            // Alongside the mesh object, under the model's "collider" object. Existing entries are
            // left as they are; a fresh guess is appended under a name that does not clash with them.
            Transform parent = ModelLayout.ColliderParent(source.transform.parent, run.Name);

            var holder = new GameObject(
                HierarchyOps.UniqueChildName(parent, ModelLayout.ShapeName(fit.Primitive)));
            Undo.RegisterCreatedObjectUndo(holder, run.Name);
            Undo.SetTransformParent(holder.transform, parent, run.Name);
            holder.layer = source.layer;
            holder.isStatic = source.isStatic;

            Transform placed = holder.transform;
            placed.position = source.transform.TransformPoint(fit.Rotation * fit.Center);
            placed.rotation = source.transform.rotation * fit.Rotation;

            // The holder is left at world scale 1 and the collider sized in world units. Inheriting the
            // model's scale instead would shear any collider whose fitted angle differs from the model's.
            HierarchyOps.SetWorldScale(placed, Vector3.one);
            AddCollider(holder, ShapeFit.WorldSize(fit, source.transform.lossyScale) * 0.5f, fit.Axis, fit.Primitive);
            return true;
        }

        protected override void Finish(IReadOnlyList<MeshFilter> targets, ToolRun run)
        {
            string skipped = BoxOnly
                ? $", {targets.Count - run.Applied} left without one (not box-shaped)"
                : string.Empty;

            run.Report($"created {run.Applied} collider(s) for {targets.Count} mesh(es){skipped}.");
        }

        static void AddCollider(GameObject holder, Vector3 extents, int axis, PrimitiveType primitive)
        {
            switch (primitive)
            {
                case PrimitiveType.Sphere:
                {
                    SphereCollider sphere = holder.AddComponent<SphereCollider>();
                    sphere.radius = (extents.x + extents.y + extents.z) / 3f;
                    break;
                }

                case PrimitiveType.Cylinder:
                case PrimitiveType.Capsule:
                {
                    CapsuleCollider capsule = holder.AddComponent<CapsuleCollider>();
                    capsule.direction = axis;
                    capsule.radius = (extents[(axis + 1) % 3] + extents[(axis + 2) % 3]) * 0.5f;
                    capsule.height = extents[axis] * 2f;
                    break;
                }

                default:
                {
                    BoxCollider box = holder.AddComponent<BoxCollider>();
                    box.size = extents * 2f;
                    break;
                }
            }
        }
    }

    /// <summary>Fits whatever shape each mesh reads as.</summary>
    public sealed class GuessGenericCollider : GuessGenericColliderBase
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Guess Generic Collider", 20,
            "Select the object(s) holding the meshes to fit.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Guess Generic Collider", KeyCode.B, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Guess Generic Collider", KeyCode.B, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<GuessGenericCollider>()) Menu(); }

        [MenuItem("GameObject/Guess Generic Collider", false, 33)]
        static void Menu() => Run<GuessGenericCollider>();

        [MenuItem("GameObject/Guess Generic Collider", true)]
        static bool Validate() => CanRun<GuessGenericCollider>();

        protected override bool BoxOnly => false;
    }

    /// <summary>
    /// The same fit, but a mesh whose best match is a sphere, cylinder or capsule is left with NO
    /// collider rather than being handed the box it also fitted.
    /// </summary>
    /// <remarks>
    /// The classifier always names a winner, so on a round part the ordinary command still emits
    /// something — a box around a wheel, say, which collides across every corner the wheel does not
    /// have. That is worse than no collider on a part meant to be shaped by hand or by
    /// PerTriangleMeshCollider afterwards. This sweeps a selection for the parts that genuinely are
    /// boxes and leaves the rest untouched, including their prefab connection.
    /// </remarks>
    public sealed class GuessGenericColliderBoxOnly : GuessGenericColliderBase
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Guess Generic Collider (Box Only)", 21,
            "Select the object(s) holding the meshes to fit.");

        [MenuItem("GameObject/Guess Generic Collider (Box Only)", false, 34)]
        static void Menu() => Run<GuessGenericColliderBoxOnly>();

        [MenuItem("GameObject/Guess Generic Collider (Box Only)", true)]
        static bool Validate() => CanRun<GuessGenericColliderBoxOnly>();

        protected override bool BoxOnly => true;
    }
}
