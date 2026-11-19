using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>
    /// Applies all InteractionRoller impulses after the rollers have sampled the physics state.
    /// This prevents the result from depending on which roller's FixedUpdate happened first when
    /// two rollers touch the same game piece. It is created automatically; robots do not need a
    /// component or prefab reference for it.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal sealed class InteractionRollerImpulseResolver : MonoBehaviour
    {
        private struct ImpulseRequest
        {
            public Rigidbody Target;
            public Rigidbody Source;
            public Vector3 Impulse;
            public Vector3 ContactPoint;
            public bool OppositeReaction;
            public string RollerKey;
            public int RollerId;
            public int ContactIndex;
        }

        private struct RequestSortKey
        {
            public string TargetKey;
            public string RollerKey;
            public int TargetId;
            public int RollerId;
            public int ContactIndex;
            public int RequestIndex;
        }

        private struct BodyImpulse
        {
            public Rigidbody Body;
            public Vector3 CenterOfMass;
            public Vector3 Linear;
            public Vector3 Angular;
            public Vector3 FirstContactPoint;
            public int Count;
        }

        private static readonly List<ImpulseRequest> Requests = new(128);
        private static readonly List<RequestSortKey> SortedRequests = new(128);
        private static readonly List<BodyImpulse> Bodies = new(64);
        private static readonly Dictionary<Rigidbody, int> BodyIndices = new(64);
        private static readonly HashSet<Rigidbody> TouchedBodies = new(128);
        private static readonly Comparison<RequestSortKey> RequestComparison = CompareRequests;
        private static int _nextRollerId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            Requests.Clear();
            SortedRequests.Clear();
            Bodies.Clear();
            BodyIndices.Clear();
            TouchedBodies.Clear();
            _nextRollerId = 0;
            InteractionRollerOrderKey.Reset();

            var manager = new GameObject(nameof(InteractionRollerImpulseResolver));
            DontDestroyOnLoad(manager);
            manager.AddComponent<InteractionRollerImpulseResolver>();
        }

        internal static int RegisterRoller() => _nextRollerId++;

        internal static void Enqueue(
            Rigidbody target, Rigidbody source, Vector3 impulse, Vector3 contactPoint,
            bool oppositeReaction, string rollerKey, int rollerId, int contactIndex)
        {
            if (!target || !source || impulse.sqrMagnitude < 1e-12f)
                return;

            Requests.Add(new ImpulseRequest
            {
                Target = target,
                Source = source,
                Impulse = impulse,
                ContactPoint = contactPoint,
                OppositeReaction = oppositeReaction,
                RollerKey = rollerKey,
                RollerId = rollerId,
                ContactIndex = contactIndex,
            });
        }

        private void FixedUpdate()
        {
            if (Requests.Count == 0)
                return;

            if (Requests.Count == 1)
            {
                var request = Requests[0];
                if (request.Target)
                    request.Target.AddForceAtPosition(request.Impulse, request.ContactPoint, ForceMode.Impulse);
                if (request.OppositeReaction && request.Source)
                    request.Source.AddForceAtPosition(-request.Impulse, request.ContactPoint, ForceMode.Impulse);
                Requests.Clear();
                return;
            }

            // Independent bodies cannot affect one another while impulses are
            // queued for this physics step. Only shared bodies need a stable
            // summation order, so skip key lookup and sorting otherwise.
            bool hasSharedBody = false;
            for (int i = 0; i < Requests.Count; i++)
            {
                var request = Requests[i];
                if (request.Target && !TouchedBodies.Add(request.Target))
                    hasSharedBody = true;
                if (request.OppositeReaction && request.Source && !TouchedBodies.Add(request.Source))
                    hasSharedBody = true;
                if (hasSharedBody) break;
            }
            TouchedBodies.Clear();

            if (!hasSharedBody)
            {
                for (int i = 0; i < Requests.Count; i++)
                {
                    var request = Requests[i];
                    if (request.Target)
                        request.Target.AddForceAtPosition(request.Impulse, request.ContactPoint, ForceMode.Impulse);
                    if (request.OppositeReaction && request.Source)
                        request.Source.AddForceAtPosition(-request.Impulse, request.ContactPoint, ForceMode.Impulse);
                }
                Requests.Clear();
                return;
            }

            // Sort by scene and hierarchy keys so summation and application order
            // is repeatable across runs with the same actor insertion order.
            for (int i = 0; i < Requests.Count; i++)
            {
                var request = Requests[i];
                SortedRequests.Add(new RequestSortKey
                {
                    TargetKey = request.Target ? InteractionRollerOrderKey.ForComponent(request.Target) : null,
                    TargetId = request.Target ? request.Target.GetInstanceID() : 0,
                    RollerKey = request.RollerKey,
                    RollerId = request.RollerId,
                    ContactIndex = request.ContactIndex,
                    RequestIndex = i,
                });
            }
            SortedRequests.Sort(RequestComparison);

            for (int i = 0; i < SortedRequests.Count; i++)
            {
                var request = Requests[SortedRequests[i].RequestIndex];
                AddImpulse(request.Target, request.Impulse, request.ContactPoint);
                if (request.OppositeReaction)
                    AddImpulse(request.Source, -request.Impulse, request.ContactPoint);
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                var body = Bodies[i];
                if (!body.Body) continue;

                // Force plus torque is equivalent to applying every impulse at its contact point,
                // while reducing a contested body to one deterministic accumulated update per tick.
                if (body.Count == 1)
                {
                    // Preserve the existing fast path for the overwhelmingly common case: one
                    // roller contacting one body. This is the same native call used before.
                    body.Body.AddForceAtPosition(body.Linear, body.FirstContactPoint, ForceMode.Impulse);
                }
                else
                {
                    body.Body.AddForce(body.Linear, ForceMode.Impulse);
                    body.Body.AddTorque(body.Angular, ForceMode.Impulse);
                }
            }

            Requests.Clear();
            SortedRequests.Clear();
            Bodies.Clear();
            BodyIndices.Clear();
        }

        private static int CompareRequests(RequestSortKey a, RequestSortKey b)
        {
            int result = string.CompareOrdinal(a.TargetKey, b.TargetKey);
            if (result != 0) return result;
            result = a.TargetId.CompareTo(b.TargetId);
            if (result != 0) return result;
            result = string.CompareOrdinal(a.RollerKey, b.RollerKey);
            if (result != 0) return result;
            result = a.RollerId.CompareTo(b.RollerId);
            if (result != 0) return result;
            return a.ContactIndex.CompareTo(b.ContactIndex);
        }

        private static void AddImpulse(Rigidbody body, Vector3 impulse, Vector3 contactPoint)
        {
            if (!body) return;

            if (!BodyIndices.TryGetValue(body, out int index))
            {
                index = Bodies.Count;
                BodyIndices.Add(body, index);
                Bodies.Add(new BodyImpulse
                {
                    Body = body,
                    Linear = impulse,
                    FirstContactPoint = contactPoint,
                    Count = 1,
                });
                return;
            }

            var aggregate = Bodies[index];
            if (aggregate.Count == 1)
            {
                // A body with only one request uses AddForceAtPosition; compute
                // torque and fetch its COM only if another request arrives.
                aggregate.CenterOfMass = body.worldCenterOfMass;
                aggregate.Angular = Vector3.Cross(
                    aggregate.FirstContactPoint - aggregate.CenterOfMass, aggregate.Linear);
            }
            aggregate.Linear += impulse;
            aggregate.Angular += Vector3.Cross(contactPoint - aggregate.CenterOfMass, impulse);
            aggregate.Count++;
            Bodies[index] = aggregate;
        }
    }

    /// <summary>
    /// Provides an ordering key derived from a component's scene and hierarchy
    /// position. The first key assigned to a component stays fixed if it moves.
    /// Runtime spawns must occur in the same hierarchy order for repeatable keys.
    /// </summary>
    internal static class InteractionRollerOrderKey
    {
        private sealed class KeyHolder
        {
            public string Value;
        }

        private static ConditionalWeakTable<Component, KeyHolder> _keys = new();

        internal static void Reset() => _keys = new ConditionalWeakTable<Component, KeyHolder>();

        internal static string ForComponent(Component component)
            => _keys.GetValue(component, BuildKey).Value;

        private static KeyHolder BuildKey(Component component)
        {
            var builder = new StringBuilder(96);
            var scene = component.gameObject.scene;
            builder.Append(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
            AppendHierarchy(builder, component.transform);
            builder.Append(':').Append(component.GetType().FullName).Append(':');

            var peers = component.GetComponents(component.GetType());
            builder.Append(Array.IndexOf(peers, component));
            return new KeyHolder { Value = builder.ToString() };
        }

        private static void AppendHierarchy(StringBuilder builder, Transform transform)
        {
            if (transform.parent != null)
                AppendHierarchy(builder, transform.parent);
            builder.Append('/').Append(transform.GetSiblingIndex());
        }
    }
}
