using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace RobotFramework.Components
{
    /// <summary>
    /// Generic configurable joint controller using PID to achieve target positions/rotations.
    /// Supports angular and linear motion with audio feedback and brake mode behavior.
    /// Requires Rigidbody and ConfigurableJoint components.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(ConfigurableJoint))]
    public class GenericJoint : MonoBehaviour
    {
        private PIDLinear _pidLinear;
        private PIDAngle _pidAngle;
        private Velocity _velocityObject;

        [Header("Settings")]
        [Tooltip("Brake strength multiplier (0-1). Use 1 for normal, <1 for weak braking.")]
        [SerializeField]
        private float brakeModeMultiplyer = 1;

        [FormerlySerializedAs("ignoreBrakeMode")]
        [SerializeField]
        [Tooltip("If true, ignores game-wide disabled state for this joint.")]
        private bool ignoreDisabledMode = false;
        
        [SerializeField]
        [Tooltip("If true, locks the joint on game-wide disabled state.")]
        private bool lockJointOnDisable = false;

        private bool _useAudio = false;
        private AudioSource _movementAudioSource;

        [Header("Audio Settings")]
        [SerializeField]
        [Tooltip("Audio clip to play when joint is moving.")]
        private AudioClip movementAudioClip;

        [SerializeField]
        [Tooltip("Error threshold in degrees/inches below which audio stops.")]
        private float audioErrorThreshold = 3f;

        [SerializeField]
        [Tooltip("Error rate threshold for audio response.")]
        private float audioErrorDeltaThreshold = 25f;

        [SerializeField]
        [Tooltip("Min/max pitch range for movement audio (x=min, y=max).")]
        private Vector2 movementAudioPitchRange = new(0.95f, 1.1f);

        [SerializeField]
        [Tooltip("Min/max volume range for movement audio (x=min, y=max).")]
        private Vector2 movementAudioVolumeRange = new(0.1f, 0.25f);

        [SerializeField]
        [Tooltip("Smoothing speed for pitch transitions.")]
        private float audioPitchSmoothSpeed = 8f;

        [SerializeField]
        [Tooltip("Smoothing speed for volume transitions.")]
        private float audioVolumeSmoothSpeed = 8f;

        [SerializeField]
        [Tooltip("Exponent for audio response curve (higher = more aggressive).")]
        private float audioResponseExponent = 0.75f;

        [SerializeField]
        [Tooltip("Enable debug logging for audio system.")]
        private bool debugPrint = false;

        [HideInInspector] public int DebugErrorDeltaInt = 0;
        private float _previousError;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Current position/angle value (for debugging).")]
        private float CurrentValue;

        [SerializeField]
        [Tooltip("Target position/angle value (for debugging).")]
        private float TargetValue;

        [SerializeField]
        [Tooltip("PID output value (for debugging).")]
        private float OutputValue;
        private ConfigurableJoint _joint;
        private Rigidbody _rigidbody;
        private PIDController _pidController;
        private JointDrive _drive;

        private bool locked;
        private ConfigurableJointMotion[] _motionsBeforeDisable;
        private JointProjectionMode _projectionBeforeDisable;
        private Vector3 _anchorBeforeDisable;


        private float _lastTime;

        private Quaternion startingAngle;
        private float angleOffset = 0;
        private bool usingManualAngleOffset = false;

        //builder patern stuff
        [HideInInspector] public Vector3 _startingPosition;

        [HideInInspector] public JointAxis linearAxis;
        [HideInInspector] public bool useDifferentEncoderAxis = false;
        [HideInInspector] public JointAxis encoderAxis;
        [HideInInspector] public bool flipDirection;
        [HideInInspector] public bool useStartingOffset;

        [HideInInspector] public JointAxis angularAxis;

        [HideInInspector] public JointAxis velocityAxis;
        [HideInInspector] public bool noWrap;
        [HideInInspector] public float noWrapAngle;
        [HideInInspector] public bool isWrapping = false;

        private bool wasDisabled;
        private bool wasLocked;
        private Quaternion _lockReferenceRotation;

        private bool fixedDTiming;
        private float lastFFvalue;

        // Start is called before the first frame update

        void Start()
        {
            usingManualAngleOffset = false;

            _joint = gameObject.GetComponent<ConfigurableJoint>();

            _rigidbody = gameObject.GetComponent<Rigidbody>();

            var connectedRotation = _joint.connectedBody != null ? _joint.connectedBody.rotation : Quaternion.identity;
            _lockReferenceRotation = Quaternion.Inverse(connectedRotation) * _rigidbody.rotation;

            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rigidbody.interpolation = RigidbodyInterpolation.None;

            _lastTime = 0;

            startingAngle = transform.localRotation;
            _startingPosition = transform.localPosition;
            wasDisabled = false;

            if (movementAudioClip != null)
            {
                _useAudio = true;
                _movementAudioSource = gameObject.AddComponent<AudioSource>();
                _movementAudioSource.outputAudioMixerGroup = Resources.Load<AudioMixerGroup>("MainAudioMixer")
                    .audioMixer.FindMatchingGroups("RobotSounds")[0];
                _movementAudioSource.playOnAwake = false;
                _movementAudioSource.loop = true;
                _movementAudioSource.clip = movementAudioClip;
                _movementAudioSource.Stop();
            }

            wasLocked = false;
            fixedDTiming = false;
            locked = false;
        }

        /// <summary>
        /// Handles brake mode when robot is disabled, limiting joint forces to prevent sudden movements.
        /// </summary>
        private void Update()
        {
            UpdateDisabledLock();
            if (BaseGameManager.Instance is null || BaseGameManager.Instance.RobotState == RobotState.Disabled && !ignoreDisabledMode && !wasDisabled)
            {
                JointDrive drive;
                _joint.targetAngularVelocity = Vector3.zero;
                _joint.targetVelocity = Vector3.zero;
                float maxForce = _joint.currentForce.magnitude * (brakeModeMultiplyer * 3);
                drive = _joint.angularXDrive;
                drive.maximumForce = maxForce;

                _joint.angularXDrive = drive;

                drive = _joint.angularYZDrive;
                drive.maximumForce = maxForce;
                _joint.angularYZDrive = drive;

                drive = _joint.xDrive;
                drive.maximumForce = maxForce;
                _joint.xDrive = drive;

                drive = _joint.yDrive;
                drive.maximumForce = maxForce;
                _joint.yDrive = drive;

                drive = _joint.zDrive;
                drive.maximumForce = maxForce;
                _joint.zDrive = drive;

                wasDisabled = true;

                if (_useAudio && _movementAudioSource.isPlaying)
                {
                    _movementAudioSource.Stop();
                }
            }
            else if (wasDisabled && BaseGameManager.Instance.RobotState != RobotState.Disabled)
            {
                JointDrive drive;

                drive = _joint.angularXDrive;
                drive.maximumForce = Mathf.Infinity;

                _joint.angularXDrive = drive;

                drive = _joint.angularYZDrive;
                drive.maximumForce = Mathf.Infinity;
                _joint.angularYZDrive = drive;

                drive = _joint.xDrive;
                drive.maximumForce = Mathf.Infinity;
                _joint.xDrive = drive;

                drive = _joint.yDrive;
                drive.maximumForce = Mathf.Infinity;
                _joint.yDrive = drive;

                drive = _joint.zDrive;
                drive.maximumForce = Mathf.Infinity;
                _joint.zDrive = drive;

                wasDisabled = false;
            }
        }

        private void FixedUpdate()
        {
            UpdateDisabledLock();
        }

        // Disable locking must also run when a controller stops sending targets.
        private bool UpdateDisabledLock()
        {
            if (_joint == null || BaseGameManager.Instance is null) return false;
            bool shouldLock = lockJointOnDisable && BaseGameManager.Instance.RobotState == RobotState.Disabled;
            return ApplyDisabledLock(shouldLock);
        }

        private bool ApplyDisabledLock(bool shouldLock)
        {
            if (shouldLock)
            {
                if (!locked)
                {
                    _motionsBeforeDisable = new[]
                    {
                        _joint.xMotion, _joint.yMotion, _joint.zMotion,
                        _joint.angularXMotion, _joint.angularYMotion, _joint.angularZMotion
                    };
                    _projectionBeforeDisable = _joint.projectionMode;
                    _anchorBeforeDisable = _joint.connectedAnchor;
                    lockAllAxisV1();
                    locked = true;
                }
                return true;
            }

            if (locked)
            {
                // Re-homing is temporary: limited travel must retain its original center.
                _joint.connectedAnchor = _anchorBeforeDisable;
                _joint.xMotion = _motionsBeforeDisable[0];
                _joint.yMotion = _motionsBeforeDisable[1];
                _joint.zMotion = _motionsBeforeDisable[2];
                _joint.angularXMotion = _motionsBeforeDisable[3];
                _joint.angularYMotion = _motionsBeforeDisable[4];
                _joint.angularZMotion = _motionsBeforeDisable[5];
                _joint.projectionMode = _projectionBeforeDisable;
                wasLocked = System.Array.TrueForAll(_motionsBeforeDisable, motion => motion == ConfigurableJointMotion.Locked);
                locked = false;
            }
            return false;
        }

        /// <summary>
        /// Builder class for configuring constant velocity joint motion.
        /// </summary>
        public class Velocity
        {
            private GenericJoint _joint;

            /// <summary>
            /// Constructs a Velocity builder for the specified joint.
            /// </summary>
            public Velocity(GenericJoint joint)
            {
                _joint = joint;
            }

            /// <summary>
            /// Sets the axis of rotation for velocity mode.
            /// </summary>
            /// <param name="axis">The axis to rotate around.</param>
            /// <returns>This builder for chaining.</returns>
            public Velocity WithAxis(JointAxis axis)
            {
                var version = MoSim.ActiveVersion;
                return WithAxisV1(axis);
            }

            private Velocity WithAxisV1(JointAxis axis)
            {
                _joint.velocityAxis = axis;
                return this;
            }
        }

        /// <summary>
        /// Sets a constant angular velocity for the joint.
        /// </summary>
        /// <param name="velocity">Angular velocity in degrees/sec.</param>
        /// <returns>The Velocity builder for chaining.</returns>
        public Velocity SetAngularVelocity(float velocity)
        {
            var version = MoSim.ActiveVersion;

            return SetAngularVelocityV1(velocity);
        }

        private Velocity SetAngularVelocityV1(float velocity)
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled && !ignoreDisabledMode)
                return _velocityObject;
            Vector3 axisOfRotation;
            switch (velocityAxis)
            {
                case JointAxis.X:
                    axisOfRotation = Vector3.right;
                    break;
                case JointAxis.Y:
                    axisOfRotation = Vector3.up;
                    break;
                case JointAxis.Z:
                    axisOfRotation = Vector3.forward;
                    break;
                default:
                    axisOfRotation = Vector3.right;
                    break;
            }

            var targetVelocity = velocity * axisOfRotation;

            _joint.targetAngularVelocity = targetVelocity;
            return _velocityObject;
        }

        public class PIDLinear
        {
            private GenericJoint _genericJoint;

            public PIDLinear(GenericJoint genericJoint)
            {
                _genericJoint = genericJoint;
            }

            /// <summary>
            /// set the axis for it to move on
            /// </summary>
            /// <param name="axis"></param>
            /// <returns></returns>
            public PIDLinear withAxis(JointAxis axis)
            {
                var version = MoSim.ActiveVersion;
                return withAxisV1(axis);
            }

            private PIDLinear withAxisV1(JointAxis axis)
            {
                _genericJoint.linearAxis = axis;
                return this;
            }

            /// <summary>
            /// chnages the angle it reads current locaiton on. rarely joints can move on the wrong axis
            /// </summary>
            /// <param name="axis"></param>
            /// <returns></returns>
            public PIDLinear useDifferentEncoderAxis(JointAxis axis)
            {
                var version = MoSim.ActiveVersion;
                return useDifferentEncoderAxisV1(axis);
            }

            private PIDLinear useDifferentEncoderAxisV1(JointAxis axis)
            {
                _genericJoint.encoderAxis = axis;
                _genericJoint.useDifferentEncoderAxis = true;
                return this;
            }

            /// <summary>
            /// flips the targets such that - is +
            /// </summary>
            /// <returns></returns>
            public PIDLinear flipDirection()
            {
                var version = MoSim.ActiveVersion;
                return flipDirectionV1();
            }

            private PIDLinear flipDirectionV1()
            {
                _genericJoint.flipDirection = true;
                return this;
            }

            /// <summary>
            /// use the automatic starting offset values
            /// </summary>
            /// <returns></returns>
            public PIDLinear useAutomaticStartingOffset()
            {
                var version = MoSim.ActiveVersion;
                return useAutomaticStartingOffsetV1();
            }

            private PIDLinear useAutomaticStartingOffsetV1()
            {
                _genericJoint.useStartingOffset = true;
                return this;
            }

            public PIDLinear withFixedDTiming()
            {
                var version = MoSim.ActiveVersion;
                return withFixedDTimingV1();
            }

            private PIDLinear withFixedDTimingV1()
            {
                _genericJoint.fixedDTiming = true;
                return this;
            }

            /// <summary>
            /// Adds Feed Forward value
            /// Is Consumed to 0 on next update loop
            /// </summary>
            /// <param name="ffValue"></param>
            /// <returns></returns>
            public PIDLinear withFeedFoward(float ffValue)
            {
                _genericJoint.lastFFvalue = ffValue;
                return this;
            }
        }

        /// <summary>
        /// set the linear target of the pid loop. Distance is in Inch
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="jointAxis"></param>
        /// <param name="encoderAxis"></param>
        /// <param name="fliped">Optional</param>
        /// <param name="startingOffset"></param>
        public PIDLinear SetLinearTarget(float distance)
        {
            var version = MoSim.ActiveVersion;

            return SetLinearTargetV1(distance);
        }

        private PIDLinear SetLinearTargetV1(float distance)
        {
            if (BaseGameManager.Instance is null) return _pidLinear;
            if (UpdateDisabledLock()) return _pidLinear;
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled && !ignoreDisabledMode) return _pidLinear;
            Vector3 axisOfMotion;
            JointAxis checkAxis;
            float distanceOffset;

            if (useDifferentEncoderAxis)
            {
                switch (encoderAxis)
                {
                    case JointAxis.X:
                        checkAxis = encoderAxis;
                        distanceOffset = _startingPosition.x;
                        break;
                    case JointAxis.Y:
                        checkAxis = encoderAxis;
                        distanceOffset = _startingPosition.y;
                        break;
                    case JointAxis.Z:
                        checkAxis = encoderAxis;
                        distanceOffset = _startingPosition.z;
                        break;
                    default:
                        checkAxis = encoderAxis;
                        distanceOffset = 0;
                        break;
                }
            }
            else
            {
                switch (linearAxis)
                {
                    case JointAxis.X:
                        checkAxis = linearAxis;
                        distanceOffset = _startingPosition.x;
                        break;
                    case JointAxis.Y:
                        checkAxis = linearAxis;
                        distanceOffset = _startingPosition.y;
                        break;
                    case JointAxis.Z:
                        checkAxis = linearAxis;
                        distanceOffset = _startingPosition.z;
                        break;
                    default:
                        checkAxis = linearAxis;
                        distanceOffset = 0;
                        break;
                }
            }

            switch (linearAxis)
            {
                case JointAxis.X:
                    axisOfMotion = new Vector3(1, 0, 0);
                    break;
                case JointAxis.Y:
                    axisOfMotion = new Vector3(0, 1, 0);
                    break;
                case JointAxis.Z:
                    axisOfMotion = new Vector3(0, 0, 1);
                    break;
                default:
                    axisOfMotion = Vector3.zero;
                    distanceOffset = 0;
                    break;
            }

            distance = flipDirection ? -distance : distance;

            float currentPosition = GetAxisLocation(checkAxis);

            currentPosition -= useStartingOffset ? distanceOffset : 0;
            var timestep = Mathf.Clamp(Time.time - _lastTime, 0.001f, 10000);

            float adjustedTarget = distance * 0.0254f;

            var targetLinearVelocity = _pidController.UpdateLinear(timestep, currentPosition, adjustedTarget);

            var targetVelocity = (targetLinearVelocity + lastFFvalue) * axisOfMotion;
            lastFFvalue = 0;

            CurrentValue = currentPosition * 39.37008f;
            TargetValue = adjustedTarget * 39.37008f;

            _joint.targetVelocity = -targetVelocity;
            OutputValue = -targetVelocity.magnitude;

            if (fixedDTiming)
            {
                _lastTime = Time.time;
            }

            if (_useAudio)
            {
                var error = Mathf.Abs(TargetValue - CurrentValue);
                var errorDelta = Mathf.Abs(error - _previousError) / timestep;
                errorDelta *= 1000f;
                var errorDeltaInt = Mathf.RoundToInt(errorDelta);
                DebugErrorDeltaInt = errorDeltaInt;

                var normalized = Mathf.Clamp01(errorDeltaInt / 90f);
                normalized = Mathf.Pow(normalized, Mathf.Max(0.0001f, audioResponseExponent));

                var targetPitch = Mathf.Lerp(movementAudioPitchRange.x, movementAudioPitchRange.y, normalized);
                var targetVolume = Mathf.Lerp(movementAudioVolumeRange.x, movementAudioVolumeRange.y, normalized);

                var pitchAlpha = 1f - Mathf.Exp(-audioPitchSmoothSpeed * timestep);
                var volumeAlpha = 1f - Mathf.Exp(-audioVolumeSmoothSpeed * timestep);

                _movementAudioSource.pitch = Mathf.Lerp(_movementAudioSource.pitch, targetPitch, pitchAlpha);
                _movementAudioSource.volume = Mathf.Lerp(_movementAudioSource.volume, targetVolume, volumeAlpha);

                if (BaseGameManager.Instance.RobotState == RobotState.Disabled ||
                    (error <= audioErrorThreshold && _movementAudioSource.isPlaying))
                {
                    _movementAudioSource.Stop();
                }
                else if (error > audioErrorThreshold && !_movementAudioSource.isPlaying)
                {
                    _movementAudioSource.Play();
                }

                if (debugPrint)
                {
                    Debug.Log("Angle Error: " + error + " | Error Delta: " + errorDeltaInt + " | Pitch: " +
                              _movementAudioSource.pitch + " | Volume: " + _movementAudioSource.volume);
                }

                _previousError = error;
            }

            return _pidLinear;
        }

        public MoSimResult<MoSimLib.Void> setLinearOffset(Vector3 positionOffset, bool isInches = true)
        {
            var version = MoSim.ActiveVersion;

            setLinearOffsetV1(positionOffset, isInches);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void setLinearOffsetV1(Vector3 positionOffset, bool isInches = true)
        {
            positionOffset *= isInches ? 0.0254f : 1;
            _startingPosition = positionOffset;
        }


        /// <summary>
        /// Get the linear local position of a singular axis
        /// </summary>
        /// <param name="jointAxis"></param>
        /// <returns></returns>
        public MoSimResult<float> GetAxisLocation(JointAxis jointAxis)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetAxisLocationV1(jointAxis));
        }

        private float GetAxisLocationV1(JointAxis jointAxis)
        {
            switch (jointAxis)
            {
                case JointAxis.X:
                    return _joint.transform.localPosition.x;
                case JointAxis.Y:
                    return _joint.transform.localPosition.y;
                case JointAxis.Z:
                    return _joint.transform.localPosition.z;
                default:
                    return 0;
            }
        }

        public class PIDAngle
        {
            private GenericJoint _joint;

            public PIDAngle(GenericJoint genericJoint)
            {
                _joint = genericJoint;
            }

            /// <summary>
            /// Sets the axis it should move on
            /// </summary>
            /// <param name="jointAxis"></param>
            /// <returns></returns>
            public PIDAngle withAxis(JointAxis jointAxis)
            {
                var version = MoSim.ActiveVersion;
                return withAxisV1(jointAxis);
            }

            private PIDAngle withAxisV1(JointAxis jointAxis)
            {
                _joint.angularAxis = jointAxis;
                return this;
            }

            /// <summary>
            /// flip the direction of motion such that - becomes +
            /// </summary>
            /// <returns></returns>
            public PIDAngle flipDirection()
            {
                var version = MoSim.ActiveVersion;
                return flipDirectionV1();
            }

            private PIDAngle flipDirectionV1()
            {
                _joint.flipDirection = true;
                return this;
            }

            /// <summary>
            /// use a starting offset value equal to the location it starts at in the prefab
            /// </summary>
            /// <returns></returns>
            public PIDAngle useAutomaticStartingOffset()
            {
                var version = MoSim.ActiveVersion;
                return useAutomaticStartingOffsetV1();
            }

            private PIDAngle useAutomaticStartingOffsetV1()
            {
                _joint.useStartingOffset = true;
                return this;
            }

            /// <summary>
            /// use a starting offset set to the angle
            /// </summary>
            /// <param name="angle"></param>
            /// <returns></returns>
            public PIDAngle useCustomStartingOffset(float angle)
            {
                var version = MoSim.ActiveVersion;
                return useCustomStartingOffsetV1(angle);
            }

            private PIDAngle useCustomStartingOffsetV1(float angle)
            {
                _joint.useStartingOffset = true;
                _joint.usingManualAngleOffset = true;
                var adjustedAngle = _joint.flipDirection ? Utils.FlipAngle(angle) : angle;
                adjustedAngle = Mathf.Repeat(adjustedAngle, 360);
                _joint.angleOffset = adjustedAngle;
                return this;
            }

            public PIDAngle withFixedDTiming()
            {
                var version = MoSim.ActiveVersion;
                return withFixedDTimingV1();
            }

            private PIDAngle withFixedDTimingV1()
            {
                _joint.fixedDTiming = true;
                return this;
            }

            /// <summary>
            /// The Joint will not Pass through the passed in angle
            /// </summary>
            /// <param name="angle"></param>
            /// <returns></returns>
            public PIDAngle noWrap(float angle)
            {
                var version = MoSim.ActiveVersion;
                return noWrapV1(angle);
            }

            private PIDAngle noWrapV1(float angle)
            {
                _joint.noWrap = true;
                _joint.noWrapAngle = Mathf.Repeat(angle, 360);
                _joint.noWrapAngle = _joint.noWrapAngle > 180 ? _joint.noWrapAngle - 360 : _joint.noWrapAngle;
                return this;
            }

            /// <summary>
            /// Adds a feed forward value
            /// Is consumed back to 0 once used in an update loop
            /// </summary>
            /// <param name="ffValue"></param>
            /// <returns></returns>
            public PIDAngle withFeedFoward(float ffValue)
            {
                _joint.lastFFvalue = ffValue;
                return this;
            }
        }

        /// <summary>
        /// Sets the PID target for an angular action
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="jointAxis"></param>
        /// <param name="fliped"></param>
        /// <param name="startOffset">Optional</param>
        public PIDAngle SetTargetAngle(float angle)
        {
            var version = MoSim.ActiveVersion;

            return SetTargetAngleV1(angle);
        }

        private PIDAngle SetTargetAngleV1(float angle)
        {
            if (BaseGameManager.Instance is null) return _pidAngle;
            if (UpdateDisabledLock()) return _pidAngle;

            if (BaseGameManager.Instance.RobotState == RobotState.Disabled && !ignoreDisabledMode) return _pidAngle;
            Vector3 axisOfRotation;
            switch (angularAxis)
            {
                case JointAxis.X:
                    axisOfRotation = new Vector3(1, 0, 0);
                    break;
                case JointAxis.Y:
                    axisOfRotation = new Vector3(0, 1, 0);
                    break;
                case JointAxis.Z:
                    axisOfRotation = new Vector3(0, 0, 1);
                    break;
                default:
                    axisOfRotation = Vector3.zero;
                    break;
            }

            float currentAngle = GetSingleAxisAngle(angularAxis);

            var angleAdjust = usingManualAngleOffset ? GetAxisAngle(angularAxis, startingAngle) : angleOffset;

            currentAngle -= useStartingOffset ? angleAdjust : 0;

            angle = flipDirection ? Utils.FlipAngle(angle) : angle;

            var wrapAngle = noWrapAngle;
            wrapAngle = flipDirection ? Utils.FlipAngle(wrapAngle) : wrapAngle;
            wrapAngle = Mathf.Repeat(wrapAngle, 360);

            float targetForPid = angle;

            var timestep = Mathf.Clamp(Time.time - _lastTime, 0.001f, 10000);

            if (fixedDTiming)
            {
                _lastTime = Time.time;
            }
            if (noWrap)
            {
                if (PassesThroughWrapAngle(currentAngle, angle, wrapAngle))
                {
                    isWrapping = true;
                    // Force the long way by adding/subtracting 360 to the target
                    float difference = Utils.AngleDifference(angle, currentAngle);

                    if (difference > 0)
                    {
                        // Would normally go counter-clockwise, force clockwise
                        targetForPid = wrapAngle + 180;
                    }
                    else
                    {
                        // Would normally go clockwise, force counter-clockwise
                        targetForPid = wrapAngle - 180;
                    }
                }
                else
                {
                    // Normal case - shortest path doesn't pass through wrap angle
                    targetForPid = angle;
                    isWrapping = false;
                }
            }


            if (_useAudio)
            {
                var error = Mathf.Abs(Utils.AngleDifference(targetForPid, currentAngle));
                var errorDelta = Mathf.Abs(error - _previousError) / timestep;
                errorDelta *= 1000f;
                var errorDeltaInt = Mathf.RoundToInt(errorDelta);
                DebugErrorDeltaInt = errorDeltaInt;

                var normalized = Mathf.Clamp01(errorDeltaInt / 90f);
                normalized = Mathf.Pow(normalized, Mathf.Max(0.0001f, audioResponseExponent));

                var targetPitch = Mathf.Lerp(movementAudioPitchRange.x, movementAudioPitchRange.y, normalized);
                var targetVolume = Mathf.Lerp(movementAudioVolumeRange.x, movementAudioVolumeRange.y, normalized);

                var pitchAlpha = 1f - Mathf.Exp(-audioPitchSmoothSpeed * timestep);
                var volumeAlpha = 1f - Mathf.Exp(-audioVolumeSmoothSpeed * timestep);

                _movementAudioSource.pitch = Mathf.Lerp(_movementAudioSource.pitch, targetPitch, pitchAlpha);
                _movementAudioSource.volume = Mathf.Lerp(_movementAudioSource.volume, targetVolume, volumeAlpha);

                if (BaseGameManager.Instance.RobotState == RobotState.Disabled ||
                    (error <= audioErrorThreshold && _movementAudioSource.isPlaying))
                {
                    _movementAudioSource.Stop();
                }
                else if (error > audioErrorThreshold && !_movementAudioSource.isPlaying)
                {
                    _movementAudioSource.Play();
                }

                if (debugPrint)
                {
                    Debug.Log("Angle Error: " + error + " | Error Delta: " + errorDeltaInt + " | Pitch: " +
                              _movementAudioSource.pitch + " | Volume: " + _movementAudioSource.volume);
                }

                _previousError = error;
            }

            var targetAngularVelocityMagnitude = _pidController.UpdateAngle(timestep, currentAngle, targetForPid);
            var targetVelocity = (targetAngularVelocityMagnitude + lastFFvalue) * axisOfRotation;
            lastFFvalue = 0;

            TargetValue = targetForPid;
            _joint.targetAngularVelocity = targetVelocity;
            OutputValue = targetVelocity.magnitude;
            return _pidAngle;
        }

        /// <summary>
        /// Determines if the shortest path from current to target angle passes through the wrap restriction angle.
        /// </summary>
        /// <param name="currentAngle">Current angle in degrees.</param>
        /// <param name="targetAngle">Target angle in degrees.</param>
        /// <param name="wrapAngle">Forbidden angle that should not be crossed.</param>
        /// <returns>True if the shortest path crosses the wrap angle.</returns>
        bool PassesThroughWrapAngle(float currentAngle, float targetAngle, float wrapAngle)
        {
            // Normalize all angles to [0, 360)
            currentAngle = ((currentAngle % 360) + 360) % 360;
            targetAngle = ((targetAngle % 360) + 360) % 360;
            wrapAngle = ((wrapAngle % 360) + 360) % 360;

            // Calculate the shortest angular difference
            float diff = targetAngle - currentAngle;
            if (diff > 180.0f) diff -= 360.0f;
            if (diff < -180.0f) diff += 360.0f;

            // Determine the angular span we're traversing
            float endAngle = currentAngle + diff;
            if (endAngle < 0) endAngle += 360.0f;
            if (endAngle >= 360.0f) endAngle -= 360.0f;

            // Check if wrapAngle is between start and end on the shortest path
            if (diff > 0)
            {
                // Moving counter-clockwise
                if (currentAngle <= endAngle)
                {
                    return (wrapAngle > currentAngle && wrapAngle < endAngle);
                }
                else
                {
                    return (wrapAngle > currentAngle || wrapAngle < endAngle);
                }
            }
            else
            {
                // Moving clockwise  
                if (currentAngle >= endAngle)
                {
                    return (wrapAngle < currentAngle && wrapAngle > endAngle);
                }
                else
                {
                    return (wrapAngle < currentAngle || wrapAngle > endAngle);
                }
            }
        }

        /// <summary>
        /// Extracts the rotation angle around a specific axis from a quaternion.
        /// </summary>
        /// <param name="jointAxis">The axis to extract the angle for.</param>
        /// <param name="quaternion">The quaternion to decompose.</param>
        /// <returns>The signed angle in degrees around the specified axis.</returns>
        public MoSimResult<float> GetAxisAngle(JointAxis jointAxis, Quaternion quaternion)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetAxisAngleV1(jointAxis, quaternion));
        }

        private float GetAxisAngleV1(JointAxis jointAxis, Quaternion quaternion)
        {
            Vector3 targetAxis;

            switch (jointAxis)
            {
                case JointAxis.X:
                    targetAxis = Vector3.right;
                    break;
                case JointAxis.Y:
                    targetAxis = Vector3.up;
                    break;
                case JointAxis.Z:
                    targetAxis = Vector3.forward;
                    break;
                default:
                    targetAxis = Vector3.zero;
                    break;
            }

            Quaternion deltaRotation = quaternion;

            deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);


            float projection = Vector3.Dot(targetAxis, axis);

            //    - The signed angle around our target axis
            float signedAngle = angle * projection;

            signedAngle = Mathf.Repeat(signedAngle, 360);

            return signedAngle;
        }

        /// <summary>
        /// Returns the corrected and offset joint angle
        /// </summary>
        /// <returns></returns>
        public MoSimResult<float> GetAngle()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetAngleV1());
        }

        private float GetAngleV1()
        {
            float currentAngle = GetSingleAxisAngle(angularAxis);

            var angleAdjust = usingManualAngleOffset ? GetAxisAngle(angularAxis, startingAngle) : angleOffset;

            currentAngle -= useStartingOffset ? angleAdjust : 0;

            return currentAngle;
        }

        /// <summary>
        /// Returns the current angle of the joint around a single axis.
        /// Most accurate when joint only rotates around one axis.
        /// </summary>
        /// <param name="jointAxis">The axis to measure.</param>
        /// <returns>The current angle in degrees.</returns>
        public MoSimResult<float> GetSingleAxisAngle(JointAxis jointAxis)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetSingleAxisAngleV1(jointAxis));
        }

        private float GetSingleAxisAngleV1(JointAxis jointAxis)
        {
            Vector3 targetAxis;

            switch (jointAxis)
            {
                case JointAxis.X:
                    targetAxis = Vector3.right;
                    break;
                case JointAxis.Y:
                    targetAxis = Vector3.up;
                    break;
                case JointAxis.Z:
                    targetAxis = Vector3.forward;
                    break;
                default:
                    targetAxis = Vector3.zero;
                    break;
            }

            Quaternion deltaRotation = transform.localRotation;

            deltaRotation.ToAngleAxis(out float angle, out Vector3 axis);


            float projection = Vector3.Dot(targetAxis, axis);

            //    - The signed angle around our target axis
            float signedAngle = angle * projection;

            signedAngle = Mathf.Repeat(signedAngle, 360);
            CurrentValue = signedAngle;
            return signedAngle;
        }

        /// <summary>
        /// Updates PID constants if they have changed, resetting the controller state.
        /// </summary>
        /// <param name="pidmi">The new PID constants to apply.</param>
        public MoSimResult<MoSimLib.Void> UpdatePid(PidConstants pidmi)
        {
            var version = MoSim.ActiveVersion;

            UpdatePidV1(pidmi);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void UpdatePidV1(PidConstants pidmi)
        {
            if (!Mathf.Approximately(pidmi.kP, _pidController.proportionalGain) ||
                !Mathf.Approximately(pidmi.kI, _pidController.integralGain) ||
                !Mathf.Approximately(pidmi.kD, _pidController.derivativeGain) ||
                !Mathf.Approximately(pidmi.Isaturation, _pidController.integralSaturation))
            {
                _pidController.proportionalGain = pidmi.kP;
                _pidController.integralGain = pidmi.kI;
                _pidController.derivativeGain = pidmi.kD;
                _pidController.integralSaturation = pidmi.Isaturation;
                _pidController.ResetController();
            }

            if (!Mathf.Approximately(pidmi.Max, _pidController.outputMax))
            {
                _pidController.outputMax = pidmi.Max;
                _pidController.outputMin = -pidmi.Max;
            }
        }

        /// <summary>
        /// Initializes the PID controller with the specified constants and configures joint springs.
        /// </summary>
        /// <param name="pidmi">The PID constants defining controller behavior.</param>
        public MoSimResult<MoSimLib.Void> SetPid(PidConstants pidmi)
        {
            var version = MoSim.ActiveVersion;

            SetPidV1(pidmi);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetPidV1(PidConstants pidmi)
        {
            _pidController ??= new PIDController();
            _pidController.proportionalGain = pidmi.kP;
            _pidController.integralGain = pidmi.kI;
            _pidController.derivativeGain = pidmi.kD;
            _pidController.integralSaturation = pidmi.Isaturation;

            _pidController.outputMax = pidmi.Max;
            _pidController.outputMin = -pidmi.Max;

            _pidController.integralSaturation = pidmi.Isaturation;


            if (_joint == null)
            {
                _joint = gameObject.GetComponent<ConfigurableJoint>();
            }

            JointDrive drive;

            drive = _joint.angularXDrive;
            drive.positionSpring = 0;

            _joint.angularXDrive = drive;

            drive = _joint.angularYZDrive;
            drive.positionSpring = 0;
            _joint.angularYZDrive = drive;

            drive = _joint.xDrive;
            drive.positionSpring = 0;
            _joint.xDrive = drive;

            drive = _joint.yDrive;
            drive.positionSpring = 0;
            _joint.yDrive = drive;

            drive = _joint.zDrive;
            drive.positionSpring = 0;
            _joint.zDrive = drive;

            _lastTime = Time.time;

            flipDirection = false;
            useStartingOffset = false;
            noWrap = false;

            _pidLinear = new PIDLinear(this);
            _pidAngle = new PIDAngle(this);
            _velocityObject = new Velocity(this);
        }

        /// <summary>
        /// Locks all joint axes, preventing any motion.
        /// Only previously free or limited axes can change the joint's home.
        /// Axes must be manually freed with freeLinearAxis or freeAngularAxis.
        /// </summary>
        public MoSimResult<MoSimLib.Void> lockAllAxis()
        {
            var version = MoSim.ActiveVersion;

            if (!UpdateDisabledLock()) lockAllAxisV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void lockAllAxisV1()
        {
            if (wasLocked) return;

            _rigidbody.velocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _joint.targetVelocity = Vector3.zero;
            _joint.targetAngularVelocity = Vector3.zero;

            RehomeUnlockedAxes();

            wasLocked = true;

            _joint.xMotion = ConfigurableJointMotion.Locked;
            _joint.yMotion = ConfigurableJointMotion.Locked;
            _joint.zMotion = ConfigurableJointMotion.Locked;
            _joint.angularXMotion = ConfigurableJointMotion.Locked;
            _joint.angularYMotion = ConfigurableJointMotion.Locked;
            _joint.angularZMotion = ConfigurableJointMotion.Locked;
            _joint.projectionAngle = 1f;
            _joint.projectionMode = JointProjectionMode.PositionAndRotation;
        }

        private void RehomeUnlockedAxes()
        {
            var connected = _joint.connectedBody;
            var connectedRotation = connected != null ? connected.rotation : Quaternion.identity;
            var referenceRotation = connectedRotation * _lockReferenceRotation;
            var jointZ = Vector3.Cross(_joint.axis, _joint.secondaryAxis).normalized;
            var jointY = Vector3.Cross(jointZ, _joint.axis).normalized;
            var jointSpace = Quaternion.LookRotation(jointZ, jointY);
            var worldJointSpace = referenceRotation * jointSpace;

            // Keep the existing home on locked axes, even if the solver has allowed some drift.
            var previousAnchor = connected != null
                ? connected.transform.TransformPoint(_joint.connectedAnchor)
                : _joint.connectedAnchor;
            var currentAnchor = transform.TransformPoint(_joint.anchor);
            var displacement = Quaternion.Inverse(worldJointSpace) * (currentAnchor - previousAnchor);
            if (_joint.xMotion == ConfigurableJointMotion.Locked) displacement.x = 0;
            if (_joint.yMotion == ConfigurableJointMotion.Locked) displacement.y = 0;
            if (_joint.zMotion == ConfigurableJointMotion.Locked) displacement.z = 0;

            var homeAnchor = previousAnchor + worldJointSpace * displacement;

            // Project the relative rotation onto the previously movable angular axes.
            // With one unlocked axis this extracts its twist and discards off-axis swing.
            var deltaRotation = Quaternion.Inverse(worldJointSpace) * _rigidbody.rotation * jointSpace;
            if (_joint.angularXMotion == ConfigurableJointMotion.Locked) deltaRotation.x = 0;
            if (_joint.angularYMotion == ConfigurableJointMotion.Locked) deltaRotation.y = 0;
            if (_joint.angularZMotion == ConfigurableJointMotion.Locked) deltaRotation.z = 0;
            _lockReferenceRotation = _lockReferenceRotation * jointSpace * deltaRotation.normalized * Quaternion.Inverse(jointSpace);

            // Updating the joint can reset its angular reference, so correct locked-axis
            // drift before rebuilding it at the accepted home rotation.
            var homeRotation = connectedRotation * _lockReferenceRotation;
            transform.rotation = homeRotation;
            _rigidbody.rotation = homeRotation;
            _rigidbody.WakeUp();
            Physics.SyncTransforms();
            _joint.autoConfigureConnectedAnchor = false;
            _joint.connectedAnchor = connected != null
                ? connected.transform.InverseTransformPoint(homeAnchor)
                : homeAnchor;
            if (connected == null)
            {
                // Null-to-null does not refresh Unity's world rotation reference.
                // Briefly connect a temporary body to force an actual reconnection.
                var referenceObject = new GameObject("Joint lock reference") { hideFlags = HideFlags.HideAndDontSave };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(referenceObject, gameObject.scene);
                var referenceBody = referenceObject.AddComponent<Rigidbody>();
                referenceBody.isKinematic = true;
                _joint.connectedBody = referenceBody;
                _joint.connectedBody = null;
                Destroy(referenceObject);
            }
            else
            {
                _joint.connectedBody = null;
                _joint.connectedBody = connected;
            }
        }

        /// <summary>
        /// Unlocks motion along a specific linear axis.
        /// </summary>
        /// <param name="axis">The axis to free (X, Y, or Z).</param>
        public MoSimResult<MoSimLib.Void> freeLinearAxis(JointAxis axis)
        {
            var version = MoSim.ActiveVersion;

            freeLinearAxisV1(axis);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void freeLinearAxisV1(JointAxis axis)
        {
            if (UpdateDisabledLock()) return;
            wasLocked = false;
            _joint.projectionMode = JointProjectionMode.None;
            switch (axis)
            {
                case JointAxis.X:
                    _joint.xMotion = ConfigurableJointMotion.Free;
                    break;
                case JointAxis.Y:
                    _joint.yMotion = ConfigurableJointMotion.Free;
                    break;
                case JointAxis.Z:
                    _joint.zMotion = ConfigurableJointMotion.Free;
                    break;
            }
        }

        /// <summary>
        /// Limits motion along a specific linear axis and allows a subsequent lock to re-home its anchor.
        /// </summary>
        /// <param name="axis">The axis to limit (X, Y, or Z).</param>
        public MoSimResult<MoSimLib.Void> limitLinearAxis(JointAxis axis)
        {
            var version = MoSim.ActiveVersion;

            limitLinearAxisV1(axis);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void limitLinearAxisV1(JointAxis axis)
        {
            if (UpdateDisabledLock()) return;
            wasLocked = false;
            _joint.projectionMode = JointProjectionMode.None;
            switch (axis)
            {
                case JointAxis.X:
                    _joint.xMotion = ConfigurableJointMotion.Limited;
                    break;
                case JointAxis.Y:
                    _joint.yMotion = ConfigurableJointMotion.Limited;
                    break;
                case JointAxis.Z:
                    _joint.zMotion = ConfigurableJointMotion.Limited;
                    break;
            }
        }

        /// <summary>
        /// Limits rotation around a specific angular axis and allows a subsequent lock to re-home its anchor.
        /// </summary>
        /// <param name="axis">The axis to limit (X, Y, or Z).</param>
        public MoSimResult<MoSimLib.Void> limitAngularAxis(JointAxis axis)
        {
            var version = MoSim.ActiveVersion;

            limitAngularAxisV1(axis);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void limitAngularAxisV1(JointAxis axis)
        {
            if (UpdateDisabledLock()) return;
            wasLocked = false;
            _joint.projectionMode = JointProjectionMode.None;
            switch (axis)
            {
                case JointAxis.X:
                    _joint.angularXMotion = ConfigurableJointMotion.Limited;
                    break;
                case JointAxis.Y:
                    _joint.angularYMotion = ConfigurableJointMotion.Limited;
                    break;
                case JointAxis.Z:
                    _joint.angularZMotion = ConfigurableJointMotion.Limited;
                    break;
            }
        }

        /// <summary>
        /// Unlocks rotation around a specific angular axis.
        /// </summary>
        /// <param name="axis">The axis to free (X, Y, or Z).</param>
        public MoSimResult<MoSimLib.Void> freeAngularAxis(JointAxis axis)
        {
            var version = MoSim.ActiveVersion;

            freeAngularAxisV1(axis);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void freeAngularAxisV1(JointAxis axis)
        {
            if (UpdateDisabledLock()) return;
            wasLocked = false;
            _joint.projectionMode = JointProjectionMode.None;
            switch (axis)
            {
                case JointAxis.X:
                    _joint.angularXMotion = ConfigurableJointMotion.Free;
                    break;
                case JointAxis.Y:
                    _joint.angularYMotion = ConfigurableJointMotion.Free;
                    break;
                case JointAxis.Z:
                    _joint.angularZMotion = ConfigurableJointMotion.Free;
                    break;
            }
        }

        /// <summary>
        /// Gets the Rigidbody component attached to this joint.
        /// </summary>
        /// <returns>The Rigidbody of this joint GameObject.</returns>
        public MoSimResult<Rigidbody> GetRigidbody()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<Rigidbody>.Success(GetRigidbodyV1());
        }

        private Rigidbody GetRigidbodyV1()
        {
            return _rigidbody;
        }
    }
}
