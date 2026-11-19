using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace MoSimLib
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NetSolver : MonoBehaviour
    {
        [System.Serializable]
        public struct NodeAnchor
        {
            public Transform targetTransform;
            public int gridX;
            public int gridY;
            public bool isReleased;
        }

        private struct SphereColliderData
        {
            public float3 Position;
            public float Radius;
            public int InstanceID;
        }

        private struct RigidbodyImpulseData
        {
            public int InstanceID;
            public Vector3 Force;
            public Vector3 Point;
        }

        [Header("Advancded Net Pinning")]
        public List<NodeAnchor> customAnchors = new List<NodeAnchor>();

        [Header("Corners (Simple Net)")]
        public Transform corner00;
        public Transform corner10;
        public Transform corner01;
        public Transform corner11;

        [Header("Net Sizing")]
        public int resolutionX = 8;
        public int resolutionY = 8;
        [Tooltip("Use the anchor-fitted starting shape for stretch, shear, and bend rest lengths. Disable to use a rectangular rest grid sized by Net Width/Height (or the four corner transforms).")]
        public bool useAuthoredRestShape = true;
        [Tooltip("Generated net width in parent-local units. Also sets the unstretched width when Use Authored Rest Shape is disabled. Ignored when all four corner transforms are assigned.")]
        public float netWidth = .6f;
        [Tooltip("Generated net length in parent-local units. Also sets the unstretched length when Use Authored Rest Shape is disabled. Ignored when all four corner transforms are assigned.")]
        public float netHeight = .8f;
        [Range(0.5f, 10f), Tooltip("Scales unstretched lengths without replacing the rest shape. 1 preserves the starting shape; values above 1 add slack, and values below 1 tighten the net.")]
        public float restLengthScale = 1f;

        [Header("SoftBody Parameters")]
        [Range(1, 20)] public int subSteps = 10;
        public float stretchCompliance = 1e-7f;
        public float shearCompliance = 1e-6f;
        public float bendCompliance = 1e-4f;
        [Range(0f, 1f)] public float damping = 0.004f;
        
        [Header("Visual Enhancement")]
        [Range(0f, 1f)] public float visualSmoothingIntensity = 0.5f;

        [Header("Physics")]
        public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
        public float particleMass = 0.01f;

        [Header("Collision")]
        public float collisionRadius = 0.05f;
        public LayerMask collisionLayers = ~0;

        [Tooltip("Run collision every N substeps. Colliders do not move between " +
                 "substeps, so re-testing every one costs 10x for very little " +
                 "accuracy. 1 = every substep (old behaviour), 2 is usually " +
                 "indistinguishable and halves the dominant cost.")]
        [Range(1, 10)] public int collisionSubstepInterval = 2;

        [Header("Anchors")]
        [Range(0f, 1f)] public float anchorBorderFraction = 1f;

        private NativeArray<float3> _pos;
        private NativeArray<float3> _prevPos;
        private NativeArray<float3> _vel;
        private NativeArray<float> _invMass;
        private NativeArray<float3> _restPos;
        private NativeArray<float3> _pinOffsets;
        private NativeArray<float> _lambda;

        private NativeArray<int> _anchorIndices;
        private NativeArray<float3> _anchorPositions;

        // Constraints are a single flat list projected sequentially inside one
        // Burst job. The previous four-way graph colouring did not actually
        // produce disjoint batches - every constraint emitted for a given
        // particle index took its colour from that same index, so a batch could
        // contain five constraints all doing Pos[idx] += ... concurrently. That
        // is a torn read-modify-write on a float3, and it was invisible because
        // [NativeDisableParallelForRestriction] switches off the very check that
        // reports it. Sequential Gauss-Seidel makes the hazard unrepresentable
        // and converges better per iteration than the coloured sweep did.
        private NativeArray<int2> _constraints;
        private NativeArray<float> _restLen;
        private NativeArray<float> _compliance;

        // Structural edges, plus a CSR incidence table mapping each particle to
        // the edges that touch it. Collision is resolved per particle by
        // gathering over its own incident edges, so it never writes to a
        // neighbour's slot.
        private NativeArray<int2> _structuralEdges;
        private NativeArray<int> _edgeStart;
        private NativeArray<int> _edgeIndices;

        private NativeList<SphereColliderData> _activeColliders;
        private NativeQueue<RigidbodyImpulseData> _impulseQueue;
        private readonly Dictionary<int, Rigidbody> _rigidbodyCache = new Dictionary<int, Rigidbody>();

        private Mesh _mesh;
        private Vector3[] _meshVerts;
        private NativeArray<Vector3> _meshVertsNative;
        private readonly Collider[] _colliderOverlapBuffer = new Collider[256];
        private readonly Dictionary<int, Transform> _gizmosPinMap = new Dictionary<int, Transform>();
        private int _particleCount;
        private int _totalConstraints;
        private Rigidbody _selfRb;
        private float3 _lastParentPos;
        private quaternion _lastParentRot;
        private bool _normalsNeedUpdate;

        #region Jobs

        /// <summary>
        /// The entire substep loop in one Burst job.
        ///
        /// This replaces eight <see cref="IJobParallelFor"/>s that were scheduled
        /// once per substep and hand-threaded together through a JobHandle chain
        /// - roughly 110 schedules per FixedUpdate to do a few hundred
        /// microseconds of arithmetic. At 10x10 to 20x20 the scheduler round-trip
        /// dominated the actual work, and every element-level job was small
        /// enough that it ran on a single worker anyway.
        ///
        /// Being a single-threaded <see cref="IJob"/> also removes the entire
        /// class of parallel-write hazard the old code carried: no
        /// [NativeDisableParallelForRestriction], no graph colouring to get
        /// wrong, and results that are bit-for-bit reproducible run to run,
        /// which the racy version was not.
        /// </summary>
        // Default float mode on purpose. FloatMode.Fast would let Burst
        // reassociate and contract these expressions for a further gain, but
        // this change already alters convergence order; stacking a numerics
        // change on top would make any behavioural regression impossible to
        // attribute. Worth trying as a separate step once this is validated.
        [BurstCompile]
        private struct XpbdSolveJob : IJob
        {
            public NativeArray<float3> Pos, PrevPos, Vel;
            [ReadOnly] public NativeArray<float> InvMass;
            [ReadOnly] public NativeArray<float3> RestPos, PinOffsets;
            public float4x4 LocalToWorld;

            [ReadOnly] public NativeArray<int2> Constraints;
            [ReadOnly] public NativeArray<float> RestLen, Compliance;
            public NativeArray<float> Lambda;

            [ReadOnly] public NativeArray<int2> Edges;
            [ReadOnly] public NativeArray<int> EdgeStart, EdgeIndices;
            [ReadOnly] public NativeArray<SphereColliderData> Colliders;
            public NativeQueue<RigidbodyImpulseData> ImpulseQueue;

            [ReadOnly] public NativeArray<int> AnchorIndices;
            [ReadOnly] public NativeArray<float3> AnchorPositions;

            public bool ApplyFrameTransform;
            public float3 TranslationDelta, Pivot;
            public quaternion RotationDelta;

            public int SubSteps, CollisionInterval;
            public float DT, Damping, ParticleRadius;
            public float3 Gravity;
            public float3 C00, C10, C01, C11;

            public void Execute()
            {
                if (ApplyFrameTransform) TransformFrame();

                // Lambda is reset once per frame rather than once per substep,
                // matching the previous behaviour so this change stays a
                // performance and correctness fix rather than a retune.
                for (var i = 0; i < Lambda.Length; i++) Lambda[i] = 0f;

                var dtSq = DT * DT;
                var invDt = 1f / DT;

                for (var s = 0; s < SubSteps; s++)
                {
                    UpdatePinned();
                    ApplyExplicitAnchors();
                    Integrate();

                    if (s % CollisionInterval == 0) SolveCollisions();

                    ProjectConstraints(dtSq);
                    ApplyExplicitAnchors();
                    UpdateVelocities(invDt);
                }
            }

            private void TransformFrame()
            {
                for (var i = 0; i < Pos.Length; i++)
                {
                    if (InvMass[i] == 0f) continue;
                    Pos[i] = math.mul(RotationDelta, Pos[i] - Pivot) + Pivot + TranslationDelta;
                    PrevPos[i] = math.mul(RotationDelta, PrevPos[i] - Pivot) + Pivot + TranslationDelta;
                }
            }

            private void UpdatePinned()
            {
                for (var i = 0; i < Pos.Length; i++)
                {
                    if (InvMass[i] > 0f) continue;

                    var u = RestPos[i].x;
                    var v = RestPos[i].y;
                    var targetPos = math.lerp(math.lerp(C00, C10, u), math.lerp(C01, C11, u), v);

                    targetPos += math.mul(LocalToWorld, new float4(PinOffsets[i], 0f)).xyz;
                    Pos[i] = targetPos;
                    PrevPos[i] = targetPos;
                    Vel[i] = float3.zero;
                }
            }

            private void ApplyExplicitAnchors()
            {
                for (var i = 0; i < AnchorIndices.Length; i++)
                {
                    var targetIdx = AnchorIndices[i];
                    if (targetIdx < 0 || targetIdx >= Pos.Length) continue;

                    var targetPos = AnchorPositions[i];
                    Pos[targetIdx] = targetPos;
                    PrevPos[targetIdx] = targetPos;
                    Vel[targetIdx] = float3.zero;
                }
            }

            private void Integrate()
            {
                for (var i = 0; i < Pos.Length; i++)
                {
                    if (InvMass[i] == 0f) continue;
                    Vel[i] = (Vel[i] + Gravity * DT) * (1f - Damping);
                    PrevPos[i] = Pos[i];
                    Pos[i] += Vel[i] * DT;
                }
            }

            private void ProjectConstraints(float dtSq)
            {
                for (var ci = 0; ci < Constraints.Length; ci++)
                {
                    var pair = Constraints[ci];
                    var a = pair.x;
                    var b = pair.y;

                    var diff = Pos[a] - Pos[b];
                    var len = math.length(diff);
                    if (len < 1e-8f) continue;

                    var wa = InvMass[a];
                    var wb = InvMass[b];
                    var wSum = wa + wb;
                    if (wSum < 1e-8f) continue;

                    var n = diff / len;
                    var c = len - RestLen[ci];
                    var alphaTilde = Compliance[ci] / dtSq;

                    // Lambda is indexed by the constraint's own flat position, so
                    // the old CLambdaIdx indirection array is gone entirely.
                    var dLambda = (-c - alphaTilde * Lambda[ci]) / (wSum + alphaTilde);
                    Lambda[ci] += dLambda;

                    Pos[a] += wa * dLambda * n;
                    Pos[b] -= wb * dLambda * n;
                }
            }

            /// <summary>
            /// Per-particle gather over incident structural edges. The old
            /// version iterated edges and scattered corrections into both
            /// endpoints, which races because adjacent edges share particles.
            /// Here every iteration writes only to slot <c>i</c>.
            /// </summary>
            private void SolveCollisions()
            {
                if (Colliders.Length == 0) return;

                for (var i = 0; i < Pos.Length; i++)
                {
                    var isDynamic = InvMass[i] > 0f;
                    var correction = float3.zero;
                    var vel = Vel[i];
                    var velTouched = false;

                    for (var e = EdgeStart[i]; e < EdgeStart[i + 1]; e++)
                    {
                        var edge = Edges[EdgeIndices[e]];
                        var a = edge.x;
                        var b = edge.y;

                        var pA = Pos[a];
                        var pB = Pos[b];
                        var ab = pB - pA;
                        var abLenSq = math.max(1e-8f, math.dot(ab, ab));

                        for (var c = 0; c < Colliders.Length; c++)
                        {
                            var col = Colliders[c];

                            var t = math.saturate(math.dot(col.Position - pA, ab) / abLenSq);
                            var closestPoint = pA + t * ab;

                            var delta = closestPoint - col.Position;
                            var dist = math.length(delta);
                            var minDist = ParticleRadius + col.Radius;
                            if (dist >= minDist) continue;

                            var normal = dist > 1e-5f ? delta / dist : new float3(0, 1, 0);
                            var penetration = minDist - dist;

                            var wA = InvMass[a] * (1f - t);
                            var wB = InvMass[b] * t;
                            var wSum = wA + wB;
                            if (wSum < 1e-8f) continue;

                            if (isDynamic)
                            {
                                var myWeight = i == a ? wA : wB;
                                correction += normal * penetration * (myWeight / wSum);

                                var vN = math.dot(vel, normal) * normal;
                                vel = (vel - vN) * 0.5f;
                                velTouched = true;
                            }

                            // Each edge is visited from both endpoints, so the
                            // lower-indexed one owns the rigidbody reaction to
                            // keep it from being counted twice.
                            if (col.InstanceID == 0 || i != a) continue;

                            // Scaled by CollisionInterval because this pass now
                            // stands in for that many substeps. Without it,
                            // raising the interval would quietly reduce the
                            // force the net imparts to game pieces, turning a
                            // performance knob into a physics knob.
                            var averageMass = 1f / math.max(1e-4f, (InvMass[a] + InvMass[b]) * 0.5f);
                            var impulseMag = math.min(penetration * averageMass / DT, averageMass * 25f)
                                             * CollisionInterval;
                            ImpulseQueue.Enqueue(new RigidbodyImpulseData
                            {
                                InstanceID = col.InstanceID,
                                Force = -normal * impulseMag,
                                Point = closestPoint
                            });
                        }
                    }

                    if (!isDynamic) continue;
                    Pos[i] += correction;
                    if (velTouched) Vel[i] = vel;
                }
            }

            private void UpdateVelocities(float invDt)
            {
                for (var i = 0; i < Pos.Length; i++)
                {
                    if (InvMass[i] == 0f) continue;
                    Vel[i] = (Pos[i] - PrevPos[i]) * invDt;
                }
            }
        }

        [BurstCompile]
        private struct SmoothCopyToMeshJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Pos;
            public NativeArray<Vector3> Verts;
            public int ResX;
            public int ResY;
            public float Intensity;

            public void Execute(int i)
            {
                // Guard clause against zero or negative dimensions
                if (ResX <= 1 || ResY <= 1)
                {
                    if (i < Pos.Length && i < Verts.Length)
                    {
                        Verts[i] = Pos[i];
                    }
                    return;
                }

                int x = i % ResX;
                int y = i / ResX;

                // Retain absolute anchor positions at raw edges
                if (x == 0 || x == ResX - 1 || y == 0 || y == ResY - 1)
                {
                    Verts[i] = Pos[i];
                    return;
                }

                // Sample immediate 4-connected neighbors
                float3 current = Pos[i];
                float3 left    = Pos[i - 1];
                float3 right   = Pos[i + 1];
                float3 bottom  = Pos[i - ResX];
                float3 top     = Pos[i + ResX];

                // Laplacian neighborhood calculation
                float3 blurred = (left + right + bottom + top) * 0.25f;
            
                // Blend based on intensity slider
                Verts[i] = math.lerp(current, blurred, Intensity);
            }
        }

        #endregion

        #region Unity Lifetime

        // Allocation is paired with OnEnable, not Awake. Re-enabling a
        // GameObject fires OnEnable without re-running Awake, so the previous
        // Awake-allocates / OnDisable-frees pairing left every buffer disposed
        // after the first disable/enable cycle, and FixedUpdate went on writing
        // into freed memory.
        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            AllocateAndBuild();
        }

        private void AllocateAndBuild()
        {
            DisposeNativeBuffers();

            var parentXform = transform.parent != null ? transform.parent : transform;
            _lastParentPos = parentXform.position;
            _lastParentRot = parentXform.rotation;

            BuildNet();
            _selfRb = GetComponentInParent<Rigidbody>();
            _impulseQueue = new NativeQueue<RigidbodyImpulseData>(Allocator.Persistent);
            _activeColliders = new NativeList<SphereColliderData>(Allocator.Persistent);
            _hasPendingMeshCopy = false;
        }

        // Class-level field to track async execution across frames
        private JobHandle _lastJobHandle;
        private bool _hasPendingMeshCopy;

        private void FixedUpdate()
        {
            if (!Application.isPlaying || !_pos.IsCreated) return;

            // Join last frame's solve + mesh copy before touching any buffer.
            _lastJobHandle.Complete();
            ProcessDeferredImpulses();
            PushMeshVertices();

            Transform parentXform = transform.parent ? transform.parent : transform;
            float3 currentParentPos = parentXform.position;
            quaternion currentParentRot = parentXform.rotation;

            GatherColliders();
            SyncAnchorPositions();

            float dt = Time.fixedDeltaTime / subSteps;

            float3 transDelta = currentParentPos - _lastParentPos;
            quaternion rotDelta = math.mul(currentParentRot, math.inverse(_lastParentRot));
            bool frameMoved = math.lengthsq(transDelta) > 1e-8f ||
                              math.distancesq(rotDelta.value, new float4(0f, 0f, 0f, 1f)) > 1e-8f;

            _lastParentPos = currentParentPos;
            _lastParentRot = currentParentRot;

            GetCornerPositions(out var c00, out var c10, out var c01, out var c11);

            // Two schedules per FixedUpdate, down from ~110.
            _lastJobHandle = new XpbdSolveJob
            {
                Pos = _pos, PrevPos = _prevPos, Vel = _vel,
                InvMass = _invMass, RestPos = _restPos,
                PinOffsets = _pinOffsets, LocalToWorld = transform.localToWorldMatrix,

                Constraints = _constraints, RestLen = _restLen,
                Compliance = _compliance, Lambda = _lambda,

                Edges = _structuralEdges, EdgeStart = _edgeStart, EdgeIndices = _edgeIndices,
                Colliders = _activeColliders.AsArray(), ImpulseQueue = _impulseQueue,

                AnchorIndices = _anchorIndices, AnchorPositions = _anchorPositions,

                ApplyFrameTransform = frameMoved,
                TranslationDelta = transDelta, RotationDelta = rotDelta, Pivot = currentParentPos,

                SubSteps = subSteps,
                CollisionInterval = Mathf.Max(1, collisionSubstepInterval),
                DT = dt, Damping = damping, ParticleRadius = collisionRadius,
                Gravity = gravity,
                C00 = c00, C10 = c10, C01 = c01, C11 = c11,
            }.Schedule();

            _lastJobHandle = new SmoothCopyToMeshJob
            {
                Pos = _pos, Verts = _meshVertsNative,
                ResX = resolutionX, ResY = resolutionY, Intensity = visualSmoothingIntensity
            }.Schedule(_particleCount, 64, _lastJobHandle);

            _hasPendingMeshCopy = true;
        }


        private void Update()
        {
            if (Application.isPlaying)
            {
                if (_normalsNeedUpdate)
                {
                    _mesh.RecalculateNormals();
                    _normalsNeedUpdate = false;
                }
            }
            else
            {
                ForceRenderUpdate();
            }
        }

        #endregion

        public Vector3[] GetMeshVertices()
        {
            return _meshVerts;
        }

        public NativeArray<float3> GetRuntimePositions()
        {
            // Callers read this on the main thread, so join the solver first
            // rather than leaving each call site to remember.
            if (Application.isPlaying) _lastJobHandle.Complete();
            return _pos;
        }

        private void ForceRenderUpdate()
        {
            _particleCount = resolutionX * resolutionY;
            _meshVerts = new Vector3[_particleCount];
            BuildMesh();
        }

        void OnDestroy() => DisposeNativeBuffers();
        private void OnDisable() => DisposeNativeBuffers();

        private void DisposeNativeBuffers()
        {
            _lastJobHandle.Complete();
            _lastJobHandle = default;
            _hasPendingMeshCopy = false;

            if (_impulseQueue.IsCreated) _impulseQueue.Dispose();
            if (_activeColliders.IsCreated) _activeColliders.Dispose();
            if (_anchorIndices.IsCreated) _anchorIndices.Dispose();
            if (_anchorPositions.IsCreated) _anchorPositions.Dispose();

            DisposeParticleBuffers();
            DisposeConstraints();
        }

        /// <summary>Frees everything <see cref="BuildNet"/> allocates directly.</summary>
        private void DisposeParticleBuffers()
        {
            if (_pos.IsCreated) _pos.Dispose();
            if (_prevPos.IsCreated) _prevPos.Dispose();
            if (_vel.IsCreated) _vel.Dispose();
            if (_invMass.IsCreated) _invMass.Dispose();
            if (_restPos.IsCreated) _restPos.Dispose();
            if (_pinOffsets.IsCreated) _pinOffsets.Dispose();
            if (_meshVertsNative.IsCreated) _meshVertsNative.Dispose();
        }

        /// <summary>
        /// Frees everything <see cref="BuildConstraints"/> allocates. Called both
        /// from teardown and from the start of a rebuild, so re-running the
        /// build no longer leaks the previous allocation.
        /// </summary>
        private void DisposeConstraints()
        {
            if (_constraints.IsCreated) _constraints.Dispose();
            if (_restLen.IsCreated) _restLen.Dispose();
            if (_compliance.IsCreated) _compliance.Dispose();
            if (_lambda.IsCreated) _lambda.Dispose();
            if (_structuralEdges.IsCreated) _structuralEdges.Dispose();
            if (_edgeStart.IsCreated) _edgeStart.Dispose();
            if (_edgeIndices.IsCreated) _edgeIndices.Dispose();
        }

        public void GetCornerPositions(out float3 c00, out float3 c10, out float3 c01, out float3 c11)
        {
            if (corner00 && corner10 && corner01 && corner11)
            {
                c00 = corner00.position;
                c10 = corner10.position;
                c01 = corner01.position;
                c11 = corner11.position;
            }
            else
            {
                var l2W = transform.parent ? (float4x4)transform.parent.localToWorldMatrix : float4x4.identity;
                c00 = math.mul(l2W, new float4(-netWidth * 0.5f, 0f, -netHeight * 0.5f, 1f)).xyz;
                c10 = math.mul(l2W, new float4(netWidth * 0.5f, 0f, -netHeight * 0.5f, 1f)).xyz;
                c01 = math.mul(l2W, new float4(-netWidth * 0.5f, 0f, netHeight * 0.5f, 1f)).xyz;
                c11 = math.mul(l2W, new float4(netWidth * 0.5f, 0f, netHeight * 0.5f, 1f)).xyz;
            }
        }

        void UpdateAnchorBuffers()
        {
            int validCount = 0;
            for (int i = 0; i < customAnchors.Count; i++)
            {
                if (customAnchors[i].targetTransform && !customAnchors[i].isReleased) validCount++;
            }

            if (_anchorIndices.IsCreated) _anchorIndices.Dispose();
            if (_anchorPositions.IsCreated) _anchorPositions.Dispose();

            _anchorIndices = new NativeArray<int>(validCount, Allocator.Persistent);
            _anchorPositions = new NativeArray<float3>(validCount, Allocator.Persistent);

            var index = 0;
            foreach (var anchor in customAnchors)
            {
                if (!anchor.targetTransform || anchor.isReleased) continue;

                var clampedX = Mathf.Clamp(anchor.gridX, 0, resolutionX - 1);
                var clampedY = Mathf.Clamp(anchor.gridY, 0, resolutionY - 1);
                var targetVertexIdx = clampedY * resolutionX + clampedX;

                _anchorIndices[index] = targetVertexIdx;
                _anchorPositions[index] = anchor.targetTransform.position;
                index++;
            }
        }

        private void SyncAnchorPositions()
        {
            var structureChanged = false;
            var activeCount = 0;

            for (var i = 0; i < customAnchors.Count; i++)
            {
                if (customAnchors[i].targetTransform && !customAnchors[i].isReleased) activeCount++;
            }

            if (!_anchorIndices.IsCreated || activeCount != _anchorIndices.Length)
            {
                structureChanged = true;
            }

            if (structureChanged)
            {
                var defaultInvMass = 1f / Mathf.Max(particleMass, 1e-6f);
                foreach (var anchor in customAnchors)
                {
                    var clampedX = Mathf.Clamp(anchor.gridX, 0, resolutionX - 1);
                    var clampedY = Mathf.Clamp(anchor.gridY, 0, resolutionY - 1);
                    var targetVertexIdx = clampedY * resolutionX + clampedX;

                    if (anchor.isReleased)
                    {
                        var remainsBorder = false;
                        if (anchorBorderFraction > 0f)
                        {
                            if (clampedX == 0 || clampedX == resolutionX - 1 || clampedY == 0 ||
                                clampedY == resolutionY - 1)
                            {
                                float px = (float)clampedX / (resolutionX - 1);
                                float py = (float)clampedY / (resolutionY - 1);
                                if (Mathf.Max(Mathf.Abs(px - 0.5f), Mathf.Abs(py - 0.5f)) * 2f >=
                                    1f - anchorBorderFraction)
                                    remainsBorder = true;
                            }
                        }

                        if (!remainsBorder) _invMass[targetVertexIdx] = defaultInvMass;
                    }
                    else if (anchor.targetTransform)
                    {
                        _invMass[targetVertexIdx] = 0f;
                    }
                }

                UpdateAnchorBuffers();
            }
            else
            {
                var index = 0;
                for (var i = 0; i < customAnchors.Count; i++)
                {
                    var anchor = customAnchors[i];
                    if (!anchor.targetTransform || anchor.isReleased) continue;
                    _anchorPositions[index++] = anchor.targetTransform.position;
                }
            }
        }
        
    [ContextMenu("Generate Edge Anchors")]
    public void GenerateEdgeAnchors()
    {
        if (resolutionX <= 1 || resolutionY <= 1)
        {
            Debug.LogError($"[XpbdNetSolver] Grid resolution is too low (X: {resolutionX}, Y: {resolutionY}). Must be >= 2.");
            return;
        }

        // This reallocates the anchor and constraint buffers, so the solver must
        // not be mid-flight over them.
        if (Application.isPlaying) _lastJobHandle.Complete();

        // 1. First, map out existing anchors to prevent duplicates/overrides
        var existingCoordinates = new HashSet<(int x, int y)>();
        if (customAnchors != null)
        {
            foreach (var anchor in customAnchors)
            {
                if (anchor.targetTransform != null)
                    existingCoordinates.Add((anchor.gridX, anchor.gridY));
            }
        }
        else
        {
            customAnchors = new List<NodeAnchor>();
        }
    
        // 2. Cache a local array of the current live positions BEFORE transforming structural buffers
        int totalNodes = resolutionX * resolutionY;
        Vector3[] capturedPositions = new Vector3[totalNodes];
        bool dataCaptured = false;
    
        if (Application.isPlaying && _pos.IsCreated && _pos.Length == totalNodes)
        {
            for (int i = 0; i < totalNodes; i++)
            {
                float3 p = _pos[i];
                capturedPositions[i] = (!math.any(math.isnan(p)) && !math.any(math.isinf(p))) ? (Vector3)p : Vector3.zero;
            }
            dataCaptured = true;
        }
        else if (_meshVerts != null && _meshVerts.Length == totalNodes)
        {
            for (int i = 0; i < totalNodes; i++)
            {
                Vector3 vLocal = _meshVerts[i];
                capturedPositions[i] = (!float.IsNaN(vLocal.x) && !float.IsInfinity(vLocal.x)) ? transform.TransformPoint(vLocal) : Vector3.zero;
            }
            dataCaptured = true;
        }
    
        // 3. Locate or create a clean hierarchy holder
        GameObject anchorHolder = GameObject.Find("NetEdgeAnchors");
        if (anchorHolder == null)
        {
            anchorHolder = new GameObject("NetEdgeAnchors");
            anchorHolder.transform.SetParent(this.transform, false);
        }
    
        GetCornerPositions(out var c00, out var c10, out var c01, out var c11);
        int generatedCount = 0;
    
        // 4. Generate transforms using the preserved live shape coordinates
        for (int y = 0; y < resolutionY; y++)
        {
            for (int x = 0; x < resolutionX; x++)
            {
                bool isEdge = (x == 0 || x == resolutionX - 1 || y == 0 || y == resolutionY - 1);
                if (!isEdge) continue;
                if (existingCoordinates.Contains((x, y))) continue;
    
                int vertexIndex = y * resolutionX + x;
                Vector3 spawnWorldPos;
    
                // Use captured shape position if valid; otherwise, use mathematical fallback
                if (dataCaptured && capturedPositions[vertexIndex] != Vector3.zero)
                {
                    spawnWorldPos = capturedPositions[vertexIndex];
                }
                else
                {
                    float u = (float)x / (resolutionX - 1);
                    float v = (float)y / (resolutionY - 1);
                    spawnWorldPos = math.lerp(math.lerp(c00, c10, u), math.lerp(c01, c11, u), v);
                }
    
                GameObject go = new GameObject($"EdgeAnchor_{x}_{y}");
                go.transform.position = spawnWorldPos;
                go.transform.SetParent(anchorHolder.transform, true);
    
                customAnchors.Add(new NodeAnchor
                {
                    targetTransform = go.transform,
                    gridX = x,
                    gridY = y,
                    isReleased = false
                });
    
                generatedCount++;
            }
        }
    
        // 5. Synchronize and update the backend simulation structural buffers
        _particleCount = totalNodes;
        UpdateAnchorBuffers();
        BuildConstraints();
        
        Debug.Log($"Successfully generated {generatedCount} edge anchors respecting the active net shape.");
    }

        private void BuildNet()
        {
            // Idempotent: releasing first means a second call replaces the
            // buffers instead of leaking the previous set, which is what the
            // public Rebuild() entry point used to do on every invocation.
            DisposeParticleBuffers();

            resolutionX = Mathf.Max(2, resolutionX);
            resolutionY = Mathf.Max(2, resolutionY);
            _particleCount = resolutionX * resolutionY;
            _meshVerts = new Vector3[_particleCount];
            BuildInitialVertices(_meshVerts);
            _pos = new NativeArray<float3>(_particleCount, Allocator.Persistent);
            _prevPos = new NativeArray<float3>(_particleCount, Allocator.Persistent);
            _vel = new NativeArray<float3>(_particleCount, Allocator.Persistent);
            _invMass = new NativeArray<float>(_particleCount, Allocator.Persistent);
            _restPos = new NativeArray<float3>(_particleCount, Allocator.Persistent);
            _pinOffsets = new NativeArray<float3>(_particleCount, Allocator.Persistent);
            _meshVertsNative = new NativeArray<Vector3>(_particleCount, Allocator.Persistent);

            var invM = 1f / Mathf.Max(particleMass, 1e-6f);
            GetCornerPositions(out var c00, out var c10, out var c01, out var c11);

            for (var y = 0; y < resolutionY; y++)
            for (var x = 0; x < resolutionX; x++)
            {
                var idx = y * resolutionX + x;
                var u = (float)x / (resolutionX - 1);
                var v = (float)y / (resolutionY - 1);

                _restPos[idx] = new float3(u, v, 0f);

                var gridPos = math.lerp(math.lerp(c00, c10, u), math.lerp(c01, c11, u), v);
                float3 worldPos = transform.TransformPoint(_meshVerts[idx]);
                _pinOffsets[idx] = transform.InverseTransformVector(worldPos - gridPos);
                _pos[idx] = worldPos;
                _prevPos[idx] = worldPos;
                _invMass[idx] = invM;
            }

            PinBorderVertices();
            UpdateAnchorBuffers();

            foreach (var targetIdx in _anchorIndices.Where(targetIdx => targetIdx >= 0 && targetIdx < _invMass.Length))
            {
                _invMass[targetIdx] = 0f;
            }

            BuildConstraints();
            BuildMesh();
        }

        private void PinBorderVertices()
        {
            if (anchorBorderFraction <= 0f) return;
            for (int y = 0; y < resolutionY; y++)
            for (int x = 0; x < resolutionX; x++)
            {
                if (x == 0 || x == resolutionX - 1 || y == 0 || y == resolutionY - 1)
                {
                    float px = (float)x / (resolutionX - 1);
                    float py = (float)y / (resolutionY - 1);
                    if (Mathf.Max(Mathf.Abs(px - 0.5f), Mathf.Abs(py - 0.5f)) * 2f >= 1f - anchorBorderFraction)
                        _invMass[y * resolutionX + x] = 0f;
                }
            }
        }

        /// <summary>
        /// Builds the flat constraint list and the per-particle edge incidence
        /// table used by collision.
        ///
        /// Constraints are emitted in row-major generation order rather than
        /// grouped by colour. Since projection is now sequential the grouping
        /// bought nothing, and generation order means consecutive constraints
        /// touch adjacent particles - the difference between streaming through
        /// _pos and striding across it, which matters at 20x20.
        /// </summary>
        void BuildConstraints()
        {
            DisposeConstraints();

            var edgeList = new List<int2>();
            var pairs = new List<int2>();
            var compliance = new List<float>();
            var restLen = new List<float>();

            var restVertices = BuildRestVertices();

            float RestLengthFor(int a, int b)
            {
                return Vector3.Distance(restVertices[a], restVertices[b]);
            }

            void AddConstraint(int a, int b, float comp, bool isStructural)
            {
                var pair = new int2(a, b);
                pairs.Add(pair);
                compliance.Add(comp);
                restLen.Add(RestLengthFor(a, b));
                if (isStructural) edgeList.Add(pair);
            }

            for (int y = 0; y < resolutionY; y++)
            {
                for (int x = 0; x < resolutionX; x++)
                {
                    int idx = y * resolutionX + x;
                    if (x < resolutionX - 1) AddConstraint(idx, idx + 1, stretchCompliance, isStructural: true);
                    if (y < resolutionY - 1)
                        AddConstraint(idx, idx + resolutionX, stretchCompliance, isStructural: true);
                    if (x < resolutionX - 1 && y < resolutionY - 1)
                    {
                        AddConstraint(idx, idx + resolutionX + 1, shearCompliance, isStructural: false);
                        AddConstraint(idx + 1, idx + resolutionX, shearCompliance, isStructural: false);
                    }

                    if (x < resolutionX - 2) AddConstraint(idx, idx + 2, bendCompliance, isStructural: false);
                    if (y < resolutionY - 2)
                        AddConstraint(idx, idx + resolutionX * 2, bendCompliance, isStructural: false);
                }
            }

            _totalConstraints = pairs.Count;

            if (!Application.isPlaying) return;

            _constraints = new NativeArray<int2>(pairs.ToArray(), Allocator.Persistent);
            _compliance = new NativeArray<float>(compliance.ToArray(), Allocator.Persistent);
            _restLen = new NativeArray<float>(restLen.ToArray(), Allocator.Persistent);
            _lambda = new NativeArray<float>(_totalConstraints, Allocator.Persistent);
            _structuralEdges = new NativeArray<int2>(edgeList.ToArray(), Allocator.Persistent);

            BuildEdgeIncidence(edgeList);
        }

        /// <summary>
        /// CSR adjacency: for particle i, the structural edges touching it are
        /// _edgeIndices[_edgeStart[i] .. _edgeStart[i + 1]). This is what lets
        /// collision be solved as a per-particle gather instead of a per-edge
        /// scatter into shared slots.
        /// </summary>
        private void BuildEdgeIncidence(List<int2> edgeList)
        {
            _edgeStart = new NativeArray<int>(_particleCount + 1, Allocator.Persistent);
            _edgeIndices = new NativeArray<int>(edgeList.Count * 2, Allocator.Persistent);

            // Pass 1: degree per particle, written into the slot one past its
            // own so the prefix sum below lands directly on the start offsets.
            foreach (var e in edgeList)
            {
                _edgeStart[e.x + 1]++;
                _edgeStart[e.y + 1]++;
            }

            // Pass 2: exclusive prefix sum.
            for (int i = 1; i <= _particleCount; i++)
                _edgeStart[i] += _edgeStart[i - 1];

            // Pass 3: scatter edge ids, advancing a moving cursor per particle.
            var cursor = new NativeArray<int>(_particleCount, Allocator.Temp);
            for (int i = 0; i < _particleCount; i++) cursor[i] = _edgeStart[i];

            for (int e = 0; e < edgeList.Count; e++)
            {
                var edge = edgeList[e];
                _edgeIndices[cursor[edge.x]++] = e;
                _edgeIndices[cursor[edge.y]++] = e;
            }

            cursor.Dispose();
        }

        private Vector3[] BuildRestVertices()
        {
            // Reconstruct from authoring inputs, never from simulated particles.
            // Rectangular sizing remains available, while authored nets retain
            // their nonuniform edge lengths and diagonal angles by default.
            var vertices = new Vector3[resolutionX * resolutionY];
            BuildInitialVertices(vertices, useAuthoredRestShape);
            float scale = Mathf.Max(0.01f, restLengthScale);
            for (var i = 0; i < vertices.Length; i++)
                vertices[i] = transform.TransformVector(vertices[i]) * scale;
            return vertices;
        }

        // One local-space shape for the editor preview and runtime initial pose.
        private void BuildInitialVertices(Vector3[] vertices, bool fitCustomAnchors = true)
        {
            GetCornerPositions(out var world00, out var world10, out var world01, out var world11);
            float3 c00 = transform.InverseTransformPoint(world00);
            float3 c10 = transform.InverseTransformPoint(world10);
            float3 c01 = transform.InverseTransformPoint(world01);
            float3 c11 = transform.InverseTransformPoint(world11);

            List<Vector2> anchorUVs = new List<Vector2>();
            List<Vector3> anchorTargets = new List<Vector3>();

            if (fitCustomAnchors && customAnchors != null)
            {
                foreach (var anchor in customAnchors)
                {
                    if (!anchor.targetTransform || anchor.isReleased) continue;

                    var au = (float)Mathf.Clamp(anchor.gridX, 0, resolutionX - 1) / (resolutionX - 1);
                    var av = (float)Mathf.Clamp(anchor.gridY, 0, resolutionY - 1) / (resolutionY - 1);

                    var uv = new Vector2(au, av);
                    var target = transform.InverseTransformPoint(anchor.targetTransform.position);
                    var existing = anchorUVs.IndexOf(uv);
                    // Match ApplyExplicitAnchors: the last active anchor at a node wins.
                    if (existing >= 0) anchorTargets[existing] = target;
                    else
                    {
                        anchorUVs.Add(uv);
                        anchorTargets.Add(target);
                    }
                }
            }

            int numAnchors = anchorUVs.Count;
            Vector3[] tpsWeights = null;
            Vector3 tpsA0 = Vector3.zero, tpsAx = Vector3.zero, tpsAy = Vector3.zero;

            if (numAnchors >= 3)
            {
                var numEq = numAnchors + 3;
                var l = new float[numEq][];
                for (int index = 0; index < numEq; index++)
                {
                    l[index] = new float[numEq];
                }

                var y = new Vector3[numEq];

                float GetDistSq(Vector2 a, Vector2 b)
                {
                    return (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
                }

                float TpsKernel(Vector2 p1, Vector2 p2)
                {
                    var r2 = GetDistSq(p1, p2);
                    var r = Mathf.Sqrt(r2);
                    return r < 1e-6f ? 0f : r2 * Mathf.Log(r);
                }

                for (var i = 0; i < numAnchors; i++)
                {
                    for (var j = 0; j < numAnchors; j++)
                    {
                        l[i][j] = TpsKernel(anchorUVs[i], anchorUVs[j]);
                    }

                    l[i][numAnchors] = 1f;
                    l[i][numAnchors + 1] = anchorUVs[i].x;
                    l[i][numAnchors + 2] = anchorUVs[i].y;

                    l[numAnchors][i] = 1f;
                    l[numAnchors + 1][i] = anchorUVs[i].x;
                    l[numAnchors + 2][i] = anchorUVs[i].y;

                    y[i] = anchorTargets[i];
                }

                bool solvable = true;
                for (var i = 0; i < numEq; i++)
                {
                    var pivot = i;
                    for (var row = i + 1; row < numEq; row++)
                        if (Mathf.Abs(l[row][i]) > Mathf.Abs(l[pivot][i])) pivot = row;
                    if (Mathf.Abs(l[pivot][i]) < 1e-6f)
                    {
                        solvable = false;
                        break;
                    }
                    if (pivot < numEq && pivot != i)
                    {
                        for (var col = 0; col < numEq; col++)
                        {
                            (l[i][col], l[pivot][col]) = (l[pivot][col], l[i][col]);
                        }

                        (y[i], y[pivot]) = (y[pivot], y[i]);
                    }

                    if (Mathf.Abs(l[i][i]) > 1e-6f)
                    {
                        for (int row = i + 1; row < numEq; row++)
                        {
                            float factor = l[row][i] / l[i][i];
                            for (int col = i; col < numEq; col++) l[row][col] -= factor * l[i][col];
                            y[row] -= factor * y[i];
                        }
                    }
                }

                if (solvable) tpsWeights = new Vector3[numAnchors];
                for (var i = numEq - 1; solvable && i >= 0; i--)
                {
                    var sum = Vector3.zero;
                    for (var j = i + 1; j < numEq; j++)
                        sum += l[i][j] * (j < numAnchors
                            ? tpsWeights[j]
                            : (j == numAnchors ? tpsA0 : (j == numAnchors + 1 ? tpsAx : tpsAy)));
                    var val = (y[i] - sum) / l[i][i];
                    if (i < numAnchors) tpsWeights[i] = val;
                    else if (i == numAnchors) tpsA0 = val;
                    else if (i == numAnchors + 1) tpsAx = val;
                    else if (i == numAnchors + 2) tpsAy = val;
                }
            }

            for (int y = 0; y < resolutionY; y++)
            {
                for (int x = 0; x < resolutionX; x++)
                {
                    int idx = y * resolutionX + x;
                    float u = (float)x / (resolutionX - 1);
                    float v = (float)y / (resolutionY - 1);

                    if (numAnchors > 0 && tpsWeights != null)
                    {
                        Vector3 evalPos = tpsA0 + tpsAx * u + tpsAy * v;

                        for (int i = 0; i < numAnchors; i++)
                        {
                            float r2 = (u - anchorUVs[i].x) * (u - anchorUVs[i].x) +
                                       (v - anchorUVs[i].y) * (v - anchorUVs[i].y);
                            if (r2 > 1e-6f) evalPos += tpsWeights[i] * (r2 * Mathf.Log(Mathf.Sqrt(r2)));
                        }

                        vertices[idx] = evalPos;
                    }
                    else
                    {
                        vertices[idx] = math.lerp(math.lerp(c00, c10, u), math.lerp(c01, c11, u), v);
                    }
                }
            }

            for (var i = 0; i < vertices.Length; i++)
            {
                if (math.all(math.isfinite((float3)vertices[i]))) continue;
                float u = (float)(i % resolutionX) / (resolutionX - 1);
                float v = (float)(i / resolutionX) / (resolutionY - 1);
                vertices[i] = math.lerp(math.lerp(c00, c10, u), math.lerp(c01, c11, u), v);
            }
            for (var i = 0; i < numAnchors; i++)
            {
                int x = Mathf.RoundToInt(anchorUVs[i].x * (resolutionX - 1));
                int y = Mathf.RoundToInt(anchorUVs[i].y * (resolutionY - 1));
                vertices[y * resolutionX + x] = anchorTargets[i];
            }
        }

        void BuildMesh()
        {
            if (_mesh == null)
            {
                var filter = GetComponent<MeshFilter>();
                if (filter.sharedMesh == null || filter.sharedMesh.name != "XPBDNetJobs")
                {
                    _mesh = new Mesh { name = "XPBDNetJobs" };
                    filter.sharedMesh = _mesh;
                }
                else
                {
                    _mesh = filter.sharedMesh;
                }
            }

            _mesh.Clear();
            _mesh.MarkDynamic();

            if (_meshVerts == null || _meshVerts.Length != _particleCount) _meshVerts = new Vector3[_particleCount];
            var uvs = new Vector2[_particleCount];

            if (!Application.isPlaying) BuildInitialVertices(_meshVerts);
            else
                for (var i = 0; i < _particleCount; i++)
                    _meshVerts[i] = transform.InverseTransformPoint(_pos[i]);

            for (var y = 0; y < resolutionY; y++)
            for (var x = 0; x < resolutionX; x++)
                uvs[y * resolutionX + x] = new Vector2((float)x / (resolutionX - 1), (float)y / (resolutionY - 1));

            int qX = resolutionX - 1, qY = resolutionY - 1;
            var tris = new int[qX * qY * 6];
            var tIdx = 0;
            for (var y = 0; y < qY; y++)
            for (var x = 0; x < qX; x++)
            {
                int bl = y * resolutionX + x, br = bl + 1, tl = bl + resolutionX, tr = tl + 1;
                tris[tIdx++] = bl;
                tris[tIdx++] = tl;
                tris[tIdx++] = tr;
                tris[tIdx++] = bl;
                tris[tIdx++] = tr;
                tris[tIdx++] = br;
            }
            
            _mesh.vertices = _meshVerts;
            _mesh.triangles = tris;
            _mesh.uv = uvs;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        protected virtual void GatherColliders()
        {
            if (!_activeColliders.IsCreated)
                _activeColliders = new NativeList<SphereColliderData>(Allocator.Persistent);

            _activeColliders.Clear();

            // Query only colliders within the net's current world-space bounds plus a margin
            // for sphere radii. This replaces the scene-wide FindObjectsByType scan and keeps
            // cost proportional to nearby objects rather than total scene collider count.
            var bounds = _mesh.bounds; // local == world because NetParentFollower forces this GO to origin
            var margin = collisionRadius + 0.25f;
            var extents = bounds.extents + new Vector3(margin, margin, margin);
            int count = Physics.OverlapBoxNonAlloc(bounds.center, extents, _colliderOverlapBuffer,
                Quaternion.identity, collisionLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var c = _colliderOverlapBuffer[i] as SphereCollider;
                if (c == null || !c.enabled) continue;
                var rb = c.attachedRigidbody;
                int id = 0;
                if (rb != null && rb != _selfRb)
                {
                    id = rb.GetInstanceID();
                    if (!_rigidbodyCache.ContainsKey(id)) _rigidbodyCache[id] = rb;
                }
                _activeColliders.Add(new SphereColliderData
                {
                    Position = c.transform.position,
                    Radius = c.radius * math.max(c.transform.lossyScale.x,
                        math.max(c.transform.lossyScale.y, c.transform.lossyScale.z)),
                    InstanceID = id
                });
            }
        }

        private void ProcessDeferredImpulses()
        {
            while (_impulseQueue.TryDequeue(out var impulse))
            {
                if (_rigidbodyCache.TryGetValue(impulse.InstanceID, out var rb) && rb != null && !rb.isKinematic)
                {
                    rb.AddForceAtPosition(impulse.Force, impulse.Point, ForceMode.Impulse);
                    if (_selfRb != null && !_selfRb.isKinematic && _selfRb != rb)
                        _selfRb.AddForceAtPosition(-impulse.Force, impulse.Point, ForceMode.Impulse);
                }
            }
        }

        /// <summary>
        /// Pushes the smoothed vertices produced by last frame's mesh-copy job.
        /// The copy itself is scheduled as a dependency of the solve job at the
        /// end of FixedUpdate and joined at the start of the next one, so it no
        /// longer costs a dedicated main-thread sync point.
        /// </summary>
        private void PushMeshVertices()
        {
            if (!_hasPendingMeshCopy) return;

            _meshVertsNative.CopyTo(_meshVerts);
            _mesh.vertices = _meshVerts;
            _normalsNeedUpdate = true;
            _mesh.RecalculateBounds();
        }

        public void ApplyImpulse(Vector3 worldPos, Vector3 impulse, float radius = 0.3f)
        {
            if (!Application.isPlaying || !_pos.IsCreated) return;

            // The solve job owns _pos and _vel until it is joined. Reading them
            // here without completing first was an unsynchronised main-thread
            // access to buffers a worker thread was actively writing.
            _lastJobHandle.Complete();

            var wp = (float3)worldPos;
            var imp = (float3)impulse;
            var r2 = radius * radius;
            for (var i = 0; i < _particleCount; i++)
            {
                if (_invMass[i] == 0f) continue;
                var d2 = math.lengthsq(_pos[i] - wp);
                if (d2 < r2) _vel[i] += imp * (_invMass[i] * (1f - math.sqrt(d2) / radius));
            }
        }

        /// <summary>Tears down and rebuilds every buffer. Safe to call repeatedly.</summary>
        public void Rebuild()
        {
            if (Application.isPlaying) AllocateAndBuild();
            else BuildNet();
        }

        private void OnDrawGizmos()
        {
            if (!_pos.IsCreated) return;

            // Joining here is what makes reading _pos below legal. Completing a
            // copy of the handle still completes the underlying job, but leaves
            // _lastJobHandle looking unjoined to the rest of the class, so keep
            // the real one.
            if (Application.isPlaying) _lastJobHandle.Complete();

            var totalNodes = resolutionX * resolutionY;
            if (_meshVerts == null || _meshVerts.Length != totalNodes)
            {
                _meshVerts = new Vector3[totalNodes];
                GetCornerPositions(out var c00, out var c10, out var c01, out var c11);
                for (var y = 0; y < resolutionY; y++)
                {
                    for (var x = 0; x < resolutionX; x++)
                    {
                        var u = (float)x / (resolutionX - 1);
                        var v = (float)y / (resolutionY - 1);
                        var worldPos = math.lerp(math.lerp(c00, c10, u), math.lerp(c01, c11, u), v);
                        _meshVerts[y * resolutionX + x] = transform.InverseTransformPoint(worldPos);
                    }
                }
            }

            _gizmosPinMap.Clear();
            if (customAnchors != null)
            {
                foreach (var anchor in customAnchors)
                {
                    if (anchor.targetTransform && !anchor.isReleased)
                    {
                        var cx = Mathf.Clamp(anchor.gridX, 0, resolutionX - 1);
                        var cy = Mathf.Clamp(anchor.gridY, 0, resolutionY - 1);
                        _gizmosPinMap[cy * resolutionX + cx] = anchor.targetTransform;
                    }
                }
            }
            var pinMap = _gizmosPinMap;

            for (var y = 0; y < resolutionY; y++)
            {
                for (var x = 0; x < resolutionX; x++)
                {
                    var idx = y * resolutionX + x;
                    var isBorder = (anchorBorderFraction > 0f) &&
                                   (x == 0 || x == resolutionX - 1 || y == 0 || y == resolutionY - 1);
                    var isPinned = pinMap.ContainsKey(idx) || isBorder;

                    Vector3 worldPos;

                    if (Application.isPlaying && _pos.Length == totalNodes)
                    {
                        worldPos = _pos[idx];
                    }
                    else
                    {
                        worldPos = transform.TransformPoint(_meshVerts[idx]);
                    }

                    Gizmos.color = isPinned ? Color.green : Color.blue;
                    Gizmos.DrawSphere(worldPos, 0.01f);
                }
            }
        }
    }
}

