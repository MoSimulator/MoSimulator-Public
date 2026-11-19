using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Games.Rebuilt.Robots;
using Unity.Collections;
using UnityEngine;

namespace Games.Rebuilt.GamePieceSystem
{
    /// <summary>
    /// Allows bounded overlap on the upper surface of fuel touching the lower chassis.
        /// Registration and snapshots run on the main thread; workers share synchronized fuel state.
    /// This changes solver contacts, not collider geometry or wheel raycast results.
    /// </summary>
    public sealed class RebuiltFuelContactModifier : IDisposable
    {
        private readonly Dictionary<int, FuelCollider[]> _fuelSlots = new();
        private readonly Dictionary<int, FuelSolver> _fuelSolvers = new();
        private readonly Dictionary<int, RobotBody> _robotBodies = new();
        private readonly List<CompressionCeiling> _nextRobotRegions = new();
        private readonly List<BoxCollider> _chassisBounds = new();
        private readonly ConcurrentQueue<FuelSolver> _pendingSolvers = new();
        private readonly List<FuelSolver> _activeSolvers = new();
        private readonly List<int> _deadBodies = new();
        private Dictionary<int, FuelShape> _fuelShapes = new();
        private Dictionary<int, int> _robotRegionIndices = new();
        private CompressionCeiling[] _robotRegions = Array.Empty<CompressionCeiling>();
        private bool _dirty = true;
        private bool _robotsDirty = true;
        private bool _enabled;
        private bool _contactsActive;
        private volatile StepData _step;
        private int _stepEpoch;

        [ThreadStatic] private static List<PreparedPair> _pairBuffer;

        private readonly struct RobotBody
        {
            public readonly Rigidbody Body;
            public readonly BoxCollider ChassisBounds;
            public readonly RobotCollider[] Colliders;

            public RobotBody(Rigidbody body, BoxCollider chassisBounds)
            {
                Body = body;
                ChassisBounds = chassisBounds;
                var colliders = body.GetComponentsInChildren<Collider>(true);
                var owned = new List<RobotCollider>(colliders.Length);
                foreach (var collider in colliders)
                    // Disabled colliders report no attachedRigidbody until Unity creates their shape.
                    if (!collider.isTrigger && (collider.attachedRigidbody == body ||
                        collider.GetComponentInParent<Rigidbody>(true) == body))
                        owned.Add(new RobotCollider(collider));
                Colliders = owned.ToArray();
            }

            public void SetContactsActive(bool active)
            {
                foreach (var collider in Colliders) collider.SetActive(active);
            }
        }

        private readonly struct RobotCollider
        {
            private readonly Collider _collider;
            private readonly bool _originalFlag;

            public RobotCollider(Collider collider)
            {
                _collider = collider;
                _originalFlag = collider.hasModifiableContacts;
            }

            public void SetActive(bool active)
            {
                if (_collider) _collider.hasModifiableContacts = active || _originalFlag;
            }
        }

        private readonly struct FuelShape
        {
            public readonly float Radius;
            public readonly FuelContactState State;

            public FuelShape(SphereCollider sphere, FuelContactState state)
            {
                var scale = sphere.transform.lossyScale;
                Radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                State = state;
            }
        }

        private sealed class FuelContactState
        {
            private readonly object _gate = new object();
            private int _epoch;
            private int _robot;
            private int _support;
            private float _compression;
            private Vector3 _supportNormal;
            private bool _tracked;
            private readonly FuelSolver _solver;
            private readonly ConcurrentQueue<FuelSolver> _pending;

            public FuelContactState(FuelSolver solver, ConcurrentQueue<FuelSolver> pending)
            {
                _solver = solver;
                _pending = pending;
            }

