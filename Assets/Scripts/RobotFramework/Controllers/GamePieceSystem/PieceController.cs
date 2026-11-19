using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public static class PieceController
    {
        private static readonly Dictionary<int, MovementState> MovementStates = new Dictionary<int, MovementState>();

        private class MovementState
        {
            public bool Moving;
            public Vector3 StartPosition;
        }

        private static MovementState GetState(int instanceId)
        {
            if (!MovementStates.ContainsKey(instanceId))
            {
                MovementStates[instanceId] = new MovementState
                {
                    Moving = false,
                    StartPosition = Vector3.zero
                };
            }
            return MovementStates[instanceId];
        }

        public static MoSimResult<MoSimLib.Void> ClearState(int instanceId)
        {
            var version = MoSim.ActiveVersion;
            ClearStateV1(instanceId);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private static void ClearStateV1(int instanceId)
        {
            MovementStates.Remove(instanceId);
        }

        /// <summary>
        /// Returns 0 if still in transit, 1 if target reached, -1 if broke.
        /// </summary>
        public static MoSimResult<int> MoveToBreakable<TData>(
            GamePiece<TData> gamePiece,
            Transform target,
            float intakeSpeed,
            float intakeForce,
            float maxDistance,
            float accuracy,
            float rotationForce,
            float maxRotationSpeed,
            bool useRotation,
            bool planarTolerance,
            Vector3 boxExtents,
            bool lockXAxis,
            bool lockYAxis,
            bool lockZAxis,
            out Vector3 distance,
            out float distanceMagnitude) where TData : IGamePieceData
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<int>.Success(MoveToBreakableV1(gamePiece, target, intakeSpeed, intakeForce, maxDistance, accuracy,
                rotationForce, maxRotationSpeed, useRotation, planarTolerance, boxExtents, lockXAxis,
                lockYAxis, lockZAxis, out distance, out distanceMagnitude));
        }

        private static int MoveToBreakableV1<TData>(
            GamePiece<TData> gamePiece,
            Transform target,
            float intakeSpeed,
            float intakeForce,
            float maxDistance,
            float accuracy,
            float rotationForce,
            float maxRotationSpeed,
            bool useRotation,
            bool planarTolerance,
            Vector3 boxExtents,
            bool lockXAxis,
            bool lockYAxis,
            bool lockZAxis,
            out Vector3 distance,
            out float distanceMagnitude) where TData : IGamePieceData
        {
            intakeSpeed *= 0.0254f;
            intakeForce *= 0.0254f;
            if (!planarTolerance)
            {
                maxDistance *= 0.0254f;
            }
            accuracy *= 0.0254f;
            boxExtents *= 0.0254f;

            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                distance = Difference(target.position, gamePiece.transform.position);
                distanceMagnitude = distance.magnitude;

                if (distance.magnitude <= accuracy)
                    return 1;
                if (distance.magnitude < maxDistance)
                    return 0;
                return -1;
            }

            var currentPose = gamePiece.transform.position;
            distance = Difference(target.position, currentPose);
            var rotation = gamePiece.transform.rotation;
            var targetRotation = ResolveTargetRotation(rotation, target.rotation, gamePiece.GamePieceData);
            var rotationError = Quaternion.Angle(rotation, targetRotation);
            var rotationVelocity = Quaternion.Euler(gamePiece.rigidbody.angularVelocity);
            var currentVelocity = gamePiece.rigidbody.velocity;
            var velocity = currentVelocity;
            var time = distance.magnitude / intakeSpeed;

            Vector3.SmoothDamp(gamePiece.transform.position, target.position, ref velocity, time, intakeSpeed, Time.fixedDeltaTime);
            var acceleration = (velocity - currentVelocity) / Time.fixedDeltaTime * intakeForce;

            // Apply axis locking
            if (lockXAxis || lockYAxis || lockZAxis)
            {
                var localAcceleration = target.InverseTransformDirection(acceleration);
                if (lockXAxis) localAcceleration.x = 0f;
                if (lockYAxis) localAcceleration.y = 0f;
                if (lockZAxis) localAcceleration.z = 0f;
                acceleration = target.TransformDirection(localAcceleration);
            }

            var forceMode = ForceMode.Acceleration;
            gamePiece.rigidbody.AddForce(acceleration, forceMode);
            var rb = Utils.FindParentObjectComponent<Rigidbody>(target.gameObject);
            if (rb) rb.AddForceAtPosition(-acceleration, gamePiece.transform.position, forceMode);
            distanceMagnitude = distance.magnitude;

            // Handle rotation
            if (rotationError > 0.1f && useRotation)
            {
                var shortestTargetRotation = targetRotation;

                if (Quaternion.Dot(rotation, shortestTargetRotation) < 0)
                {
                    shortestTargetRotation = new Quaternion(
                        -shortestTargetRotation.x,
                        -shortestTargetRotation.y,
                        -shortestTargetRotation.z,
                        -shortestTargetRotation.w
                    );
                }

                var smoothedRotation = Utils.SmoothDamp(rotation, shortestTargetRotation, ref rotationVelocity, time);
                var deltaRotation = smoothedRotation * Quaternion.Inverse(rotation);
                var maxDeltaAV = maxRotationSpeed - gamePiece.rigidbody.angularVelocity.magnitude;
                var angularVelocityDelta = QuaternionToAngularVelocity(deltaRotation, Time.fixedDeltaTime, Mathf.Clamp(maxDeltaAV, 0, maxRotationSpeed));
                var angularAcceleration = angularVelocityDelta * rotationForce;

                gamePiece.rigidbody.AddTorque(angularAcceleration, ForceMode.Force);
                if (rb) ApplyTorqueAtPosition(rb, -angularAcceleration, gamePiece.transform.position);
            }

            // Check completion
            if (planarTolerance)
            {
                var worldDistance = target.position - gamePiece.transform.position;
                var localDistance = Quaternion.Inverse(target.transform.rotation) * worldDistance;

                if (Mathf.Abs(localDistance.x) <= boxExtents.x / 2 &&
                    Mathf.Abs(localDistance.y) <= boxExtents.y / 2 &&
                    Mathf.Abs(localDistance.z) <= boxExtents.z / 2)
                {
                    return 1;
                }

                var scaledBoxSize = boxExtents * maxDistance;
                if (Mathf.Abs(localDistance.x) < scaledBoxSize.x &&
                    Mathf.Abs(localDistance.y) < scaledBoxSize.y &&
                    Mathf.Abs(localDistance.z) < scaledBoxSize.z)
                {
                    return 0;
                }

                return -1;
            }

            if (distance.magnitude <= accuracy)
                return 1;
            else if (distance.magnitude < maxDistance)
                return 0;
            else
                return -1;
        }

        public static MoSimResult<int> MoveTo<TData>(
            GamePiece<TData> gamePiece,
            TData gamePieceData,
            Transform target,
            float speed,
            float angularSpeed,
            out Vector3 distance,
            out float distanceMagnitude) where TData : IGamePieceData
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<int>.Success(MoveToV1(gamePiece, gamePieceData, target, speed, angularSpeed, out distance,
                out distanceMagnitude));
        }

        /// <summary>
        /// Applies a pure torque at a world-space position. Unity exposes no AddTorqueAtPosition;
        /// two equal and opposite forces form an equivalent force couple with no net force.
        /// </summary>
        private static void ApplyTorqueAtPosition(Rigidbody rigidbody, Vector3 torque, Vector3 position)
        {
            if (torque.sqrMagnitude <= Mathf.Epsilon) return;

            const float leverLength = 0.1f;
            var referenceAxis = Mathf.Abs(Vector3.Dot(torque.normalized, Vector3.up)) < 0.99f
                ? Vector3.up
                : Vector3.right;
            var lever = Vector3.Cross(torque, referenceAxis).normalized * leverLength;
            var force = Vector3.Cross(torque, lever) / (2f * lever.sqrMagnitude);

            rigidbody.AddForceAtPosition(force, position + lever, ForceMode.Force);
            rigidbody.AddForceAtPosition(-force, position - lever, ForceMode.Force);
        }

        private static int MoveToV1<TData>(
            GamePiece<TData> gamePiece,
            TData gamePieceData,
            Transform target,
            float speed,
            float angularSpeed,
            out Vector3 distance,
            out float distanceMagnitude) where TData : IGamePieceData
        {
            speed *= 0.0254f;
            var state = GetState(gamePiece.gameObject.GetInstanceID());

            if (speed == 0)
            {
                gamePiece.transform.rotation = ResolveTargetRotation(
                    gamePiece.transform.rotation, target.rotation, gamePieceData);
                gamePiece.rigidbody.rotation = gamePiece.transform.rotation;
                state.Moving = false;
                distance = Vector3.zero;
                distanceMagnitude = 0;
                return 1;
            }

            if (!state.Moving)
            {
                state.Moving = true;
                state.StartPosition = gamePiece.transform.localPosition;
            }

            distance = gamePiece.transform.parent.InverseTransformPoint(target.position) - state.StartPosition;
            var parentPosition = gamePiece.transform.parent.position;
            var maxStep = speed * Time.fixedDeltaTime;
            distanceMagnitude = distance.magnitude;
            var step = distance.normalized * Mathf.Min(maxStep, distanceMagnitude);
            var finalPosition = state.StartPosition + step;

            state.StartPosition = finalPosition;
            gamePiece.transform.position = parentPosition + gamePiece.transform.parent.TransformDirection(finalPosition);
            gamePiece.rigidbody.position = parentPosition + gamePiece.transform.parent.TransformDirection(finalPosition);
            gamePiece.rigidbody.velocity = Vector3.zero;

            distanceMagnitude = distance.magnitude;

            // Rotation with symmetry support
            var shortestTargetRotation = ResolveTargetRotation(
                gamePiece.transform.rotation, target.rotation, gamePieceData);

            gamePiece.transform.rotation = Quaternion.RotateTowards(
                gamePiece.transform.rotation,
                shortestTargetRotation,
                angularSpeed * Time.fixedDeltaTime
            );

            if (angularSpeed == 0)
            {
                gamePiece.transform.rotation = shortestTargetRotation;
            }
            gamePiece.rigidbody.rotation = gamePiece.transform.rotation;

            if (distanceMagnitude <= 0.25f * 0.0254f)
            {
                state.Moving = false;
                return 1;
            }

            return 0;
        }

        internal static Quaternion ResolveTargetRotation(Quaternion current, Quaternion target, IGamePieceData gamePieceData)
        {
            return gamePieceData.HasSymmetry
                ? FindShortestSymmetricRotation(current, target, gamePieceData.SymmetryType)
                : target;
        }

        public static MoSimResult<Quaternion> FindShortestSymmetricRotation(Quaternion current, Quaternion target, SymmetryType symmetryType)
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<Quaternion>.Success(FindShortestSymmetricRotationV1(current, target, symmetryType));
        }

        private static Quaternion FindShortestSymmetricRotationV1(Quaternion current, Quaternion target, SymmetryType symmetryType)
        {
            if (symmetryType == SymmetryType.None)
            {
                return target;
            }

            var symmetricRotations = new List<Quaternion> { target };

            // Generate symmetric rotations based on type
            switch (symmetryType)
            {
                case SymmetryType.XAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 0f));
                    break;

                case SymmetryType.YAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 0f));
                    break;

                case SymmetryType.ZAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 0f, 180f));
                    break;

                case SymmetryType.XYAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 180f, 0f));
                    break;

                case SymmetryType.XZAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 0f, 180f));
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 180f));
                    break;

                case SymmetryType.YZAxis:
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 0f, 180f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 180f));
                    break;

                case SymmetryType.XYZAxis:
                    // All combinations of 180° rotations on X, Y, Z
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 0f, 180f));
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 180f, 0f));
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 0f, 180f));
                    symmetricRotations.Add(target * Quaternion.Euler(0f, 180f, 180f));
                    symmetricRotations.Add(target * Quaternion.Euler(180f, 180f, 180f));
                    break;
            }

            // Find the rotation with the smallest angular distance
            var bestRotation = target;
            var smallestAngle = float.MaxValue;

            foreach (var symRotation in symmetricRotations)
            {
                var angle = Quaternion.Angle(current, symRotation);
                if (angle < smallestAngle)
                {
                    smallestAngle = angle;
                    bestRotation = symRotation;
                }
            }

            return bestRotation;
        }

        private static Vector3 QuaternionToAngularVelocity(Quaternion rotation, float deltaTime, float maxVelocity)
        {
            rotation.ToAngleAxis(out var angle, out var axis);

            if (Mathf.Abs(angle) > 0.0001f)
            {
                var angularVelocity = axis * (angle * Mathf.Deg2Rad) / deltaTime;

                if (angularVelocity.magnitude > maxVelocity)
                {
                    return angularVelocity.normalized * maxVelocity;
                }

                return angularVelocity;
            }
            return Vector3.zero;
        }

        private static Vector3 Difference(Vector3 a, Vector3 b)
        {
            return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }
    }
}
