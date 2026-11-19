using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoSimLib
{
    /// <summary>
    /// Inspector-configured collision ignores, active while this component is enabled.
    /// Rigidbody entries include their active child colliders, as in Utils.
    /// </summary>
    [AddComponentMenu("MoSim/Physics/Ignore Collisions")]
    public class IgnoreCollisions : MonoBehaviour
    {
        [Serializable]
        public class ColliderRigidbodyPair
        {
            public Collider collider;
            public Rigidbody rigidbody;
        }

        [Serializable]
        public class RigidbodyPair
        {
            public Rigidbody rigidbodyA;
            public Rigidbody rigidbodyB;
        }

        [SerializeField, Tooltip("Ignore this collider against all active child colliders of the rigidbody. Applied on enable.")]
        private ColliderRigidbodyPair[] colliderToRigidbody = Array.Empty<ColliderRigidbodyPair>();

        [SerializeField, Tooltip("Ignore all active child colliders of one rigidbody against those of the other. Applied on enable.")]
        private RigidbodyPair[] rigidbodyToRigidbody = Array.Empty<RigidbodyPair>();

        // Keep the actual collider sets so disabling restores the same pairs even
        // if Inspector references or the rigidbody hierarchies change at runtime.
        private readonly List<(Collider[] first, Collider[] second)> appliedPairs = new();

        private void OnEnable()
        {
            if (colliderToRigidbody != null)
            {
                foreach (var pair in colliderToRigidbody)
                {
                    if (pair == null || pair.collider == null || pair.rigidbody == null)
                        continue;

                    ApplyIgnore(new[] { pair.collider }, pair.rigidbody.GetComponentsInChildren<Collider>());
                }
            }

            if (rigidbodyToRigidbody != null)
            {
                foreach (var pair in rigidbodyToRigidbody)
                {
                    if (pair == null || pair.rigidbodyA == null || pair.rigidbodyB == null ||
                        pair.rigidbodyA == pair.rigidbodyB)
                        continue;

                    ApplyIgnore(pair.rigidbodyA.GetComponentsInChildren<Collider>(),
                        pair.rigidbodyB.GetComponentsInChildren<Collider>());
                }
            }
        }

        private void ApplyIgnore(Collider[] first, Collider[] second)
        {
            Utils.IgnoreCollisionsBetween(first, second);
            appliedPairs.Add((first, second));
        }

        private void OnDisable()
        {
            foreach (var pair in appliedPairs)
                Utils.UnignoreCollisionsBetween(pair.first, pair.second);

            appliedPairs.Clear();
        }
    }
}