            public void MarkPressed(int epoch, int robot, int support, float compression, Vector3 localNormal)
            {
                lock (_gate)
                {
                    // Keep the main support while crossing a lower lip, even if its contact drops
                    // out briefly. Reclassifying that lip as the main support would pin fuel into
                    // the floor. A less recessed surface can take over; release expires the state.
                    bool continuous = _support != 0 && _robot == robot && unchecked((uint)epoch - (uint)_epoch) <= 1u;
                    if (!continuous || support == _support || compression < _compression - 0.001f)
                    {
                        if (!continuous || support != _support) _supportNormal = localNormal;
                        _support = support;
                        _compression = compression;
                    }
                    _epoch = epoch;
                    _robot = robot;
                    if (!_tracked)
                    {
                        _tracked = true;
                        _pending.Enqueue(_solver);
                    }
                }
            }

            public int PressedSupport(int epoch, int robot, out Vector3 localNormal)
            {
                lock (_gate)
                {
                    localNormal = _supportNormal;
                    return _robot == robot && unchecked((uint)epoch - (uint)_epoch) <= 1u ? _support : 0;
                }
            }

            public bool IsLoaded(int epoch)
            {
                lock (_gate)
                {
                    bool loaded = _support != 0 && unchecked((uint)epoch - (uint)_epoch) <= 1u;
                    if (!loaded) _tracked = false;
                    return loaded;
                }
            }

            public void Reset()
            {
                lock (_gate)
                {
                    _tracked = false;
                    _support = 0;
                    _robot = 0;
                }
            }
        }

        private sealed class FuelSolver
        {
            private readonly Rigidbody _body;
            public readonly FuelContactState State;
            private readonly int _positionIterations;
            private readonly int _velocityIterations;
            private bool _boosted;
            private bool _registered = true;

            public FuelSolver(Rigidbody body, ConcurrentQueue<FuelSolver> pending)
            {
                _body = body;
                State = new FuelContactState(this, pending);
                _positionIterations = body.solverIterations;
                _velocityIterations = body.solverVelocityIterations;
            }

            public bool Prepare(int epoch)
            {
                bool loaded = _registered && State.IsLoaded(epoch);
                if (loaded == _boosted) return loaded;
                if (loaded)
                {
                    if (_body)
                    {
                        _body.solverIterations = Mathf.Max(8, _positionIterations);
                        _body.solverVelocityIterations = Mathf.Max(4, _velocityIterations);
                    }
                    _boosted = true;
                }
                else Restore();
                return loaded;
            }

            public void Unregister()
            {
                _registered = false;
                Restore();
                State.Reset();
            }

            public void Restore()
            {
                if (!_boosted) return;
                if (_body)
                {
                    _body.solverIterations = _positionIterations;
                    _body.solverVelocityIterations = _velocityIterations;
                }
                _boosted = false;
            }
        }

        private readonly struct CompressionCeiling
        {
            public readonly Vector3 Up;
            public readonly float Height;
            public readonly int RobotGroup;

            public CompressionCeiling(BoxCollider bounds)
            {
                var frame = bounds.transform;
                Up = frame.up;
                Vector3 top = frame.TransformPoint(bounds.center + Vector3.up * (bounds.size.y * 0.5f));
                Height = Vector3.Dot(top, Up);
                RobotGroup = bounds.GetInstanceID();
            }
        }

        private readonly struct FuelCollider
        {
            public readonly SphereCollider Collider;
            public readonly int Id;
            public readonly FuelContactState State;

            public FuelCollider(SphereCollider collider, FuelContactState state)
            {
                Collider = collider;
                Id = collider.GetInstanceID();
                State = state;
            }
        }

        private sealed class StepData
        {
            public readonly Dictionary<int, FuelShape> FuelShapes;
            public readonly Dictionary<int, int> RobotRegionIndices;
            public readonly CompressionCeiling[] RobotRegions;
            public readonly Vector3 Up;
            public readonly float Depth;
            public readonly float StartDot;
            public readonly float FrictionMultiplier;
            public readonly bool CompressedEdgeGlide;
            public readonly float EdgeRelief;
            public readonly float InverseBlendRange;
            public readonly float InverseDepth;

