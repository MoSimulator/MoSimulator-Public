using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// The hierarchy edits the authoring tools share: severing prefab links, naming siblings, placing
    /// holder objects and putting a transform at a wanted world scale.
    /// </summary>
    public static class HierarchyOps
    {
        /// <summary>
        /// A prefab instance cannot be restructured in place, so the link is severed outright. The tools
        /// that call this are one-way operations on the scene objects; nothing is written back to an asset.
        /// </summary>
        public static void Unpack(GameObject root)
        {
            if (root == null) return;
            if (!PrefabUtility.IsPartOfPrefabInstance(root)) return;

            GameObject outermost = PrefabUtility.GetOutermostPrefabInstanceRoot(root);
            PrefabUtility.UnpackPrefabInstance(outermost, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        /// <summary>
        /// The wanted name, suffixed if a sibling already uses it, so repeated runs append to a model's
        /// contents instead of stacking objects that look like replacements of each other.
        /// </summary>
        public static string UniqueChildName(Transform parent, string wanted)
        {
            if (parent == null) return wanted;

            var taken = new HashSet<string>();
            foreach (Transform child in parent) taken.Add(child.name);

            if (!taken.Contains(wanted)) return wanted;

            for (int i = 2; ; i++)
            {
                string candidate = $"{wanted} ({i})";
                if (!taken.Contains(candidate)) return candidate;
            }
        }

        /// <summary>
        /// The named direct child of the container, created at its origin if it is not there yet. An
        /// existing one is reused untouched: nothing under it is renamed, reparented or destroyed.
        /// Matching is by exact name on a direct child, so it cannot adopt something deeper in the model.
        /// </summary>
        public static Transform FindOrCreateChild(Transform container, string name, string undoName)
        {
            Transform existing = container != null
                ? container.Find(name)
                : GameObject.Find("/" + name)?.transform;

            if (existing != null) return existing;

            var holder = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(holder, undoName);
            Undo.SetTransformParent(holder.transform, container, undoName);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            holder.transform.localScale = Vector3.one;
            return holder.transform;
        }

        /// <summary>
        /// Gives the transform the wanted world scale whatever the hierarchy above it is scaled by, so a
        /// size measured in world units survives being parented under a scaled model.
        /// </summary>
        public static void SetWorldScale(Transform target, Vector3 worldScale)
        {
            target.localScale = Vector3.one;
            Vector3 current = target.lossyScale;
            target.localScale = new Vector3(
                Safe(worldScale.x / current.x),
                Safe(worldScale.y / current.y),
                Safe(worldScale.z / current.z));
        }

        /// <summary>How many parents sit above the transform.</summary>
        public static int Depth(Transform transform)
        {
            int depth = 0;
            for (Transform t = transform; t.parent != null; t = t.parent) depth++;
            return depth;
        }

        /// <summary>
        /// True when the object holds nothing but its Transform, so all it contributes is the grouping
        /// it provides. A model's collider holder is exempt: it is empty by design when a model has no
        /// authored colliders yet, and the tools look it up by name.
        /// </summary>
        public static bool IsEmptyNode(Transform transform)
        {
            if (transform == null) return false;
            if (ModelLayout.IsColliderHolder(transform)) return false;

            Component[] components = transform.GetComponents<Component>();
            return components.Length == 1 && components[0] is Transform;
        }

        /// <summary>
        /// A node with a scale of its own cannot be dissolved without changing what its children look
        /// like, since the scale it contributes would be lost or, combined with rotation, sheared. One
        /// with no children has nothing to lose either way.
        /// </summary>
        public static bool CanDissolve(Transform transform)
        {
            if (transform.childCount == 0) return true;

            Vector3 scale = transform.localScale;
            return IsOne(scale.x) && IsOne(scale.y) && IsOne(scale.z);
        }

        /// <summary>
        /// Puts a grouping object back on a scale of one, pushing the scale it was carrying down into
        /// its children so nothing moves or changes size. A scale belongs on the thing being drawn, not
        /// on the object filing it, so a node with a scale of its own hands it to whatever it holds.
        /// </summary>
        /// <remarks>
        /// A joint anchor is a point in the object's own space, so the scale it was read through goes
        /// with it: the anchors on this object, and the ones other joints place on it, are re-expressed
        /// against the new scale so the connection stays where it was. Non-uniform scale only survives
        /// the move when the children are unrotated and no joint is involved: combined with a rotation
        /// it is a shear, which neither a child's own scale nor a joint axis can express, so that node
        /// is left as it is rather than silently deformed. A childless node has nowhere to put its
        /// scale and is left alone, since its own scale is the only thing giving what it carries a size.
        /// </remarks>
        public static bool NormalizeScale(Transform node, string undoName)
        {
            if (node == null || node.childCount == 0) return false;

            Vector3 scale = node.localScale;
            if (IsOne(scale.x) && IsOne(scale.y) && IsOne(scale.z)) return false;

            Transform[] children = node.Cast<Transform>().ToArray();

            bool uniform = Mathf.Approximately(scale.x, scale.y) && Mathf.Approximately(scale.y, scale.z);
            if (!uniform && children.Any(child => child.localRotation != Quaternion.identity)) return false;

            Joint[] anchored = node.GetComponents<Joint>();
            Joint[] connected = ConnectedTo(node);
            if (!uniform && (anchored.Length > 0 || connected.Length > 0)) return false;

            Vector3[] positions = children.Select(child => child.position).ToArray();
            Quaternion[] rotations = children.Select(child => child.rotation).ToArray();
            Vector3[] scales = children.Select(child => child.lossyScale).ToArray();
            Vector3[] anchors = anchored.Select(joint => node.TransformPoint(joint.anchor)).ToArray();
            Vector3[] points = connected.Select(joint => node.TransformPoint(joint.connectedAnchor)).ToArray();

            Undo.RecordObject(node, undoName);
            node.localScale = Vector3.one;

            for (int i = 0; i < children.Length; i++)
            {
                Undo.RecordObject(children[i], undoName);
                children[i].position = positions[i];
                children[i].rotation = rotations[i];
                SetWorldScale(children[i], scales[i]);
            }

            for (int i = 0; i < anchored.Length; i++)
            {
                Undo.RecordObject(anchored[i], undoName);
                anchored[i].anchor = node.InverseTransformPoint(anchors[i]);
            }

            for (int i = 0; i < connected.Length; i++)
            {
                Undo.RecordObject(connected[i], undoName);
                connected[i].connectedAnchor = node.InverseTransformPoint(points[i]);
            }

            return true;
        }

        /// <summary>
        /// The joints anchoring themselves to this object's body from elsewhere in the same hierarchy.
        /// One configuring its connected anchor itself needs no help: Unity recomputes it from the
        /// bodies' current transforms, which is what a rescale leaves it wanting anyway.
        /// </summary>
        static Joint[] ConnectedTo(Transform node)
        {
            var body = node.GetComponent<Rigidbody>();
            if (body == null) return new Joint[0];

            return node.root.GetComponentsInChildren<Joint>(true)
                .Where(joint => joint.connectedBody == body && !joint.autoConfigureConnectedAnchor)
                .ToArray();
        }

        /// <summary>
        /// Promotes each empty candidate's children in its place and destroys it, deepest first so a
        /// chain of empty nodes collapses in one pass. Candidates that carry a component, or whose
        /// children would move, are left alone and counted in <paramref name="kept"/>.
        /// </summary>
        public static int Dissolve(IEnumerable<Transform> candidates, string undoName, out int kept)
        {
            int removed = 0;
            kept = 0;

            Transform[] ordered = candidates
                .Where(t => t != null)
                .OrderByDescending(Depth)
                .ToArray();

            foreach (Transform candidate in ordered)
            {
                if (candidate == null) continue;
                if (!IsEmptyNode(candidate)) continue;

                if (!CanDissolve(candidate))
                {
                    kept++;
                    continue;
                }

                foreach (Transform child in candidate.Cast<Transform>().ToArray())
                    Undo.SetTransformParent(child, candidate.parent, undoName);

                Undo.DestroyObjectImmediate(candidate.gameObject);
                removed++;
            }

            return removed;
        }

        static bool IsOne(float value)
        {
            return Mathf.Abs(value - 1f) < 1e-4f;
        }

        /// <summary>A scale factor that cannot be trusted collapses to 1 rather than to nothing.</summary>
        static float Safe(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || Mathf.Approximately(value, 0f) ? 1f : value;
        }
    }
}
