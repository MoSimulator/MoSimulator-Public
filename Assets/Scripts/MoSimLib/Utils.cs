using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MoSimLib
{
    public static class Utils
    {
        /// <summary>
        /// Finds a child with a given name by only searching the children instead of everything.
        /// breadth only search!!
        /// </summary>
        /// <param name="childName"></param>
        /// <param name="parent"></param>
        /// <returns></returns>
        public static GameObject FindChild(string childName, GameObject parent)
        {
            if (parent == null) return null;
            
            for (int i = 0; i < parent.transform.childCount; i++)
            {
                if (parent.transform.GetChild(i).name == childName)
                {
                    return parent.transform.GetChild(i).gameObject;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Checks for and adds if missing components and scripts
        /// </summary>
        /// <param name="parent">The parent game object</param>
        /// <param name="componentType">The component type to add</param>
        /// <returns></returns>
        public static T TryGetAddComponent<T>(GameObject parent) where T: Component
        {
            // 1. Try to get the component.
            if (parent.TryGetComponent(typeof(T), out var existingComponent))
            {
                // Found it, return the existing component.
                return (T)existingComponent;
            }

            // 2. Not found, add the component.
            Component newComponent = parent.AddComponent(typeof(T));

            // 3. Return the newly created component.
            return (T)newComponent;
        }

        public static bool InRange(float input, float target, float range)
        {
            return input > target - range && input < target + range;
        }
        
        public static T TryGetAddComponent<T>(GameObject parent, out bool spawned) where T: Component
        {
            // 1. Try to get the component.
            if (parent.TryGetComponent(typeof(T), out var existingComponent))
            {
                // Found it, return the existing component.
                spawned = false;
                return (T)existingComponent;
            }

            // 2. Not found, add the component.
            Component newComponent = parent.AddComponent(typeof(T));
            spawned = true;

            // 3. Return the newly created component.
            return (T)newComponent;
        }
        
        /// <summary>
        /// Flips the angle 180
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float FlipAngle(float angle)
        {
            angle = -angle;
            angle = Mathf.Repeat(angle, 360); 
            if (angle < 0)
            {
                angle += 360;
            }
            return angle;
        }
        
        /// <summary>
        /// Wraps the angle into -180 180 from 360
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float WrapAngle180(float angle)
        {
            angle = Mathf.Repeat(angle, 360);
            if (angle > 180)
            {
                angle -= 360; // Convert to -180 to 180 range
            }
            return angle;
        }
        
        /// <summary>
        /// Determines if a value (A) is within a specified unit distance (C) of a center value (B).
        /// </summary>
        /// <param name="a">The value to check.</param>
        /// <param name="b">The center value.</param>
        /// <param name="c">The maximum allowed difference (tolerance).</param>
        /// <returns>True if A is in the range [B - C, B + C], False otherwise.</returns>
        public static bool InRange(double a, double b, double c)
        {
            return Math.Abs(a - b) <= c;
        }

        /// <summary>
        /// wraps angle to 0 to 360
        /// </summary>
        /// <param name="angle"></param>
        /// <returns></returns>
        public static float WrapAngle360(float angle)
        {
            angle = Mathf.Repeat(angle, 360);
            return angle;
        }
        
        public static Vector3 MultiplyVectors(Vector3 vector1, Vector3 vector2)
        {
            return new Vector3(vector1.x * vector2.x, vector1.y * vector2.y, vector1.z * vector2.z);
        }
        
        /// <summary>
        /// returns the difference between two angles
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static float AngleDifference(float a, float b) {
            return (a - b + 540) % 360 - 180;
        }
        
        /// <summary>
        /// Determines if angle A is within the angular range defined by the shortest arc between B and C.
        /// </summary>
        /// <param name="a">The angle to check.</param>
        /// <param name="b">The first boundary angle.</param>
        /// <param name="c">The second boundary angle.</param>
        /// <returns>True if A is within the shortest arc segment [B, C], False otherwise.</returns>
        public static bool WithinAngularRange(float a, float b, float c)
        {
            float diffBC = AngleDifference(c, b);

            float diffBa = AngleDifference(a, b);
            
            bool sameDirection = (diffBa * diffBC >= 0);

            bool smallerMagnitude = (Math.Abs(diffBa) <= Math.Abs(diffBC));

            return sameDirection && smallerMagnitude;
        }
        
        /// <summary>
        /// returns if A is within C degrees of B
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="c"></param>
        /// <returns></returns>
        public static bool InAngularRange(float a, float b, float c)
        {
            return Mathf.Abs(AngleDifference(a, b)) <= c;
        }
        
        /// <summary>
        /// Determines if a value (A) lies within the inclusive range defined by B and C.
        /// The order of B and C does not matter.
        /// </summary>
        /// <param name="a">The value to check.</param>
        /// <param name="b">One boundary of the range.</param>
        /// <param name="c">The other boundary of the range.</param>
        /// <returns>True if A is within the closed interval [min(B, C), max(B, C)], False otherwise.</returns>
        public static bool WithinRange(double a, double b, double c)
        {
            double minBound = Math.Min(b, c);
            double maxBound = Math.Max(b, c);
        
            // A is within the range if it is >= the minimum bound AND <= the maximum bound.
            return a >= minBound && a <= maxBound;
        }
        
        private static readonly Dictionary<int, Dictionary<(Type, bool), Component>> Cache = new();

        public static T FindParentObjectComponent<T>(GameObject child, bool includeSelf = false) where T : Component
        {
            if (child == null) return null;

            int childId = child.GetInstanceID();
            var key = (typeof(T), includeSelf);

            // 1. Check Cache
            if (Cache.TryGetValue(childId, out var typeDict) && typeDict.TryGetValue(key, out var cachedComponent))
            {
                return cachedComponent as T;
            }

            // 2. Expensive Traversal
            T foundComponent = FindParentObjectComponentActual<T>(child, includeSelf);

            // 3. Cache the result
            if (foundComponent != null)
            {
                if (typeDict == null)
                {
                    typeDict = new Dictionary<(Type, bool), Component>();
                    Cache[childId] = typeDict;
                }
                typeDict[key] = foundComponent;
            }

            return foundComponent;
        }

        private static T FindParentObjectComponentActual<T>(GameObject child, bool includeSelf) where T : Component
        {
            Transform currentTransform = includeSelf ? child.transform : child.transform.parent;

            while (currentTransform != null)
            {
                // Non-allocating component check
                if (currentTransform.TryGetComponent<T>(out T component))
                {
                    return component;
                }
                currentTransform = currentTransform.parent;
            }
            return null;
        }

        // Call this when scenes change or objects are destroyed to free RAM
        public static void ClearCache()
        {
            Cache.Clear();
        }
    
        /// <summary>
        /// smooth damp. behaves the same as Vector3.smooth damp.
        /// Full credit to MaxAttack
        /// </summary>
        /// <param name="rot"></param>
        /// <param name="target"></param>
        /// <param name="deriv"></param>
        /// <param name="time"></param>
        /// <returns></returns>
        //https://gist.github.com/maxattack/4c7b4de00f5c1b95a33b
        public static Quaternion SmoothDamp(Quaternion rot, Quaternion target, ref Quaternion deriv, float time) {
            if (Time.fixedTime < Mathf.Epsilon) return rot;
            // account for double-cover
            var dot = Quaternion.Dot(rot, target);
            var multi = dot > 0f ? 1f : -1f;
            target.x *= multi;
            target.y *= multi;
            target.z *= multi;
            target.w *= multi;
            // smooth damp (nlerp approx)
            var result = new Vector4(
                Mathf.SmoothDamp(rot.x, target.x, ref deriv.x, time),
                Mathf.SmoothDamp(rot.y, target.y, ref deriv.y, time),
                Mathf.SmoothDamp(rot.z, target.z, ref deriv.z, time),
                Mathf.SmoothDamp(rot.w, target.w, ref deriv.w, time)
            ).normalized;
		
            // ensure deriv is tangent
            var derivError = Vector4.Project(new Vector4(deriv.x, deriv.y, deriv.z, deriv.w), result);
            deriv.x -= derivError.x;
            deriv.y -= derivError.y;
            deriv.z -= derivError.z;
            deriv.w -= derivError.w;		
		
            return new Quaternion(result.x, result.y, result.z, result.w);
        }
        
        public static List<GameObject> FindGameObjectsOnLayer(string layerName)
        {
            // 1. Get the integer ID for the layer name.
            int layerID = LayerMask.NameToLayer(layerName);

            // Check if the layer name is valid (returns -1 if invalid).
            if (layerID == -1)
            {
                Debug.LogWarning($"Layer '{layerName}' not found in Unity's layer settings.");
                return new List<GameObject>();
            }

            // 2. Find all active GameObjects in the scene.
            // NOTE: This can be a slow operation and shouldn't be called every frame (e.g., in Update).
            GameObject[] allGameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // 3. Filter the array to only include objects on the target layer.
            List<GameObject> objectsOnLayer = allGameObjects
                .Where(go => go.layer == layerID)
                .ToList();

            return objectsOnLayer;
        }
        
        public static T GetStringToEnum<T>(string stringName) where T : struct, Enum
        {
            if (string.IsNullOrEmpty(stringName)) return default;

            var enumNames = Enum.GetNames(typeof(T));

            var match = enumNames.FirstOrDefault(name => 
                stringName.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (match != null && Enum.TryParse<T>(match, true, out var result))
            {
                return result;
            }

            return default;
        }

        #region Collision ignoring

        /// <summary>
        /// Ignores or restores collisions between all colliders on two rigidbodies.
        /// </summary>
        public static void IgnoreCollisionsBetween(Rigidbody rbA, Rigidbody rbB, bool ignore = true)
        {
            if (rbA == null || rbB == null)
            {
                Debug.LogWarning("IgnoreCollisionsBetween: one or both rigidbodies are null.");
                return;
            }

            IgnoreCollisionsBetween(rbA.GetComponentsInChildren<Collider>(), rbB.GetComponentsInChildren<Collider>(), ignore);
        }

        /// <summary>
        /// Ignores or restores collisions between two arbitrary sets of colliders.
        /// </summary>
        public static void IgnoreCollisionsBetween(Collider[] collidersA, Collider[] collidersB, bool ignore = true)
        {
            if (collidersA == null || collidersB == null) return;

            foreach (var colA in collidersA)
            {
                if (colA == null) continue;

                foreach (var colB in collidersB)
                {
                    if (colB != null)
                        Physics.IgnoreCollision(colA, colB, ignore);
                }
            }
        }

        /// <summary>
        /// Ignores or restores collisions between a collider and all colliders on a rigidbody.
        /// </summary>
        public static void IgnoreCollisionsBetween(Collider colA, Rigidbody rbB, bool ignore = true)
        {
            if (colA == null || rbB == null)
            {
                Debug.LogWarning("IgnoreCollisionsBetween: collider or rigidbody is null.");
                return;
            }

            IgnoreCollisionsBetween(new[] { colA }, rbB.GetComponentsInChildren<Collider>(), ignore);
        }

        /// <summary>
        /// Ignores or restores collisions between two colliders.
        /// </summary>
        public static void IgnoreCollisionsBetween(Collider colA, Collider colB, bool ignore = true)
        {
            if (colA == null || colB == null)
            {
                Debug.LogWarning("IgnoreCollisionsBetween: one or both colliders are null.");
                return;
            }

            Physics.IgnoreCollision(colA, colB, ignore);
        }

        /// <summary>
        /// Ignores or restores collisions between one rigidbody and a collection of rigidbodies.
        /// </summary>
        public static void IgnoreCollisionsBetween(Rigidbody rbA, IEnumerable<Rigidbody> others, bool ignore = true)
        {
            if (rbA == null || others == null) return;

            foreach (var rbB in others)
                IgnoreCollisionsBetween(rbA, rbB, ignore);
        }

        /// <summary>
        /// Ignores or restores collisions between colliders and all colliders on a rigidbody.
        /// </summary>
        public static void IgnoreCollisionsBetween(Collider[] collidersA, Rigidbody rbB, bool ignore = true)
        {
            if (collidersA == null || rbB == null)
            {
                Debug.LogWarning("IgnoreCollisionsBetween: colliders array or rigidbody is null.");
                return;
            }

            IgnoreCollisionsBetween(collidersA, rbB.GetComponentsInChildren<Collider>(), ignore);
        }

        /// <summary>
        /// Ignores or restores collisions between a rigidbody and a set of colliders.
        /// </summary>
        public static void IgnoreCollisionsBetween(Rigidbody rbA, Collider[] collidersB, bool ignore = true)
        {
            if (rbA == null || collidersB == null)
            {
                Debug.LogWarning("IgnoreCollisionsBetween: rigidbody or colliders array is null.");
                return;
            }

            IgnoreCollisionsBetween(rbA.GetComponentsInChildren<Collider>(), collidersB, ignore);
        }

        public static void UnignoreCollisionsBetween(Rigidbody rbA, Rigidbody rbB) => IgnoreCollisionsBetween(rbA, rbB, false);
        public static void UnignoreCollisionsBetween(Collider[] collidersA, Collider[] collidersB) => IgnoreCollisionsBetween(collidersA, collidersB, false);
        public static void UnignoreCollisionsBetween(Collider colA, Rigidbody rbB) => IgnoreCollisionsBetween(colA, rbB, false);
        public static void UnignoreCollisionsBetween(Collider colA, Collider colB) => IgnoreCollisionsBetween(colA, colB, false);
        public static void UnignoreCollisionsBetween(Rigidbody rbA, IEnumerable<Rigidbody> others) => IgnoreCollisionsBetween(rbA, others, false);
        public static void UnignoreCollisionsBetween(Collider[] collidersA, Rigidbody rbB) => IgnoreCollisionsBetween(collidersA, rbB, false);
        public static void UnignoreCollisionsBetween(Rigidbody rbA, Collider[] collidersB) => IgnoreCollisionsBetween(rbA, collidersB, false);

        #endregion
        
        /// <summary>
        /// Creates a deterministic GUID based on the provided seed string.
        /// </summary>
        /// <param name="seed">Seed used to create a GUID hash code.</param>
        /// <returns>The GUID hash code from the provided seed.</returns>
        public static Guid CreateDeterministicGuid(string seed)
        {
            seed ??= string.Empty;

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            return new Guid(hash);
        }
    }
}