            public StepData(Dictionary<int, FuelShape> fuelShapes,
                Dictionary<int, int> robotRegionIndices, CompressionCeiling[] robotRegions, Vector3 up,
                float depth, float startDot, float frictionMultiplier, bool compressedEdgeGlide)
            {
                FuelShapes = fuelShapes;
                RobotRegionIndices = robotRegionIndices;
                RobotRegions = robotRegions;
                Up = up;
                Depth = depth;
                StartDot = startDot;
                FrictionMultiplier = frictionMultiplier;
                CompressedEdgeGlide = compressedEdgeGlide;
                EdgeRelief = Mathf.Min(0.01f, depth * 0.25f);
                InverseBlendRange = 1f / (1f - startDot);
                InverseDepth = depth > 0f ? 1f / depth : 0f;
            }
        }

        private readonly struct PreparedPair
        {
            public readonly ModifiableContactPair Pair;
            public readonly FuelShape Fuel;
            public readonly CompressionCeiling Region;
            public readonly int RobotCollider;
            public readonly float Direction;
            public readonly Quaternion InverseRobotRotation;

            public PreparedPair(ModifiableContactPair pair, FuelShape fuel, CompressionCeiling region,
                bool fuelFirst, bool ccd)
            {
                Pair = pair;
                Fuel = fuel;
                Region = region;
                RobotCollider = fuelFirst ? pair.otherColliderInstanceID : pair.colliderInstanceID;
                Direction = (fuelFirst ? -1f : 1f) * (ccd ? -1f : 1f);
                InverseRobotRotation = Quaternion.Inverse(fuelFirst ? pair.otherRotation : pair.rotation);
            }
        }

        public void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            Physics.ContactModifyEvent += ModifyContacts;
            Physics.ContactModifyEventCCD += ModifyCCDContacts;
        }

        public void Disable()
        {
            _step = null;
            SetContactsActive(false);
            ResetSolvers();
            if (!_enabled) return;
            Physics.ContactModifyEvent -= ModifyContacts;
            Physics.ContactModifyEventCCD -= ModifyCCDContacts;
            _enabled = false;
        }

        private void SetContactsActive(bool active)
        {
            if (_contactsActive == active) return;
            foreach (var robot in _robotBodies.Values) robot.SetContactsActive(active);
            _contactsActive = active;
        }

        private void ResetSolvers()
        {
            foreach (var solver in _fuelSolvers.Values)
            {
                solver.Restore();
                solver.State.Reset();
            }
            _activeSolvers.Clear();
            while (_pendingSolvers.TryDequeue(out _)) { }
        }

        public void RegisterFuel(int slot, Rigidbody body)
        {
            UnregisterFuel(slot);
            var colliders = body.GetComponentsInChildren<SphereCollider>(true);
            var owned = new List<FuelCollider>(colliders.Length);
            var solver = new FuelSolver(body, _pendingSolvers);
            foreach (var collider in colliders)
            {
                if (collider.isTrigger || (collider.attachedRigidbody != body &&
                    collider.GetComponentInParent<Rigidbody>(true) != body)) continue;
                owned.Add(new FuelCollider(collider, solver.State));
            }
            _fuelSlots.Add(slot, owned.ToArray());
            _fuelSolvers.Add(slot, solver);
            _dirty = true;
        }

        public void UnregisterFuel(int slot)
        {
            if (_fuelSolvers.TryGetValue(slot, out var solver))
            {
                solver.Unregister();
                _fuelSolvers.Remove(slot);
            }
            if (!_fuelSlots.TryGetValue(slot, out var colliders)) return;
            _fuelSlots.Remove(slot);
            _dirty = true;
        }

        public void RegisterRobot(RebuiltRobotBase robot)
        {
            // The root box describes the bumper/chassis envelope, including on 2910B and Simbotics.
            // Use the same ceiling for articulated bodies so a hopper's own pose cannot enable it.
            var chassisBounds = robot.GetComponent<BoxCollider>();
            foreach (var body in robot.GetComponentsInChildren<Rigidbody>(true))
            {
                // Picked-up fuel can be parented to a robot, but must never become robot geometry.
                if (body.GetComponent<RebuiltGamePieceController>() != null) continue;
                RegisterRobotBody(body, chassisBounds);
            }
        }

