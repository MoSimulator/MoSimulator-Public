using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Lifting colliders off the objects that carry them and filing them under a collider holder,
    /// shared by the tools that do it: one on demand for a chosen parent, one while reorganising a
    /// whole subsystem. Where the holder is, is the caller's decision; what moving means is here.
    /// </summary>
    public static class ColliderSplit
    {
        /// <summary>
        /// Files an object's colliders under the given holder, returning how many moved. An object that
        /// exists only to carry colliders is moved wholesale; anything else keeps its renderer and
        /// scripts where they are and hands copies of its colliders to a new holder child.
        /// </summary>
        public static int Extract(GameObject owner, Transform holder, string undoName)
        {
            return Extract(owner, holder, undoName, null);
        }

        /// <summary>
        /// Files an object's colliders under the given holder and reports each replacement before an
        /// original collider is destroyed. Callers with serialized collider references can use this
        /// to keep those references valid when a collider has to be copied.
        /// </summary>
        public static int Extract(GameObject owner, Transform holder, string undoName,
            Action<Collider, Collider> onReplacement)
        {
            Collider[] colliders = owner.GetComponents<Collider>();
            if (colliders.Length == 0) return 0;

            // Already filed: re-running would otherwise shuffle the holder's own contents around.
            if (owner.transform.parent == holder) return 0;

            if (IsColliderOnly(owner))
            {
                Undo.RecordObject(owner, undoName);
                owner.name = HierarchyOps.UniqueChildName(holder, ModelLayout.ShapeName(colliders[0]));
                Undo.SetTransformParent(owner.transform, holder, undoName);
                foreach (Collider collider in colliders) onReplacement?.Invoke(collider, collider);
                return colliders.Length;
            }

            var carrier = new GameObject(
                HierarchyOps.UniqueChildName(holder, ModelLayout.ShapeName(colliders[0])));
            Undo.RegisterCreatedObjectUndo(carrier, undoName);
            Undo.SetTransformParent(carrier.transform, holder, undoName);
            carrier.layer = owner.layer;
            carrier.isStatic = owner.isStatic;

            // Matching the owner's full world transform reproduces every collider exactly, since their
            // sizes are stored in the owner's own units.
            carrier.transform.position = owner.transform.position;
            carrier.transform.rotation = owner.transform.rotation;
            HierarchyOps.SetWorldScale(carrier.transform, owner.transform.lossyScale);

            foreach (Collider collider in colliders)
            {
                ComponentUtility.CopyComponent(collider);
                ComponentUtility.PasteComponentAsNew(carrier);
                Collider[] copied = carrier.GetComponents<Collider>();
                Collider replacement = copied[copied.Length - 1];
                onReplacement?.Invoke(collider, replacement);
                Undo.DestroyObjectImmediate(collider);
            }

            return colliders.Length;
        }

        /// <summary>True when the object carries nothing but colliders, so it can be moved wholesale.</summary>
        public static bool IsColliderOnly(GameObject go)
        {
            if (go.transform.childCount > 0) return false;

            foreach (Component component in go.GetComponents<Component>())
            {
                if (component is Transform || component is Collider) continue;
                return false;
            }

            return true;
        }
    }
}
