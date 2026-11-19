using System;
using System.Collections.Generic;
using System.Linq;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;

namespace RobotFramework.Components
{
    [RequireComponent(typeof(GenericJoint))]
    public class GenericTurret : MonoBehaviour
    {
        private const float c_MinTargetDistanceSqr = 0.0001f;

        // Largest unwind step. Must stay under 180 or the joint's shortest-path solve cuts back.
        private const float c_MaxUnwindStep = 150f;

        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetPosition;
        [SerializeField] private bool useTargetPosition;
        [SerializeField] private Transform referencePoint;

        [Header("Joint")]
        [SerializeField] private JointAxis turretAxis = JointAxis.Y;
        [SerializeField] private float angleOffset;
        [SerializeField] private bool flipDirection;
        [SerializeField] private bool useStartingOffset;
        [SerializeField] private WrappingSettings wrappingSettings;

        [Serializable]
        private struct WrappingSettings
        {
            [SerializeField] public bool useNoWrap;
            [SerializeField] public float noWrapMin;
            [SerializeField] public float noWrapMax;
        }

        private float noWrapAngle;

        private float _continuousAngle;
        private float _lastRawAngle;

        private bool isUnwinding;
        private float lastSteerValue;

        private float MinTravel => Mathf.Min(wrappingSettings.noWrapMin, wrappingSettings.noWrapMax);
        private float MaxTravel => Mathf.Max(wrappingSettings.noWrapMin, wrappingSettings.noWrapMax);

        [Header("Behavior")]
        [SerializeField] private bool trackContinuously = true;

        [SerializeField] private float steerFeedFowardConstant = 0;

        private GenericJoint genericJoint;
        private RobotBase robotBase;
        private PidConstants pidConstants;
        private Dictionary<DefaultMap, Transform> defaultMap;
        private Dictionary<PassMap, Transform> passMap;

        [Header("Debug")]
        [SerializeField] private float currentAngle;

        public MoSimResult<float> CurrentAngle()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(CurrentAngleV1());
        }

        private float CurrentAngleV1() => _continuousAngle;

        private void Awake()
        {
            TryGetComponent(out genericJoint);
            robotBase = GetComponentInParent<RobotBase>();
        }

        private void Start()
        {
            if (genericJoint == null)
            {
                Debug.LogWarning($"{nameof(GenericTurret)} on {name} requires a {nameof(GenericJoint)}.", this);
                enabled = false;
                return;
            }

            ConfigureJoint();

            _lastRawAngle = genericJoint.GetAngle();
            _continuousAngle = 0;

            if (robotBase != null)
            {
                Initialize(robotBase);
            }
        }

        private void LateUpdate()
        {
            if (pidConstants != null)
            {
                genericJoint.UpdatePid(pidConstants);
            }
        }

