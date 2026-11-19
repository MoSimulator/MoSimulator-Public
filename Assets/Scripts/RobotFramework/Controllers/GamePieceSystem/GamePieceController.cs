// GamePieceController.cs - State holder with minimal logic
using System;
using System.Collections;
using System.Collections.Generic;
using MoSimLib;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public abstract class GamePieceController<TPiece, TData> : MonoBehaviour
        where TPiece : GamePiece<TData>
        where TData : IGamePieceData
    {
        [SerializeField] protected TData gamePieceData;
        protected TPiece gamePiece;

        public TPiece GamePiece => gamePiece;
        public string PieceName => gamePieceData.Name;
        public bool IsScored { get; set; } = false;

        // State
        public Vector3 Distance { get; private set; }
        public float DistanceMagnitude { get; private set; }

        private float _startingMass;
        private float _startingDrag;
        private float _startingAngularDrag;
        private bool _moving;
        private bool _isControlled;
        private RobotBase _ignoredRobot;
        private Collider[] _ignoredRobotColliders = Array.Empty<Collider>();

        private Transform _gamePieceWorld;
        private Collider[] _colliders;
        private Transform _colliderParent;
        private Vector3 _startPosition;
        private Quaternion _startRotation;

        private void Awake()
        {
            gamePiece = Activator.CreateInstance<TPiece>();
            gamePiece.Initialize(gamePieceData, gameObject);
        }

        protected void Start()
        {
            _moving = false;
            _isControlled = false;
            _colliderParent = Utils.FindChild("Colliders", gameObject).transform;
            _startPosition = _colliderParent.localPosition;
            _startRotation = _colliderParent.localRotation;
            _colliders = new Collider[_colliderParent.transform.childCount];
            
            for (var i = 0; i < _colliderParent.transform.childCount; i++)
            {
                _colliders[i] = _colliderParent.transform.GetChild(i).GetComponent<Collider>();
            }

            if (!GamePiece.rigidbody)
            {
                GamePiece.rigidbody = gameObject.GetComponent<Rigidbody>();
                _startingMass = GamePiece.rigidbody.mass;
                _startingDrag = GamePiece.rigidbody.drag;
                _startingAngularDrag = GamePiece.rigidbody.angularDrag;
            }

            if (GamePiece.rigidbody)
            {
                GamePiece.rigidbody.automaticInertiaTensor = false;
                GamePiece.rigidbody.inertiaTensor = new Vector3(0.01f, 0.01f, 0f);
                GamePiece.rigidbody.inertiaTensorRotation = Quaternion.identity;
                GamePiece.rigidbody.ResetInertiaTensor();
            }
            else
            {
                Debug.LogError("GamePieceController: Rigidbody is null on " + gameObject.name);
            }
        }

        private void LateUpdate()
        {
            if (!_gamePieceWorld)
            {
                _gamePieceWorld = GameObject.FindGameObjectWithTag("GamePieceWorld").transform;
            }

            if (!GamePiece.rigidbody)
            {
                GamePiece.rigidbody = gameObject.GetComponent<Rigidbody>();
                _startingMass = GamePiece.rigidbody.mass;
                _startingDrag = GamePiece.rigidbody.drag;
                _startingAngularDrag = GamePiece.rigidbody.angularDrag;
            }

            if (!GamePiece.transform)
                GamePiece.transform = gameObject.transform;

            if (!GamePiece.owner)
                GamePiece.owner = transform.parent.gameObject;

            if (!GamePiece.gameObject)
                GamePiece.gameObject = gameObject;

            if (GamePiece.owner.layer == LayerMask.NameToLayer("Robot") && !_moving)
            {
                Controlled();
            }
        }

        private void OnDestroy()
        {
            PieceController.ClearState(gameObject.GetInstanceID());
        }

        /// <summary>
        /// Undoes everything a robot did to this piece while carrying it, so it can be parked and
        /// handed out again instead of destroyed. Mirrors <see cref="UseRobot"/> - a piece that is
        /// released normally goes through that, and a pooled one has to end up in the same state -
        /// plus the collider child, which is reparented onto the robot while carried.
        /// Returns false if the piece can no longer be reused, which the pool answers by replacing it.
        /// </summary>
        public bool ReturnToPool()
        {
            StopAllCoroutines();

            // _colliders is only assigned in Start, so a null one means this piece has never been
            // used and needs no undoing. A non-null one with no collider child left means the child
            // was destroyed along with the robot carrying it, and the piece is unusable for good.
            var used = _colliders != null;
            if (used && !_colliderParent) return false;

            var body = GamePiece != null && GamePiece.rigidbody ? GamePiece.rigidbody : GetComponent<Rigidbody>();

            // Only a piece that was being carried has any of this to undo. IgnoreRobot is the sole
            // thing that moves the collider child onto the robot and rewrites the layer filters on
            // every one of them, and UseRobot has already put it all back for anything released
            // normally - so undoing it on every pooled piece meant thousands of PhysX filter writes
            // and an inertia recompute per restart, on pieces that were already correct.
            // The parent check covers the frame between Move parenting the colliders onto a robot and
            // LateUpdate noticing, where the flag has not caught up yet.
            if (used && (_isControlled || _colliderParent.parent != transform))
            {
                _colliderParent.parent = transform;
                _colliderParent.localPosition = _startPosition;
                _colliderParent.localRotation = _startRotation;

                var noExclusions = LayerMask.GetMask();

                for (var i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i])
                    {
                        _colliders[i].excludeLayers = noExclusions;
                    }
                }

                if (body)
                {
                    body.excludeLayers = noExclusions;
                    body.automaticCenterOfMass = true;
                    body.automaticInertiaTensor = false;
                    body.inertiaTensor = new Vector3(0.01f, 0.01f, 0f);
                    body.inertiaTensorRotation = Quaternion.identity;
                    body.ResetInertiaTensor();

                    body.mass = _startingMass;
                    body.drag = _startingDrag;
                    body.angularDrag = _startingAngularDrag;
                }
            }

            // Going kinematic drops the force and torque the piece has accumulated but not yet
            // integrated, which is the momentum that used to survive a restart and fling things. Only
            // a live body has any to drop, and writing these to one that is already kinematic is both
            // pointless and a logged warning per call.
            if (body && !body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            IsScored = false;
            _isControlled = false;
            _moving = false;

            if (GamePiece != null)
            {
                GamePiece.isScored = false;
                GamePiece.owner = null;
            }

            // Normally cleared by OnDestroy, which a pooled piece never reaches.
            PieceController.ClearState(gameObject.GetInstanceID());
            return true;
        }

        public MoSimResult<int> MoveBreakable(Transform target, float intakeSpeed, float intakeForce, float maxDistance,
            float accuracy, float rotationForce, float maxRotationSpeed, bool useRotation, bool smoothHandoff,
            bool planarTolerance, Vector3 boxExtents, bool lockXAxis, bool lockYAxis, bool lockZAxis)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<int>.Success(MoveBreakableV1(target, intakeSpeed, intakeForce, maxDistance, accuracy, rotationForce,
                maxRotationSpeed, useRotation, smoothHandoff, planarTolerance, boxExtents, lockXAxis,
                lockYAxis, lockZAxis));
        }

        private int MoveBreakableV1(Transform target, float intakeSpeed, float intakeForce, float maxDistance,
            float accuracy, float rotationForce, float maxRotationSpeed, bool useRotation, bool smoothHandoff,
            bool planarTolerance, Vector3 boxExtents, bool lockXAxis, bool lockYAxis, bool lockZAxis)
        {
            if (!_gamePieceWorld) return 0;
            _moving = true;
            int result = PieceController.MoveToBreakable(
                GamePiece, target, intakeSpeed, intakeForce, maxDistance, accuracy,
                rotationForce, maxRotationSpeed, useRotation, planarTolerance, boxExtents,
                lockXAxis, lockYAxis, lockZAxis, out var distance, out var distanceMagnitude);

            Distance = distance;
            DistanceMagnitude = distanceMagnitude;

            GamePiece.owner = Mathf.Approximately(result, 1) ? target.gameObject : GamePiece.owner;
            if (result == 1)
            {
                _moving = smoothHandoff;
                Controlled(smoothHandoff);
            }

            return result;
        }

        public MoSimResult<int> Move(Transform target, float moveSpeed, float angularSpeed, bool smoothHandoff)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<int>.Success(MoveV1(target, moveSpeed, angularSpeed, smoothHandoff));
        }

        private int MoveV1(Transform target, float moveSpeed, float angularSpeed, bool smoothHandoff)
        {
            if (!_gamePieceWorld) return 0;
            _moving = true;
            int result = PieceController.MoveTo(
                GamePiece, gamePieceData, target, moveSpeed, angularSpeed, out var distance, out var distanceMagnitude);

            Distance = distance;
            DistanceMagnitude = distanceMagnitude;

            if (result == 1)
            {
                _moving = smoothHandoff;
                SetParent(target);
            }
            return result;
        }

        /// <summary>Releases with force in the holding target's local axes, independent of piece symmetry.</summary>
        public MoSimResult<MoSimLib.Void> Release(Vector3 force, ForceMode forceMode = ForceMode.Impulse)
        {
            var version = MoSim.ActiveVersion;
            ReleaseV1(force, forceMode);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ReleaseV1(Vector3 force, ForceMode forceMode = ForceMode.Impulse)
        {
            if (!_gamePieceWorld) return;
            // Capture the holding frame before SetParent replaces the owner with the world.
            var worldForce = GamePiece.owner.transform.TransformDirection(force);
            UseRobot();
            var rb = Utils.FindParentObjectComponent<Rigidbody>(GamePiece.gameObject);
            SetParent(_gamePieceWorld);
            _colliderParent.parent = gameObject.transform;
            _colliderParent.localPosition = Vector3.zero + _startPosition;
            _colliderParent.localRotation = Quaternion.identity * _startRotation;
            GamePiece.rigidbody.velocity = Vector3.zero;
            GamePiece.rigidbody.angularVelocity = Vector3.zero;
            GamePiece.rigidbody.AddForce(worldForce, forceMode);
            if (rb && forceMode != ForceMode.VelocityChange) rb.AddForceAtPosition(-worldForce, GamePiece.transform.position, forceMode);
        }

        /// <summary>Starts force in the holding target's axes, then follows the piece's rotation during release.</summary>
        public MoSimResult<IEnumerator> ContinuedRelease(Vector3 force, float time, float maxSpeed, ForceMode forceMode = ForceMode.Impulse)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<IEnumerator>.Success(ContinuedReleaseV1(force, time, maxSpeed, forceMode));
        }

        private IEnumerator ContinuedReleaseV1(Vector3 force, float time, float maxSpeed, ForceMode forceMode = ForceMode.Impulse)
        {
            if (!_gamePieceWorld) yield break;
            // Cancel the initial symmetric orientation, while retaining a piece-local force after detachment.
            var localForce = transform.InverseTransformDirection(GamePiece.owner.transform.TransformDirection(force));
            UseRobot();
            SetParent(_gamePieceWorld);
            var startTime = Time.time;
            _colliderParent.parent = gameObject.transform;
            _colliderParent.localPosition = Vector3.zero + _startPosition;
            _colliderParent.localRotation = Quaternion.identity * _startRotation;
            GamePiece.rigidbody.velocity = Vector3.zero;
            GamePiece.rigidbody.angularVelocity = Vector3.zero;
            
            while (Time.time - startTime < time)
            {
                if (GamePiece.rigidbody.velocity.magnitude < maxSpeed)
                {
                    GamePiece.rigidbody.AddForce(transform.TransformDirection(localForce), forceMode);
                }
                yield return null;
                yield return new WaitForFixedUpdate();
            }
        }

        private void SetParent(Transform parent)
        {
            if (!_gamePieceWorld) return;
            GamePiece.owner = parent.gameObject;
            GamePiece.transform.parent = parent;
            _colliderParent.parent = parent;
        }

        private void Controlled(bool smoothHandoff = false)
        {
            if (!_gamePieceWorld) return;
            GamePiece.transform.parent = GamePiece.owner.transform;
            if (!_isControlled)
            {
                IgnoreRobot();
            }

            if (!smoothHandoff)
            {
                GamePiece.transform.rotation = PieceController.ResolveTargetRotation(
                    GamePiece.transform.rotation, GamePiece.owner.transform.rotation, gamePieceData);
                GamePiece.transform.localPosition = Vector3.zero;

                // The colliders are siblings of the carried piece; retain its symmetric orientation.
                var localRotation = GamePiece.transform.localRotation;
                _colliderParent.localPosition = localRotation * _startPosition;
                _colliderParent.localRotation = localRotation * _startRotation;
                GamePiece.rigidbody.rotation = GamePiece.transform.rotation;
                GamePiece.rigidbody.position = GamePiece.transform.position;
            }
            GamePiece.rigidbody.useGravity = false;
            GamePiece.rigidbody.velocity = Vector3.zero;
            GamePiece.rigidbody.angularVelocity = Vector3.zero;
        }

        private void IgnoreRobot()
        {
            var version = MoSim.ActiveVersion;
            if (version > new MoSimVersion(25, 3, 2))
            {
                IgnoreRobotV2();
                return;
            }

            IgnoreRobotV1();
        }

        private void IgnoreRobotV1()
        {
            if (!_gamePieceWorld) return;
            GamePiece.rigidbody.isKinematic = false;
            GamePiece.rigidbody.automaticInertiaTensor = false;
            GamePiece.rigidbody.inertiaTensor = Vector3.zero;
            GamePiece.rigidbody.automaticCenterOfMass = false;
            GamePiece.rigidbody.detectCollisions = false;
            GamePiece.rigidbody.centerOfMass = transform.InverseTransformPoint(GamePiece.owner.transform.parent.position);
            GamePiece.rigidbody.interpolation = RigidbodyInterpolation.None;
            GamePiece.rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            
            var parent = Utils.FindParentObjectComponent<Rigidbody>(gameObject);
            parent.excludeLayers = (1 << gameObject.layer) | (1 << LayerMask.NameToLayer("Robot"));
            GamePiece.rigidbody.excludeLayers = LayerMask.GetMask("Robot");
            _colliderParent.parent = transform.parent.transform;
            
            for (var i = 0; i < _colliders.Length; i++)
            {
                _colliders[i].excludeLayers = LayerMask.GetMask("Robot");
            }

            _isControlled = true;
        }

        private void UseRobot()
        {
            var version = MoSim.ActiveVersion;
            if (version > new MoSimVersion(25, 3, 2))
            {
                UseRobotV2();
                return;
            }

            UseRobotV1();
        }

        private void UseRobotV1()
        {
            if (!_gamePieceWorld) return;
            Utils.FindParentObjectComponent<Rigidbody>(gameObject).excludeLayers = LayerMask.GetMask("Robot");

            GamePiece.rigidbody.automaticInertiaTensor = false;
            GamePiece.rigidbody.inertiaTensor = new Vector3(0.01f, 0.01f, 0f);
            GamePiece.rigidbody.inertiaTensorRotation = Quaternion.identity;
            GamePiece.rigidbody.ResetInertiaTensor();

            GamePiece.rigidbody.automaticCenterOfMass = true;
            GamePiece.rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            GamePiece.rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            GamePiece.rigidbody.isKinematic = false;
            GamePiece.rigidbody.useGravity = true;
            GamePiece.rigidbody.angularDrag = _startingAngularDrag;
            GamePiece.rigidbody.mass = _startingMass;
            GamePiece.rigidbody.drag = _startingDrag;
            GamePiece.rigidbody.excludeLayers = LayerMask.GetMask();
            GamePiece.rigidbody.detectCollisions = true;
            _colliderParent.parent = GamePiece.transform;
            _colliderParent.position = GamePiece.transform.position;
            _colliderParent.rotation = GamePiece.transform.rotation;
            
            for (var i = 0; i < _colliders.Length; i++)
            {
                _colliders[i].excludeLayers = LayerMask.GetMask();
            }

            _isControlled = false;
        }

        private void IgnoreRobotV2()
        {
            if (!_gamePieceWorld) return;

            GamePiece.rigidbody.isKinematic = false;
            GamePiece.rigidbody.automaticInertiaTensor = false;
            GamePiece.rigidbody.inertiaTensor = Vector3.zero;
            GamePiece.rigidbody.automaticCenterOfMass = false;
            // Keep the held piece Rigidbody non-colliding as in V1. Its colliders are
            // reparented to the robot while held; enabling this body changes its dynamics.
            GamePiece.rigidbody.detectCollisions = false;
            GamePiece.rigidbody.centerOfMass = transform.InverseTransformPoint(GamePiece.owner.transform.parent.position);
            GamePiece.rigidbody.interpolation = RigidbodyInterpolation.None;
            GamePiece.rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            GamePiece.rigidbody.excludeLayers = LayerMask.GetMask();
            _colliderParent.parent = transform.parent.transform;

            for (var i = 0; i < _colliders.Length; i++)
            {
                _colliders[i].excludeLayers = LayerMask.GetMask();
            }

            _ignoredRobot = GamePiece.owner.GetComponentInParent<RobotBase>();
            if (_ignoredRobot != null)
            {
                SetRobotCollisionsIgnored(true);
            }

            _isControlled = true;
        }

        private void UseRobotV2()
        {
            if (!_gamePieceWorld) return;

            SetRobotCollisionsIgnored(false);
            _ignoredRobot = null;

            GamePiece.rigidbody.automaticInertiaTensor = false;
            GamePiece.rigidbody.inertiaTensor = new Vector3(0.01f, 0.01f, 0f);
            GamePiece.rigidbody.inertiaTensorRotation = Quaternion.identity;
            GamePiece.rigidbody.ResetInertiaTensor();

            GamePiece.rigidbody.automaticCenterOfMass = true;
            GamePiece.rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            GamePiece.rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
            GamePiece.rigidbody.isKinematic = false;
            GamePiece.rigidbody.useGravity = true;
            GamePiece.rigidbody.angularDrag = _startingAngularDrag;
            GamePiece.rigidbody.mass = _startingMass;
            GamePiece.rigidbody.drag = _startingDrag;
            GamePiece.rigidbody.excludeLayers = LayerMask.GetMask();
            GamePiece.rigidbody.detectCollisions = true;
            _colliderParent.parent = GamePiece.transform;
            _colliderParent.position = GamePiece.transform.position;
            _colliderParent.rotation = GamePiece.transform.rotation;

            for (var i = 0; i < _colliders.Length; i++)
            {
                _colliders[i].excludeLayers = LayerMask.GetMask();
            }

            _isControlled = false;
        }

        private void SetRobotCollisionsIgnored(bool ignored)
        {
            if (!ignored)
            {
                // Restore the pickup snapshot even if the robot or its exclusions changed.
                Utils.IgnoreCollisionsBetween(_colliders, _ignoredRobotColliders, false);
                _ignoredRobotColliders = Array.Empty<Collider>();
                return;
            }

            if (_ignoredRobot?.Rigidbodies == null) return;

            // While held, the collider container is a sibling of the piece's Rigidbody.
            // Use the cached colliders directly rather than asking the Rigidbody for children.
            var robotColliders = new List<Collider>();
            var exclusions = _ignoredRobot.GamePieceCollisionIgnoreExclusions;
            foreach (var robotRigidbody in _ignoredRobot.Rigidbodies)
            {
                if (robotRigidbody == null ||
                    (exclusions != null && Array.IndexOf(exclusions, robotRigidbody) >= 0)) continue;

                foreach (var collider in robotRigidbody.GetComponentsInChildren<Collider>())
                {
                    // A parent's search also finds nested rigidbodies and the held piece.
                    // Only include this body's own colliders, excluding the piece itself.
                    if (collider.attachedRigidbody == robotRigidbody &&
                        Array.IndexOf(_colliders, collider) < 0)
                    {
                        robotColliders.Add(collider);
                    }
                }
            }

            _ignoredRobotColliders = robotColliders.ToArray();
            Utils.IgnoreCollisionsBetween(_colliders, _ignoredRobotColliders);
        }
    }
}
