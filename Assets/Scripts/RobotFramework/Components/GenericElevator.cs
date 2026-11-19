using System;
using MoSimLib;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using UnityEngine;
using UnityEngine.Serialization;

namespace RobotFramework.Components
{
    /// <summary>
    /// Manages elevator/lift mechanisms with support for cascade and continuous types.
    /// Coordinates multiple GenericJoint stages with PID control and audio feedback.
    /// </summary>
    public class GenericElevator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Array of GenericJoint stages composing this elevator.")]
        private GenericJoint[] stages;
        
        [SerializeField]
        [Tooltip("Elevator configuration type (cascade or continuous).")]
        private ElevatorType elevatorType;

        [FormerlySerializedAs("pidmi")]
        [SerializeField]
        [Tooltip("PID constants for primary elevator stage control.")]
        private PidConstants pidConstants;

        [SerializeField]
        [Tooltip("PID constants for individual stages (continuous elevators only).")]
        private PidConstants stagesPidConstants;

        [SerializeField]
        [Tooltip("Which axis the elevator moves along.")]
        private JointAxis elevatorAxis;
        
        [SerializeField]
        [Tooltip("Whether to invert the elevator direction.")]
        private bool flipped;

        [SerializeField]
        [Tooltip("Per-stage PID overrides.")]
        private PerStageOverrides[] setStageOverrides;

        [SerializeField]
        [Tooltip("Height of each stage in inches.")]
        private float stageHeight;
        
        [SerializeField]
        [Tooltip("Overlap between stages in inches.")]
        private float stageOverlap;

        [SerializeField]
        [Tooltip("Height of the carriage (0 for open-top, or stage overlap value).")]
        private float carriageHeight;

        private float _previousStageHeight;
        private float _previousStageOverlap;
        private float _previousCarriageHeight;

        private Vector3 _carriageStartPosition;

        private JointAxis _lastAxis;
        private bool _lastOffset;

        [Header("Audio Settings")] [SerializeField]
        private AudioSource elevatorSound;

        [SerializeField] private AudioClip elevatorClip;
        [SerializeField] private float minSpeed = 1f;
        [SerializeField] private float maxSpeed = 60f;
        [SerializeField] private Vector2 soundPitchRange = new Vector2(0.8f, 1.2f);
        [SerializeField] private Vector2 soundVolumeRange = new Vector2(0.3f, 1.0f);

        [Header("Continuous Elevator Audio Settings")] [SerializeField]
        private AudioClip stageClickClip;

        [SerializeField] private float clickVolume = 0.1f;
        [SerializeField] private float clickSoundCooldown = 0.2f;
        [SerializeField] private float topClickOffset = 0.5f;
        [SerializeField] private float bottomClickOffset = 0.5f;

        [SerializeField] private Transform referenceTransform;

        private AudioSource[] _stageClickSources;

        // private float[] _initialStagePositionsInches;
        private float[] _lastStagePositions;
        private float[] _lastClickTimes;
        private bool[] _wasAtTop;

        private bool[] _wasAtBottom;
        // private float[] _lastMovement;
        // private float[] _lastPosAbove;

        private Vector3 _lastStagePosition;
        private float _continuousRealTargetHeight;

        [Serializable]
        public struct PerStageOverrides
        {
            public int stageNum;
            public JointAxis overrideAxis;
            public bool useStartingOffset;
        }

        private void Start()
        {
            for (var i = 0; i < stages.Length; i++)
            {
                if (i == stages.Length - 1 || elevatorType == ElevatorType.Cascade)
                {
                    stages[i].SetPid(pidConstants);
                }
                else
                {
                    stages[i].SetPid(stagesPidConstants);
                }
            }

            _carriageStartPosition = stages[0].transform.parent.InverseTransformPoint(stages[^1].transform.position);
            _lastAxis = elevatorAxis;
            _lastOffset = flipped;

            _lastStagePosition = stages[^1].transform.localPosition;

            if (elevatorSound != null && elevatorClip != null)
            {
                elevatorSound.clip = elevatorClip;
                elevatorSound.loop = true;
                elevatorSound.playOnAwake = false;
            }
            else
            {
                Debug.LogWarning("Elevator sound or clip not set.");
            }

            _stageClickSources = new AudioSource[stages.Length];
            _lastClickTimes = new float[stages.Length];
            _lastStagePositions = new float[stages.Length];
            _wasAtTop = new bool[stages.Length];
            _wasAtBottom = new bool[stages.Length];
            for (var i = 0; i < stages.Length; i++)
            {
                _stageClickSources[i] = stages[i].gameObject.AddComponent<AudioSource>();
                _stageClickSources[i].clip = stageClickClip;
                _stageClickSources[i].volume = clickVolume;
                _stageClickSources[i].playOnAwake = false;
                _stageClickSources[i].loop = false;

                _lastStagePositions[i] = stages[i].GetAxisLocation(elevatorAxis) * 39.3701f; // Convert to inches
                _lastClickTimes[i] = -clickSoundCooldown;
                _wasAtTop[i] = false;
                _wasAtBottom[i] = false;
            }

            InitClickPositions();
            PrecacheCombinedHeights();
        }

