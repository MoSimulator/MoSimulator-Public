using UnityEditor.ShortcutManagement;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Replaces a mesh object with the built-in primitive that best matches its silhouette, sized to an
    /// oriented box fitted to the mesh. Intended for background detail whose exact geometry does not matter.
    /// </summary>
    public sealed class GuessGeneric : TargetTool<MeshFilter>
    {
        const string GenericSuffix = "_Generic";

        public override ToolInfo Info => new ToolInfo("Mesh", "Guess Generic Shape", 12,
            "Select the object(s) holding the mesh renderer to replace.");

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Guess Generic Shape", KeyCode.G, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Guess Generic Shape", KeyCode.G, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { if (CanRun<GuessGeneric>()) Menu(); }

        [MenuItem("GameObject/Guess Generic Shape", false, 31)]
        static void Menu() => Run<GuessGeneric>();

        [MenuItem("GameObject/Guess Generic Shape", true)]
        static bool Validate() => CanRun<GuessGeneric>();

        /// <summary>The mesh being replaced has to be the one that was clicked, not one below it.</summary>
        protected override TargetScope Scope => TargetScope.Self;

        protected override bool Filter(MeshFilter target)
        {
            return target.sharedMesh != null && target.GetComponent<MeshRenderer>() != null;
        }

        protected override bool UnpackBeforeApply => true;

        protected override bool Apply(MeshFilter target, ToolRun run)
        {
            GameObject source = target.gameObject;
            if (!ShapeFit.TryFit(target.sharedMesh, out ShapeFit.Fit fit)) return false;

            GameObject replacement = GameObject.CreatePrimitive(fit.Primitive);
            Undo.RegisterCreatedObjectUndo(replacement, run.Name);
            replacement.name = source.name + GenericSuffix;
            replacement.isStatic = source.isStatic;
            replacement.layer = source.layer;

            Transform placed = replacement.transform;
            Undo.SetTransformParent(placed, source.transform.parent, run.Name);
            placed.position = source.transform.TransformPoint(fit.Rotation * fit.Center);
            placed.rotation = source.transform.rotation * fit.Rotation * AxisRotation(fit);

            // The primitive meshes are 1 unit wide; the cylinder and capsule are 2 units tall.
            Vector3 alongAxes = AxisOrder(fit, ShapeFit.WorldSize(fit, source.transform.lossyScale));
            if (ShapeFit.IsAxial(fit.Primitive)) alongAxes.y *= 0.5f;
            HierarchyOps.SetWorldScale(placed, alongAxes);

            replacement.GetComponent<MeshRenderer>().sharedMaterial = source.GetComponent<MeshRenderer>().sharedMaterial;

            // CreatePrimitive always attaches a collider; keep it only if the source was collidable.
            if (source.GetComponent<Collider>() == null)
                Object.DestroyImmediate(replacement.GetComponent<Collider>());

            Debug.Log($"{run.Name}: '{source.name}' → {fit.Primitive}. Scores: {fit.Report}", replacement);

            Undo.DestroyObjectImmediate(source);
            return true;
        }

        /// <summary>Rotation that points the primitive's own Y axis down the chosen fitted axis.</summary>
        static Quaternion AxisRotation(ShapeFit.Fit fit)
        {
            if (!ShapeFit.IsAxial(fit.Primitive)) return Quaternion.identity;

            if (fit.Axis == 0) return Quaternion.Euler(0f, 0f, -90f);
            if (fit.Axis == 2) return Quaternion.Euler(90f, 0f, 0f);
            return Quaternion.identity;
        }

        /// <summary>Fitted size reordered so y holds the extent along the chosen axis.</summary>
        static Vector3 AxisOrder(ShapeFit.Fit fit, Vector3 size)
        {
            if (!ShapeFit.IsAxial(fit.Primitive)) return size;

            if (fit.Axis == 0) return new Vector3(size.y, size.x, size.z);
            if (fit.Axis == 2) return new Vector3(size.x, size.z, size.y);
            return size;
        }
    }
}