#if UNITY_EDITOR
namespace MoSimLib
{
    [CustomEditor(typeof(NetSolver))]
    public class NetSolverEditor : Editor
    {
        // GUI points: the visible disc and its hit area stay the same size at every zoom/DPI.
        private const float NodeRadius = 7f;
        private const float DragToleranceSqr = 16f;

        private struct ProjectedNode
        {
            public int Index;
            public Transform Target;
            public Vector3 WorldPosition;
            public Vector3 GuiPosition;
        }

        private readonly Dictionary<int, Transform> _pinMap = new Dictionary<int, Transform>();
        private readonly List<ProjectedNode> _nodes = new List<ProjectedNode>();
        private int _controlId;
        private int _pressedIndex = -1;
        private Transform _pressedTarget;
        private Vector2 _pressPosition;
        private bool _dragged;
        private int _selectionVersion;

        private void OnSceneGUI()
        {
            var solver = (NetSolver)target;
            var view = SceneView.currentDrawingSceneView;
            if (view == null || view.camera == null) return;
            Event e = Event.current;
            _controlId = GUIUtility.GetControlID(solver.GetInstanceID(), FocusType.Passive);
            CollectNodes(solver, view.camera);
            int nearest = FindNearestNode(_nodes, e.mousePosition);
            bool navigating = e.alt || Tools.viewToolActive;

            switch (e.GetTypeForControl(_controlId))
            {
                case EventType.Layout:
                case EventType.MouseMove:
                    // One winner avoids overlapping sphere controls all reporting distance zero.
                    if (!navigating && nearest >= 0) HandleUtility.AddControl(_controlId, 0f);
                    if (e.type == EventType.MouseMove) view.Repaint();
                    break;
                case EventType.MouseDown:
                    if (e.button != 0 || navigating || nearest < 0 || GUIUtility.hotControl != 0 ||
                        HandleUtility.nearestControl != _controlId) break;
                    _pressedIndex = _nodes[nearest].Index;
                    _selectionVersion++;
                    _pressedTarget = _nodes[nearest].Target;
                    _pressPosition = e.mousePosition;
                    _dragged = false;
                    GUIUtility.hotControl = _controlId;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != _controlId) break;
                    _dragged |= (e.mousePosition - _pressPosition).sqrMagnitude > DragToleranceSqr;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (e.button != 0 || GUIUtility.hotControl != _controlId) break;
                    bool clicked = !navigating && !_dragged && nearest >= 0 &&
                        (e.mousePosition - _pressPosition).sqrMagnitude <= DragToleranceSqr &&
                        _nodes[nearest].Index == _pressedIndex && _nodes[nearest].Target == _pressedTarget;
                    Transform picked = clicked ? _pressedTarget : null;
                    ReleaseControl();
                    e.Use();
                    if (picked != null)
                    {
                        int[] selection = Selection.instanceIDs;
                        int version = _selectionVersion;
                        EditorApplication.delayCall += () =>
                        {
                            if (this != null && version == _selectionVersion && picked != null &&
                                Selection.instanceIDs.SequenceEqual(selection))
                                Selection.activeGameObject = picked.gameObject;
                        };
                    }
                    break;
                case EventType.KeyDown:
                    if (e.keyCode != KeyCode.Escape || GUIUtility.hotControl != _controlId) break;
                    ReleaseControl();
                    e.Use();
                    break;
                case EventType.Ignore:
                    if (GUIUtility.hotControl == _controlId) ReleaseControl();
                    break;
                case EventType.Repaint:
                    bool hovered = !navigating && HandleUtility.nearestControl == _controlId &&
                        (GUIUtility.hotControl == 0 || GUIUtility.hotControl == _controlId);
                    DrawNodes(view.camera, hovered ? nearest : -1, solver.resolutionX);
                    break;
            }
        }