        private void LateUpdate()
        {
            for (var i = 0; i < stages.Length; i++)
            {
                if (i == stages.Length - 1 || elevatorType == ElevatorType.Cascade)
                {
                    stages[i].UpdatePid(pidConstants);
                }
                else
                {
                    stages[i].UpdatePid(stagesPidConstants);
                }
            }

            if (!Mathf.Approximately(stageHeight, _previousStageHeight) ||
                !Mathf.Approximately(stageOverlap, _previousStageOverlap) ||
                !Mathf.Approximately(carriageHeight, _previousCarriageHeight))
            {
                PrecacheCombinedHeights();
                _previousStageHeight = stageHeight;
                _previousStageOverlap = stageOverlap;
                _previousCarriageHeight = carriageHeight;
            }
        }

        private void FixedUpdate()
        {
            UpdateElevatorAudio();
            CheckContinuousElevatorClicks();
        }

        public MoSimResult<MoSimLib.Void> SetTarget(float target)
        {
            var version = MoSim.ActiveVersion;

            SetTargetV1(target);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetTargetV1(float target)
        {
            switch (elevatorType)
            {
                case ElevatorType.Cascade:
                    RunCascade(target);
                    break;
                case ElevatorType.Continuous:
                    RunContinuous(target);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void RunCascade(float target)
        {
            for (var i = stages.Length - 1; i >= 0; i--)
            {
                var useStartingOffset = false;
                var axis = elevatorAxis;
                for (var j = 0; j < setStageOverrides.Length; j++)
                {
                    if (setStageOverrides[j].stageNum - 1 != i) continue;
                    axis = setStageOverrides[j].overrideAxis;
                    useStartingOffset = setStageOverrides[j].useStartingOffset;
                }

                if (flipped)
                {
                    if (useStartingOffset)
                    {
                        stages[i].SetLinearTarget(target / stages.Length).withAxis(elevatorAxis)
                            .useDifferentEncoderAxis(axis).flipDirection().useAutomaticStartingOffset();
                    }
                    else
                    {
                        stages[i].SetLinearTarget(target / stages.Length).withAxis(elevatorAxis)
                            .useDifferentEncoderAxis(axis).flipDirection();
                    }
                }
                else
                {
                    if (useStartingOffset)
                    {
                        stages[i].SetLinearTarget(target / stages.Length).withAxis(elevatorAxis)
                            .useDifferentEncoderAxis(axis).useAutomaticStartingOffset();
                    }
                    else
                    {
                        stages[i].SetLinearTarget(target / stages.Length).withAxis(elevatorAxis)
                            .useDifferentEncoderAxis(axis);
                    }
                }
            }
        }

        public static MoSimResult<float> GetLocationOnAxis(JointAxis jointAxis, Vector3 setTransform)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetLocationOnAxisV1(jointAxis, setTransform));
        }

        private static float GetLocationOnAxisV1(JointAxis jointAxis, Vector3 setTransform)
        {
            return jointAxis switch
            {
                JointAxis.X => setTransform.x,
                JointAxis.Y => setTransform.y,
                JointAxis.Z => setTransform.z,
                _ => 0
            };
        }

        /// <summary>
        /// PrecacheCombinedHeights MUST be called before running this function.
        /// </summary>
        private void RunContinuous(float target)
        {
            float minStagePosition = transform.InverseTransformPoint(stages[^1].transform.position).y;

            for (int i = 0; i < stages.Length; i++)
            {
                float setPoint = 0;
                if (i == stages.Length - 1)
                {
                    setPoint = target;
                    setTarget(setPoint, i);
                    continue; // Skip follower calculations
                }

                float combinedHeight = _cachedCombinedHeights[i];

                if (combinedHeight < minStagePosition)
                {
                    setPoint = minStagePosition - combinedHeight;
                }

                setTarget(setPoint * 39.3701f, i);
            }
        }

        private float[] _cachedCombinedHeights;

        private void PrecacheCombinedHeights()
        {
            _cachedCombinedHeights = new float[stages.Length];

            for (int i = 0; i < stages.Length - 1; i++)
            {
                float combinedHeight = (-carriageHeight) * 0.0254f;

                for (int j = i; j < stages.Length - 1; j++)
                {
                    float retractionOffset = 0;

                    if (j < stages.Length - 2)
                    {
                        retractionOffset = (stageOverlap + ((stages.Length - j) * 2f)) * 0.0254f;
                    }

                    // Equivalent to: combinedHeight += (stageHeight * 0.0254f) - retractionOffset;
                    combinedHeight += (stageHeight * 0.0254f) - retractionOffset;
                }

                // Store the calculated combined height for stage 'i'
                _cachedCombinedHeights[i] = combinedHeight;
            }
        }

        private void setTarget(float target, int i)
        {
            var useStartingOffset = false;
            var axis = elevatorAxis;
            for (var j = 0; j < setStageOverrides.Length; j++)
            {
                if (setStageOverrides[j].stageNum - 1 != i) continue;
                axis = setStageOverrides[j].overrideAxis;
                useStartingOffset = setStageOverrides[j].useStartingOffset;
            }

            if (flipped)
            {
                if (useStartingOffset)
                {
                    stages[i].SetLinearTarget(target).withAxis(elevatorAxis).useDifferentEncoderAxis(axis)
                        .flipDirection().useAutomaticStartingOffset();
                }
                else
                {
                    stages[i].SetLinearTarget(target).withAxis(elevatorAxis).useDifferentEncoderAxis(axis)
                        .flipDirection();
                }
            }
            else
            {
                if (useStartingOffset)
                {
                    stages[i].SetLinearTarget(target).withAxis(elevatorAxis).useDifferentEncoderAxis(axis)
                        .useAutomaticStartingOffset();
                }
                else
                {
                    stages[i].SetLinearTarget(target).withAxis(elevatorAxis).useDifferentEncoderAxis(axis);
                }
            }
        }

        public MoSimResult<float> GetElevatorHeight()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetElevatorHeightV1());
        }

        private float GetElevatorHeightV1()
        {
            if (elevatorType == ElevatorType.Cascade)
            {
                if (stages.Length == 0) return 0f;

                var elevatorHeight = 0f;
                foreach (var stage in stages)
                {
                    elevatorHeight += stage.GetAxisLocation(elevatorAxis) * 39.3701f;
                    elevatorHeight -= stage.useStartingOffset ? stage._startingPosition.y * 39.3701f : 0f;
                }

                return elevatorHeight;
            }
            else
            {
                return stages[^1].GetAxisLocation(elevatorAxis) * 39.3701f -
                       (stages[^1].useStartingOffset ? stages[^1]._startingPosition.y * 39.3701f : 0f);
            }
        }

        public MoSimResult<float> GetContinuousTargetHeight(float targetHeight = 0f)
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<float>.Success(GetContinuousTargetHeightV1(targetHeight));
        }

        private float GetContinuousTargetHeightV1(float targetHeight = 0f)
        {
            if (elevatorType != ElevatorType.Continuous)
            {
                throw new InvalidOperationException("This method is only valid for continuous elevators.");
            }

            if (targetHeight == 0f) return _continuousRealTargetHeight;

            _continuousRealTargetHeight = 0f;

            for (var i = stages.Length - 1; i >= 0; i--)
            {
                var axis = elevatorAxis;
                for (var j = 0; j < setStageOverrides.Length; j++)
                {
                    if (setStageOverrides[j].stageNum - 1 != i) continue;
                    axis = setStageOverrides[j].overrideAxis;
                }

                var altAxis = axis;
                if (i == stages.Length - 1)
                {
                    foreach (var stageOverride in setStageOverrides)
                    {
                        if (stageOverride.stageNum - 1 == 0)
                        {
                            altAxis = stageOverride.overrideAxis;
                        }
                    }
                }

                float realTarget;
                if (i == stages.Length - 1)
                {
                    var axisPose = GetLocationOnAxis(altAxis, _carriageStartPosition) * 39.3701f;
                    var axisCPose = GetLocationOnAxis(altAxis,
                        stages[0].transform.parent.InverseTransformPoint(stages[^1].transform.position)) * 39.3701f;
                    if (axisCPose > targetHeight - axisPose - (stageHeight - stageOverlap) * (stages.Length - 1) ||
                        targetHeight < axisPose + (stageHeight - carriageHeight))
                    {
                        realTarget = Mathf.Min(targetHeight, stageHeight - carriageHeight);
                    }
                    else
                    {
                        realTarget = Mathf.Infinity - 1;
                    }
                }
                else
                {
                    var maxExtension = i + 2 == stages.Length
                        ? stageHeight - carriageHeight
                        : stageHeight - stageOverlap;
                    var adjustedTarget = targetHeight - ((maxExtension) * ((stages.Length - 1) - i));
                    var offset = _lastOffset
                        ? (GetLocationOnAxis(_lastAxis, stages[i + 1]._startingPosition) * 39.3701f)
                        : 0;
                    if (adjustedTarget > 0 &&
                        maxExtension - ((stages[i + 1].GetAxisLocation(_lastAxis) * 39.3701) - offset) < 2f)
                    {
                        realTarget = Mathf.Min(Mathf.Max(adjustedTarget, 0), stageHeight - stageOverlap);
                    }
                    else
                    {
                        realTarget = 0;
                    }
                }

                realTarget = Mathf.Max(realTarget, 0);

                _continuousRealTargetHeight += realTarget;

                _lastOffset = flipped;
                _lastAxis = axis;
            }

            return _continuousRealTargetHeight;
        }

        private void UpdateElevatorAudio()
        {
            if (elevatorSound == null || stages.Length == 0) return;

            var currentPosition = stages[^1].transform.localPosition;
            var distanceMoved = Vector3.Distance(_lastStagePosition, currentPosition);
            var speed = distanceMoved / Time.fixedDeltaTime * 39.3701f;
            _lastStagePosition = currentPosition;

            if (speed > minSpeed)
            {
                if (!elevatorSound.isPlaying)
                {
                    elevatorSound.Play();
                }

                var t = Mathf.Clamp01(speed / maxSpeed);
                elevatorSound.pitch = Mathf.Lerp(soundPitchRange.x, soundPitchRange.y, t);
                elevatorSound.volume = Mathf.Lerp(soundVolumeRange.x, soundVolumeRange.y, t);
            }
            else
            {
                if (elevatorSound.isPlaying)
                {
                    elevatorSound.Stop();
                }
            }
        }

        private void CheckContinuousElevatorClicks()
        {
            if (elevatorType != ElevatorType.Continuous || stageClickClip == null)
                return;

            for (var i = 0; i < stages.Length; i++)
            {
                // 1. Get the current, extended height of the *individual* stage (in inches)
                // You need a way to get the local extension height of stage[i]. 
                // This is often stages[i].transform.localPosition.y or similar, converted to inches.

                // **CRITICAL CHANGE HERE:** You must find the correct way to get the *local extension*
                // For example, if 'stages' holds the Transform components:
                var localStagePosition = stages[i].transform.localPosition.y * 39.3701f;

                var currentPos = localStagePosition; // Use the local extension height

                var movement = currentPos - _lastStagePositions[i]; // Compare current extension to last extension

                var movingUp = movement > 0.001f;
                var movingDown = movement < -0.001f;

                // travelDist should represent the *maximum extension* for this specific stage.
                // If _cachedCombinedHeights[i] holds the maximum extension of stage i from the stage below it, this is correct.
                var travelDist = i == stages.Length - 1 ? stageHeight - carriageHeight : stageHeight - stageOverlap;

                // Trigger positions relative to the stage's *local travel range*
                var topTriggerPos = travelDist - topClickOffset; // Near max extension
                var bottomTriggerPos = bottomClickOffset; // Near min extension (usually near 0)

                var hitTopTrigger = currentPos >= topTriggerPos;
                var hitBottomTrigger = currentPos <= bottomTriggerPos;

                var playClick = (movingUp && hitTopTrigger && !_wasAtTop[i]) ||
                                (movingDown && hitBottomTrigger && !_wasAtBottom[i]);

                if (playClick)
                {
                    if (Time.time - _lastClickTimes[i] > clickSoundCooldown)
                    {
                        _stageClickSources[i].Play();
                        _lastClickTimes[i] = Time.time;
                    }
                }

                _wasAtTop[i] = hitTopTrigger;
                _wasAtBottom[i] = hitBottomTrigger;
                _lastStagePositions[i] = currentPos; // Store the local extension height
            }
        }

        private void InitClickPositions()
        {
            if (referenceTransform == null && stages.Length > 0)
            {
                referenceTransform = stages[0].transform.parent;
            }

            _lastStagePositions = new float[stages.Length];
            _lastClickTimes = new float[stages.Length];

            for (var i = 0; i < stages.Length; i++)
            {
                var localPos = referenceTransform.InverseTransformPoint(stages[i].transform.position);
                var posInches = GetLocationOnAxis(elevatorAxis, localPos) * 39.3701f;

                if (flipped) posInches = -posInches;

                _lastStagePositions[i] = posInches;
                _lastClickTimes[i] = -clickSoundCooldown;
                _wasAtTop[i] = false;
                _wasAtBottom[i] = false;
            }
        }
    }
}