        // Also used by isolated physics validation without instantiating an entire robot.
        public void RegisterRobotBody(Rigidbody body, BoxCollider chassisBounds = null)
        {
            if (!chassisBounds) chassisBounds = body.GetComponent<BoxCollider>();
            // Without a chassis envelope, leave contacts rigid rather than softening an entire robot.
            if (!chassisBounds) return;
            int id = body.GetInstanceID();
            if (_robotBodies.TryGetValue(id, out var previous)) previous.SetContactsActive(false);
            var robotBody = new RobotBody(body, chassisBounds);
            _robotBodies[id] = robotBody;
            robotBody.SetContactsActive(_contactsActive);
            _robotsDirty = true;
        }

        public void PrepareStep(Vector3 gravity, float depth, float startDot, float frictionMultiplier = 6f,
            bool compressedEdgeGlide = true)
        {
            _stepEpoch = unchecked(_stepEpoch + 1);
            depth = Mathf.Clamp(depth, 0f, 0.05f);
            Vector3 up = -gravity.normalized;
            bool active = _enabled && depth > 0f && up.sqrMagnitude > 0f;
            if (!active)
            {
                bool wasActive = _contactsActive;
                _step = null;
                SetContactsActive(false);
                if (wasActive) ResetSolvers();
                return;
            }
            SetContactsActive(true);
            // Workers report a fuel only on its first loaded contact. Visit loaded/releasing fuel,
            // not the hundreds of freely rolling or sleeping bodies registered with the manager.
            while (_pendingSolvers.TryDequeue(out var pending)) _activeSolvers.Add(pending);
            for (int i = _activeSolvers.Count - 1; i >= 0; i--)
            {
                if (_activeSolvers[i].Prepare(_stepEpoch)) continue;
                int last = _activeSolvers.Count - 1;
                _activeSolvers[i] = _activeSolvers[last];
                _activeSolvers.RemoveAt(last);
            }
            _deadBodies.Clear();
            foreach (var entry in _robotBodies)
            {
                if (!entry.Value.Body || !entry.Value.ChassisBounds)
                {
                    _deadBodies.Add(entry.Key);
                    entry.Value.SetContactsActive(false);
                    _robotsDirty = true;
                    continue;
                }
            }

            foreach (int id in _deadBodies) _robotBodies.Remove(id);

            bool regionsChanged = _robotsDirty;
            if (_robotsDirty)
            {
                // Body-to-chassis ownership changes only on registration/removal. Publish a fresh
                // immutable map then; moving robots only need a small array of unique chassis planes.
                var indices = new Dictionary<int, int>(_robotBodies.Count);
                var chassisIndices = new Dictionary<int, int>();
                _chassisBounds.Clear();
                foreach (var entry in _robotBodies)
                {
                    int chassisId = entry.Value.ChassisBounds.GetInstanceID();
                    if (!chassisIndices.TryGetValue(chassisId, out int index))
                    {
                        index = _chassisBounds.Count;
                        chassisIndices.Add(chassisId, index);
                        _chassisBounds.Add(entry.Value.ChassisBounds);
                    }
                    indices.Add(entry.Key, index);
                }
                _robotRegionIndices = indices;
                _robotsDirty = false;
            }
            _nextRobotRegions.Clear();
            for (int i = 0; i < _chassisBounds.Count; i++)
            {
                var region = new CompressionCeiling(_chassisBounds[i]);
                _nextRobotRegions.Add(region);
                if (i >= _robotRegions.Length || _robotRegions[i].RobotGroup != region.RobotGroup ||
                    _robotRegions[i].Up != region.Up || _robotRegions[i].Height != region.Height)
                    regionsChanged = true;
            }
            if (_dirty)
            {
                // Never mutate a snapshot after publishing it: callbacks may run on multiple workers.
                _fuelShapes = new Dictionary<int, FuelShape>();
                foreach (var colliders in _fuelSlots.Values)
                    foreach (var collider in colliders)
                        if (collider.Collider) _fuelShapes[collider.Id] = new FuelShape(collider.Collider, collider.State);
                _dirty = false;
            }

            // Horizontal driving does not change these planes. Publish only when a plane or registration changes.
            if (regionsChanged)
                _robotRegions = _nextRobotRegions.ToArray();

            startDot = Mathf.Clamp(startDot, 0f, 0.95f);
            frictionMultiplier = Mathf.Clamp(frictionMultiplier, 1f, 12f);
            var previous = _step;
            if (previous != null && previous.FuelShapes == _fuelShapes && previous.RobotRegions == _robotRegions &&
                previous.RobotRegionIndices == _robotRegionIndices &&
                previous.CompressedEdgeGlide == compressedEdgeGlide &&
                previous.Up == up && previous.Depth == depth && previous.StartDot == startDot &&
                previous.FrictionMultiplier == frictionMultiplier) return;
            _step = new StepData(_fuelShapes, _robotRegionIndices, _robotRegions, up, depth, startDot,
                frictionMultiplier, compressedEdgeGlide);
        }