        private void CollectNodes(NetSolver solver, Camera camera)
        {
            _nodes.Clear();
            _pinMap.Clear();
            int resX = solver.resolutionX;
            int resY = solver.resolutionY;
            if (resX < 2 || resY < 2) return;
            int count = resX * resY;
            if (solver.customAnchors != null)
            {
                foreach (var anchor in solver.customAnchors)
                {
                    if (anchor.targetTransform == null || anchor.isReleased) continue;
                    int x = Mathf.Clamp(anchor.gridX, 0, resX - 1);
                    int y = Mathf.Clamp(anchor.gridY, 0, resY - 1);
                    // Match the solver: the last active anchor at this coordinate wins.
                    _pinMap[y * resX + x] = anchor.targetTransform;
                }
            }

            var vertices = solver.GetMeshVertices();
            var positions = Application.isPlaying ? solver.GetRuntimePositions() : default;
            bool useRuntime = Application.isPlaying && positions.IsCreated && positions.Length == count;
            if (!useRuntime && (vertices == null || vertices.Length != count)) return;
            foreach (var pin in _pinMap)
            {
                Vector3 position = useRuntime ? (Vector3)positions[pin.Key]
                    : solver.transform.TransformPoint(vertices[pin.Key]);
                Vector3 guiPosition = HandleUtility.WorldToGUIPointWithDepth(position);
                if (!math.all(math.isfinite((float3)guiPosition)) ||
                    guiPosition.z < camera.nearClipPlane || guiPosition.z > camera.farClipPlane) continue;
                _nodes.Add(new ProjectedNode
                {
                    Index = pin.Key, Target = pin.Value, WorldPosition = position, GuiPosition = guiPosition
                });
            }
            _nodes.Sort((a, b) =>
            {
                int depth = b.GuiPosition.z.CompareTo(a.GuiPosition.z);
                return depth != 0 ? depth : b.Index.CompareTo(a.Index);
            });
        }