        public MoSimResult<MoSimLib.Void> SetPid(PidConstants _pidConstants)
        {
            var version = MoSim.ActiveVersion;

            SetPidV1(_pidConstants);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPidV1(PidConstants _pidConstants)
        {
            if (genericJoint == null && !TryGetComponent(out genericJoint))
            {
                return;
            }

            if (_pidConstants == null)
            {
                return;
            }

            pidConstants = _pidConstants;
            genericJoint.SetPid(pidConstants);
            ConfigureJoint();
        }

        public MoSimResult<MoSimLib.Void> UpdatePid(PidConstants _pidConstants)
        {
            var version = MoSim.ActiveVersion;

            UpdatePidV1(_pidConstants);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdatePidV1(PidConstants _pidConstants)
        {
            if (genericJoint == null && !TryGetComponent(out genericJoint))
            {
                return;
            }

            if (_pidConstants == null)
            {
                return;
            }

            pidConstants = _pidConstants;
            genericJoint.UpdatePid(pidConstants);
        }

        public MoSimResult<MoSimLib.Void> Initialize(RobotBase _robotBase)
        {
            var version = MoSim.ActiveVersion;

            InitializeV1(_robotBase);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void InitializeV1(RobotBase _robotBase)
        {
            robotBase = _robotBase;
            InitializeDefaultMap();
        }

        public MoSimResult<MoSimLib.Void> SetTrackContinuously(bool _trackContinuously)
        {
            var version = MoSim.ActiveVersion;

            SetTrackContinuouslyV1(_trackContinuously);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTrackContinuouslyV1(bool _trackContinuously)
        {
            trackContinuously = _trackContinuously;
        }

        private void FixedUpdate()
        {
            // Ahead of Align so the travel limit plans against this step's measured angle. Robots that
            // drive the turret themselves have their own FixedUpdate, and with no execution order set
            // it may land either side of this one.
            float rawAngle = genericJoint.GetAngle();

            float delta = Mathf.DeltaAngle(_lastRawAngle, rawAngle);

            _continuousAngle += delta;
            _lastRawAngle = rawAngle;

            currentAngle = _continuousAngle;

            if (trackContinuously)
            {
                // Whichever Align call lands last owns the joint for this step, so the auto-align has to
                // carry the caller's steer rather than overwrite it with zero.
                Align(lastSteerValue);
            }
        }

        public MoSimResult<MoSimLib.Void> SetTarget(Transform _target)
        {
            var version = MoSim.ActiveVersion;

            SetTargetV1(_target);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTargetV1(Transform _target)
        {
            target = _target;
            useTargetPosition = false;
        }

        public MoSimResult<MoSimLib.Void> SetTarget(Vector3 _targetPosition)
        {
            var version = MoSim.ActiveVersion;

            SetTargetV1(_targetPosition);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTargetV1(Vector3 _targetPosition)
        {
            targetPosition = _targetPosition;
            useTargetPosition = true;
        }

        public MoSimResult<MoSimLib.Void> ClearTarget()
        {
            var version = MoSim.ActiveVersion;

            ClearTargetV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ClearTargetV1()
        {
            target = null;
            useTargetPosition = false;
        }

        public MoSimResult<MoSimLib.Void> SetDefaultMap(DefaultMap _map)
        {
            var version = MoSim.ActiveVersion;

            SetDefaultMapV1(_map);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetDefaultMapV1(DefaultMap _map)
        {
            if (defaultMap == null)
            {
                InitializeDefaultMap();
            }

            if (defaultMap != null && defaultMap.TryGetValue(_map, out Transform mapTarget))
            {
                SetTarget(mapTarget);
            }
        }

        public MoSimResult<Vector3> GetDefaultMapPosition(DefaultMap _map)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<Vector3>.Success(GetDefaultMapPositionV1(_map));
        }

        private Vector3 GetDefaultMapPositionV1(DefaultMap _map)
        {
            if (defaultMap == null)
            {
                InitializeDefaultMap();
            }

            return defaultMap != null && defaultMap.TryGetValue(_map, out Transform mapTarget)
                ? mapTarget.position
                : Vector3.zero;
        }

        public MoSimResult<MoSimLib.Void> SetPassMap(PassMap _map)
        {
            var version = MoSim.ActiveVersion;

            SetPassMapV1(_map);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPassMapV1(PassMap _map)
        {
            if (passMap == null)
            {
                InitializeDefaultMap();
            }

            if (passMap != null && passMap.TryGetValue(_map, out Transform mapTarget))
            {
                SetTarget(mapTarget);
            }
        }

        public MoSimResult<MoSimLib.Void> SetPassMapClosest()
        {
            var version = MoSim.ActiveVersion;

            SetPassMapClosestV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPassMapClosestV1()
        {
            if (passMap == null)
            {
                InitializeDefaultMap();
            }

            if (passMap == null || passMap.Count == 0)
            {
                return;
            }

            float minValue = Mathf.Infinity;
            Transform closest = null;
            Transform distanceReference = robotBase != null ? robotBase.transform : transform;

            foreach (Transform pose in passMap.Values)
            {
                float mag = (pose.position - distanceReference.position).sqrMagnitude;

                if (mag < minValue)
                {
                    minValue = mag;
                    closest = pose;
                }
            }

            if (closest != null)
            {
                SetTarget(closest);
            }
        }

        public MoSimResult<MoSimLib.Void> SetCustomTarget(Vector3 _targetPosition)
        {
            var version = MoSim.ActiveVersion;

            SetCustomTargetV1(_targetPosition);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetCustomTargetV1(Vector3 _targetPosition)
        {
            SetTarget(_targetPosition);
        }

        public MoSimResult<MoSimLib.Void> Align(float steerValue = 0)
        {
            var version = MoSim.ActiveVersion;

            AlignV1(steerValue);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void AlignV1(float steerValue = 0)

        {
            if (!useTargetPosition && target == null)
            {
                return;
            }

            AlignToTarget(GetActiveTargetPosition(), steerValue);
        }

        public MoSimResult<MoSimLib.Void> AlignToTarget(Vector3 _targetPosition, float steerValue)
        {
            var version = MoSim.ActiveVersion;

            AlignToTargetV1(_targetPosition, steerValue);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void AlignToTargetV1(Vector3 _targetPosition, float steerValue)
        {
            if (pidConstants == null)
            {
                return;
            }

            Vector3 localDirection = GetPlanarLocalDirection(_targetPosition);

            if (localDirection.sqrMagnitude <= c_MinTargetDistanceSqr)
            {
                return;
            }

            lastSteerValue = steerValue;

            ConfigureJoint();

            float targetAngle = ApplyTravelLimit(GetAxisAngleFromPlanarDirection(localDirection) + angleOffset);

            genericJoint.SetTargetAngle(targetAngle).withFixedDTiming()
                .withFeedFoward(LimitFeedForward(steerValue * steerFeedFowardConstant));
        }

        /// <summary>
        /// Resolves an azimuth to the nearest winding of it that lies inside noWrapMin/noWrapMax, and
        /// returns the next step toward it.
        /// </summary>
        /// <remarks>
        /// Everything here is in <c>_continuousAngle</c>, the only angle in the system that carries a
        /// winding count, so envelopes wider than a revolution are expressible: an azimuth the turret
        /// can reach at more than one winding resolves to whichever is nearer, and one that is out of
        /// reach rides the nearer stop. The joint itself only ever sees a step under 180, so its
        /// shortest-path solve is always the direction we picked — it needs no cut angle of its own.
        /// </remarks>
        private float ApplyTravelLimit(float targetAngle)
        {
            if (!wrappingSettings.useNoWrap)
            {
                isUnwinding = false;
                return targetAngle;
            }

            float current = genericJoint.GetAngle();

            // Winding of this azimuth nearest to where we are now — the one the joint would pick alone.
            float nearest = _continuousAngle + Mathf.DeltaAngle(current, targetAngle);

            // Windings of that same azimuth that fall inside the envelope. The range is empty only when
            // the azimuth is unreachable, which an envelope narrower than a revolution allows.
            int lowestTurn = Mathf.CeilToInt((MinTravel - nearest) / 360f);
            int highestTurn = Mathf.FloorToInt((MaxTravel - nearest) / 360f);

            float goal = lowestTurn > highestTurn
                ? Mathf.Clamp(nearest, MinTravel, MaxTravel)
                : nearest + 360f * Mathf.Clamp(0, lowestTurn, highestTurn);

            float remaining = goal - _continuousAngle;

            // Past the step limit the turret owes more than a half turn, so it is unwinding rather than
            // tracking: the azimuth is close by but the winding it has to reach is not.
            isUnwinding = Mathf.Abs(remaining) > c_MaxUnwindStep;

            float step = isUnwinding ? Mathf.Sign(remaining) * c_MaxUnwindStep : remaining;

            // Normalised so the joint sees the same -180..180 domain the direct path hands it.
            return Mathf.DeltaAngle(0f, current + step);
        }

        /// <summary>
        /// Blocks the steer feed-forward from driving travel further out of the envelope.
        /// </summary>
        /// <remarks>
        /// The feed-forward is summed onto the joint's commanded velocity downstream of the PID and is
        /// not subject to its output clamp, so at a stop — where the PID has settled and has no error
        /// left to push back with — it would walk the turret out of bounds unopposed. Everywhere else it
        /// is kept, including mid-unwind: there the PID is saturated the other way and outweighs it, and
        /// the limiter replans from the measured angle every step regardless, so the feed-forward can
        /// only slow an unwind, never defeat it.
        /// </remarks>
        private float LimitFeedForward(float feedForward)
        {
            if (!wrappingSettings.useNoWrap)
            {
                return feedForward;
            }

            if (feedForward > 0f && _continuousAngle >= MaxTravel)
            {
                return 0f;
            }

            return feedForward < 0f && _continuousAngle <= MinTravel ? 0f : feedForward;
        }

        public MoSimResult<float> GetTargetAngle(Vector3 _targetPosition)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetTargetAngleV1(_targetPosition));
        }

        private float GetTargetAngleV1(Vector3 _targetPosition)
        {
            Vector3 localDirection = GetPlanarLocalDirection(_targetPosition);

            if (localDirection.sqrMagnitude <= c_MinTargetDistanceSqr)
            {
                return 0f;
            }

            return GetAxisAngleFromPlanarDirection(localDirection) + angleOffset;
        }

        public MoSimResult<Vector3> GetCurrentTarget()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<Vector3>.Success(GetCurrentTargetV1());
        }

        private Vector3 GetCurrentTargetV1()
        {
            if (!useTargetPosition && target == null)
            {
                return Vector3.zero;
            }

            return GetActiveTargetPosition();
        }

        /// <summary>
        /// The transform this turret measures target direction from. Aim solvers that hand the turret
        /// a pre-rotated virtual target must build that point from this same origin — any offset
        /// between the two origins shows up in the resulting angle as a range-dependent error.
        /// </summary>
        public Transform GetReferencePoint()
        {
            return referencePoint != null ? referencePoint : transform;
        }

        public MoSimResult<float> GetError()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetErrorV1());
        }

        private float GetErrorV1()
        {
            if (!useTargetPosition && target == null)
            {
                return 0f;
            }

            float targetAngle = GetJointTargetAngle(GetTargetAngle(GetActiveTargetPosition()));
            return Mathf.DeltaAngle(genericJoint.GetAngle(), targetAngle);
        }

        public MoSimResult<bool> IsWrapping()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<bool>.Success(IsWrappingV1());
        }

        private bool IsWrappingV1()
        {
            return isUnwinding || genericJoint.isWrapping;
        }

        private void ConfigureJoint()
        {
            if (genericJoint == null)
            {
                return;
            }

            genericJoint.angularAxis = turretAxis;
            genericJoint.flipDirection = flipDirection;
            genericJoint.useStartingOffset = useStartingOffset;

            // ApplyTravelLimit owns the limit now, so leave the joint's own crossing test off rather
            // than letting both steer the same axis.
            genericJoint.noWrap = false;
        }

        public MoSimResult<MoSimLib.Void> UseNoWrap(bool noWrap)
        {
            var version = MoSim.ActiveVersion;

            UseNoWrapV1(noWrap);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UseNoWrapV1(bool noWrap)
        {
            wrappingSettings.useNoWrap = noWrap;
        }

        public MoSimResult<MoSimLib.Void> SetNoWrapMin(float min)
        {
            var version = MoSim.ActiveVersion;

            SetNoWrapMinV1(min);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetNoWrapMinV1(float min)
        {
            wrappingSettings.noWrapMin = min;
        }

        public MoSimResult<MoSimLib.Void> SetNoWrapMax(float max)
        {
            var version = MoSim.ActiveVersion;

            SetNoWrapMaxV1(max);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetNoWrapMaxV1(float max)
        {
            wrappingSettings.noWrapMax = max;
        }

        public MoSimResult<MoSimLib.Void> SetNoWrap(float min, float max)
        {
            var version = MoSim.ActiveVersion;

            SetNoWrapV1(min, max);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetNoWrapV1(float min, float max)
        {
            SetNoWrapMin(min);
            SetNoWrapMax(max);
        }

        private Vector3 GetActiveTargetPosition()
        {
            return useTargetPosition ? targetPosition : target.position;
        }

        private Vector3 GetLocalDirection(Vector3 _targetPosition)
        {
            Transform reference = referencePoint != null ? referencePoint : transform;
            Vector3 worldDirection = _targetPosition - reference.position;

            return transform.parent != null ? transform.parent.InverseTransformDirection(worldDirection) : worldDirection;
        }

        private Vector3 GetPlanarLocalDirection(Vector3 _targetPosition)
        {
            Vector3 localDirection = GetLocalDirection(_targetPosition);

            return turretAxis switch
            {
                JointAxis.X => new Vector3(0f, localDirection.y, localDirection.z),
                JointAxis.Y => new Vector3(localDirection.x, 0f, localDirection.z),
                JointAxis.Z => new Vector3(localDirection.x, localDirection.y, 0f),
                _ => localDirection
            };
        }

        private float GetAxisAngleFromPlanarDirection(Vector3 _planarLocalDirection)
        {
            return GetAxisAngle(_planarLocalDirection.normalized);
        }

        private float GetJointTargetAngle(float _targetAngle)
        {
            return flipDirection ? Utils.FlipAngle(_targetAngle) : _targetAngle;
        }

        private void InitializeDefaultMap()
        {
            defaultMap = new Dictionary<DefaultMap, Transform>();

            var foundTargets = Utils.FindGameObjectsOnLayer("AutoAngleNodes");

            if (robotBase != null)
            {
                foreach (var foundTarget in foundTargets.ToList()
                             .Where(foundTarget =>
                                 foundTarget.name.Contains("blue", System.StringComparison.OrdinalIgnoreCase) &&
                                 robotBase.Alliance == Alliance.Red ||
                                 foundTarget.name.Contains("red", System.StringComparison.OrdinalIgnoreCase) &&
                                 robotBase.Alliance == Alliance.Blue))
                {
                    foundTargets.Remove(foundTarget);
                }
            }

            foreach (var foundTarget in foundTargets)
            {
                var map = Utils.GetStringToEnum<DefaultMap>(foundTarget.name);

                if (!defaultMap.ContainsKey(map))
                {
                    defaultMap.Add(map, foundTarget.transform);
                }
            }

            passMap = new Dictionary<PassMap, Transform>();

            foreach (var mapTarget in defaultMap.Where(mapTarget => mapTarget.Key != DefaultMap.Hub))
            {
                var pass = Utils.GetStringToEnum<PassMap>(mapTarget.Key.ToString());

                if (!passMap.ContainsKey(pass))
                {
                    passMap.Add(pass, mapTarget.Value);
                }
            }
        }

        private float GetAxisAngle(Vector3 _localDirection)
        {
            return turretAxis switch
            {
                JointAxis.X => Mathf.Atan2(_localDirection.y, _localDirection.z) * Mathf.Rad2Deg,
                JointAxis.Y => Mathf.Atan2(_localDirection.x, _localDirection.z) * Mathf.Rad2Deg,
                JointAxis.Z => Mathf.Atan2(_localDirection.y, _localDirection.x) * Mathf.Rad2Deg,
                _ => 0f
            };
        }

        public enum DefaultMap
        {
            Hub,
            LeftPass,
            RightPass
        }

        public enum PassMap
        {
            LeftPass,
            RightPass
        }
    }
}