        private void ModifyContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            Modify(pairs, false);
        }

        private void ModifyCCDContacts(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            Modify(pairs, true);
        }

        private void Modify(NativeArray<ModifiableContactPair> pairs, bool ccd)
        {
            var step = _step;
            if (step == null || step.Depth <= 0f || step.Up.sqrMagnitude == 0f) return;
            int epoch = Volatile.Read(ref _stepEpoch);
            // A callback's scratch list belongs to this worker thread and never escapes the call.
            // Classify once, keeping both phases so edge contacts can use later support contacts.
            var prepared = _pairBuffer ??= new List<PreparedPair>();
            prepared.Clear();
            try
            {
                foreach (var pair in pairs)
                {
                    bool fuelFirst = step.FuelShapes.TryGetValue(pair.colliderInstanceID, out var firstFuel);
                    bool fuelSecond = step.FuelShapes.TryGetValue(pair.otherColliderInstanceID, out var secondFuel);
                    if (fuelFirst == fuelSecond) continue;
                    int robotId = fuelFirst ? pair.otherBodyInstanceID : pair.bodyInstanceID;
                    if (!step.RobotRegionIndices.TryGetValue(robotId, out int regionIndex)) continue;
                    prepared.Add(new PreparedPair(pair, fuelFirst ? firstFuel : secondFuel,
                        step.RobotRegions[regionIndex], fuelFirst, ccd));
                }
                ModifyPrepared(prepared, step, epoch);
            }
            finally
            {
                // Do not retain contact pointers or fuel states between physics callbacks.
                prepared.Clear();
            }
        }

        private static void ModifyPrepared(List<PreparedPair> pairs, StepData step, int epoch)
        {
            // First collect overhead compression, independent of contact order within this batch.
            // Per-fuel state also bridges worker batches and lasts one extra physics step at an edge.
            if (step.CompressedEdgeGlide) MarkOverheadCompression(pairs, step, epoch);
            foreach (var prepared in pairs)
            {
                var pair = prepared.Pair;
                var fuel = prepared.Fuel;
                var region = prepared.Region;
                int robotCollider = prepared.RobotCollider;
                Vector3 supportNormal = Vector3.zero;
                int support = step.CompressedEdgeGlide ? fuel.State.PressedSupport(epoch, region.RobotGroup, out supportNormal) : 0;
                Quaternion inverseRobotRotation = prepared.InverseRobotRotation;
                // Edge-only clearance is capped at 1 cm and does not alter the overhead support.
                float capHeight = Mathf.Max(fuel.Radius * 0.1f, fuel.Radius - step.Depth - step.EdgeRelief);

                // Discrete normals point from collider 2 to collider 1. CCD reports the reverse.
                float direction = prepared.Direction;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    // An upward-facing fuel contact also occurs inside a packed hopper. Only the
                    // lower chassis may compress fuel; force direction cannot distinguish those cases.
                    if (Vector3.Dot(pair.GetPoint(i), region.Up) > region.Height) continue;
                    Vector3 normal = pair.GetNormal(i) * direction;
                    float alignment = Vector3.Dot(normal, step.Up);
                    float separation = pair.GetSeparation(i);
                    float adjustedSeparation;
                    float frictionSeparation = separation;
                    // Compare with the loaded surface in its solver frame, not with world up.
                    // The main support remains the same plane as the robot rocks or gravity changes.
                    bool supportEdge = robotCollider == support &&
                        Vector3.Dot(inverseRobotRotation * normal, supportNormal) < 0.9999f;
                    if (support != 0 && (robotCollider != support || supportEdge) &&
                        alignment > 0f && alignment * fuel.Radius >= capHeight)
                    {
                        // Clip only the part of the sphere already allowed to compress. Reorienting
                        // a corner normal while leaving an off-axis contact creates artificial torque
                        // and can roll fuel back beneath the robot. Keep the native normal and point.
                        float height = alignment * (fuel.Radius + separation);
                        float clippedSeparation = (height - capHeight) / alignment;
                        if (clippedSeparation >= 0f)
                        {
                            pair.IgnoreContact(i);
                            continue;
                        }
                        adjustedSeparation = Mathf.Max(separation, clippedSeparation);
                        frictionSeparation = height - fuel.Radius;
                    }
                    else
                    {
                        float blend = Mathf.Clamp01((alignment - step.StartDot) * step.InverseBlendRange);
                        blend = blend * blend * (3f - 2f * blend);
                        if (blend <= 0f) continue;
                        adjustedSeparation = separation + step.Depth * blend;
                    }
                    // Measure real overlap before adding the allowance. Scaling the already combined
                    // coefficients preserves each material pair and keeps static/sliding friction in step.
                    float compression = Mathf.Clamp01(-frictionSeparation * step.InverseDepth);
                    float frictionScale = Mathf.Lerp(1f, step.FrictionMultiplier, compression);
                    if (frictionScale > 1f)
                    {
                        pair.SetStaticFriction(i, pair.GetStaticFriction(i) * frictionScale);
                        pair.SetDynamicFriction(i, pair.GetDynamicFriction(i) * frictionScale);
                    }
                    pair.SetSeparation(i, adjustedSeparation);
                }
            }
        }

        private static void MarkOverheadCompression(List<PreparedPair> pairs, StepData step, int epoch)
        {
            foreach (var prepared in pairs)
            {
                var pair = prepared.Pair;
                var region = prepared.Region;
                float direction = prepared.Direction;
                Quaternion inverseRobotRotation = prepared.InverseRobotRotation;
                for (int i = 0; i < pair.contactCount; i++)
                {
                    if (Vector3.Dot(pair.GetPoint(i), region.Up) > region.Height ||
                        Vector3.Dot(pair.GetNormal(i) * direction, step.Up) < 0.98f ||
                        pair.GetSeparation(i) > -step.Depth * 0.5f) continue;
                    prepared.Fuel.State.MarkPressed(epoch, region.RobotGroup,
                        prepared.RobotCollider, -pair.GetSeparation(i), inverseRobotRotation * (pair.GetNormal(i) * direction));
                    break;
                }
            }
        }

        public void Dispose()
        {
            Disable();
            _fuelSlots.Clear();
            _fuelSolvers.Clear();
            _robotBodies.Clear();
            _nextRobotRegions.Clear();
            _chassisBounds.Clear();
            _fuelShapes = new Dictionary<int, FuelShape>();
            _robotRegionIndices = new Dictionary<int, int>();
            _robotRegions = Array.Empty<CompressionCeiling>();
            _dirty = true;
            _robotsDirty = true;
        }
    }
}