        private static int FindNearestNode(List<ProjectedNode> nodes, Vector2 mouse)
        {
            int nearest = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < nodes.Count; i++)
            {
                ProjectedNode node = nodes[i];
                float distance = ((Vector2)node.GuiPosition - mouse).sqrMagnitude;
                if (!math.all(math.isfinite((float3)node.GuiPosition)) || node.GuiPosition.z <= 0f ||
                    distance > NodeRadius * NodeRadius) continue;
                if (nearest >= 0 && (distance > bestDistance || (distance == bestDistance &&
                    (node.GuiPosition.z > nodes[nearest].GuiPosition.z ||
                        (node.GuiPosition.z == nodes[nearest].GuiPosition.z && node.Index > nodes[nearest].Index)))))
                    continue;
                nearest = i;
                bestDistance = distance;
            }
            return nearest;
        }

        private void DrawNodes(Camera camera, int hovered, int resX)
        {
            Color oldColor = Handles.color;
            var oldDepth = Handles.zTest;
            try
            {
                Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                for (int i = 0; i < _nodes.Count; i++)
                    if (i != hovered) DrawNode(camera, _nodes[i], Color.green);
                if (hovered >= 0)
                {
                    ProjectedNode node = _nodes[hovered];
                    // Keep the actual click target visible even in a cluster of overlapping nodes.
                    DrawNode(camera, node, Handles.preselectionColor);
                    Handles.Label(node.WorldPosition,
                        $"  {node.Target.name} [{node.Index % resX}, {node.Index / resX}]");
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.zTest = oldDepth;
            }
        }

        private static void DrawNode(Camera camera, ProjectedNode node, Color color)
        {
            Vector3 screen = camera.WorldToScreenPoint(node.WorldPosition);
            Vector3 edge = screen + Vector3.right * (NodeRadius * EditorGUIUtility.pixelsPerPoint);
            float radius = Vector3.Distance(node.WorldPosition, camera.ScreenToWorldPoint(edge));
            Handles.color = color;
            Handles.DrawSolidDisc(node.WorldPosition, camera.transform.forward, radius);
            Handles.color = Color.black;
            Handles.DrawWireDisc(node.WorldPosition, camera.transform.forward, radius);
        }

        private void ReleaseControl()
        {
            if (_controlId != 0 && GUIUtility.hotControl == _controlId) GUIUtility.hotControl = 0;
            _pressedIndex = -1;
            _pressedTarget = null;
        }

        private void OnDisable()
        {
            _selectionVersion++;
            ReleaseControl();
        }
    }
}
#endif
