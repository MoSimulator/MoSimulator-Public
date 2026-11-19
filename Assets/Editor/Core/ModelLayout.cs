using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Where the tools file what they generate inside a model. These names are a convention several
    /// tools read as well as write, so they live in one place rather than being spelled out per tool.
    /// </summary>
    public static class ModelLayout
    {
        /// <summary>The single object holding a subsystem's geometry: what is drawn and nothing else.</summary>
        public const string ModelParentName = "Model";

        /// <summary>The single object holding every authored collider belonging to a model.</summary>
        public const string ColliderParentName = "collider";

        /// <summary>Prefix put on a renderer object that has been demoted to a collider carrier.</summary>
        public const string ColliderPrefix = "collider-";

        /// <summary>The single object holding every roller belonging to a model.</summary>
        public const string DynamicPhysicsName = "DynamicPhysics";

        /// <summary>The holder for transforms used as authored animation and game-piece state targets.</summary>
        public const string AnimationParentName = "Animation";

        /// <summary>The node a run's rollers are parented to, inside <see cref="DynamicPhysicsName"/>.</summary>
        public const string RollerGroupName = "RollerGroup";

        /// <summary>True when the transform is a model's collider holder, so its contents are already filed.</summary>
        public static bool IsColliderHolder(Transform transform)
        {
            return transform != null && transform.name == ColliderParentName;
        }

        /// <summary>The subsystem's geometry holder, created if it is not there yet.</summary>
        public static Transform ModelParent(Transform container, string undoName)
        {
            return HierarchyOps.FindOrCreateChild(container, ModelParentName, undoName);
        }

        /// <summary>The model's collider holder, created alongside its geometry if it is not there yet.</summary>
        public static Transform ColliderParent(Transform container, string undoName)
        {
            return HierarchyOps.FindOrCreateChild(container, ColliderParentName, undoName);
        }

        /// <summary>The model's roller holder, created alongside its geometry if it is not there yet.</summary>
        public static Transform DynamicPhysicsParent(Transform container, string undoName)
        {
            return HierarchyOps.FindOrCreateChild(container, DynamicPhysicsName, undoName);
        }

        /// <summary>The subsystem's authored target holder, created if it is not there yet.</summary>
        public static Transform AnimationParent(Transform container, string undoName)
        {
            return HierarchyOps.FindOrCreateChild(container, AnimationParentName, undoName);
        }

        /// <summary>Names a collider object after its shape, so every tool names them the same way.</summary>
        public static string ShapeName(Collider collider)
        {
            switch (collider)
            {
                case BoxCollider _: return "Box";
                case SphereCollider _: return "Sphere";
                case CapsuleCollider _: return "Capsule";
                case MeshCollider _: return "Mesh";
                default: return collider.GetType().Name.Replace("Collider", string.Empty);
            }
        }

        /// <summary>
        /// Names a collider object after the primitive it was fitted to. Unity has no cylinder collider,
        /// so a fitted cylinder is carried by a capsule and named as one.
        /// </summary>
        public static string ShapeName(PrimitiveType primitive)
        {
            switch (primitive)
            {
                case PrimitiveType.Sphere: return "Sphere";
                case PrimitiveType.Cylinder:
                case PrimitiveType.Capsule: return "Capsule";
                default: return "Box";
            }
        }
    }
}
