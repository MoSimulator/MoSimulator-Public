using System;
using System.Collections.Generic;
using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.Robots;
using RobotFramework.Controllers.Drivetrain;
using UnityEngine;

namespace Games.Reefscape.Components
{
    public class ReefscapeAutoAlign : AutoAlign
    {
        [Header("Offsets")]
        [Tooltip("Use pipe-centered offsets like V25. Uses Legacy Offset instead of the reef-centered X and Y values below.")]
        [SerializeField] private bool useLegacyOffsets = false;
        [Tooltip("Offset from the selected pipe: X is forward, Y is sideways (meters), Theta is rotation (degrees). Zero centers the robot on the pipe. Sideways offsets are not mirrored between pipes; directions follow the pipe even when aligning backwards.")]
        [SerializeField] private Offset legacyOffset = DEFAULT_OFFSET;
        [Tooltip("The offset in the X (forward/reverse) direction from the reef's center for the robot to target (Always positive)")]
        [SerializeField] protected float x = 1.5f;
        [Tooltip("The offset in the Y (strafe) direction from the reef's center for the robot to target (Always positive)")]
        [SerializeField] protected float y = 0.17f;
        [Tooltip("The tolerance at which the controller signals that the robot is in position")]
        [SerializeField] protected float positionTolerance = 0.05f;

        [Header("Auto Align Direction")]
        [Tooltip("Enable forward auto align (when facing the reef)")]
        [SerializeField] public bool enableForwardAlign = true;
        [Tooltip("Enable backwards auto align (when facing away from the reef)")]
        [SerializeField] public bool enableBackwardsAlign = true;
        [Tooltip("The control strategy to use for aiming the robot")]
        [SerializeField] private AimControl aimControl = AimControl.Profiled;

        public bool Active { get; protected set; } = false;
        public float ReefDistance { get; protected set; } = 0f;

        /// <summary>
        /// Whether alignment uses pipe-centered legacy offsets.
        /// </summary>
        public bool UseLegacyOffsets
        {
            get => useLegacyOffsets;
            set => useLegacyOffsets = value;
        }

        /// <summary>
        /// Base pipe-relative offset used when useLegacyOffsets is enabled.
        /// X is forward and Y is sideways in meters; Theta is in degrees.
        /// SetOffset adjustments are added to this value.
        /// </summary>
        public Offset LegacyOffset
        {
            get => legacyOffset;
            set => legacyOffset = value;
        }

        protected Offset _offset = DEFAULT_OFFSET;
        protected bool _enableLeadIn = true;

        protected ReefscapeRobotBase _reefBase;
        protected ReefscapeCommandRobotBase _commandBase;
        protected Vector2 _blueReef;
        protected Vector2 _redReef;

        private readonly List<AlignNode> _targetNodes = new();
        private readonly Dictionary<Transform, AlignNode> _parentLookup = new();
        private readonly Transform[] _candidates = new Transform[4];
        private Transform _closestNode;
        private Transform _secondClosestNode;
        private bool _hasTarget;
        private readonly Queue<PrepPoint> _prepPoints = new();
        private readonly Queue<PrepPoint> _queuedPrepPoints = new();
        private bool _wasAligning;
        private bool _followingPrepPoint;
        private Transform _alignmentNode;
        private bool _alignmentLeft;
        private bool _alignmentPerspective;
        private bool _alignmentForwards;

        // Face numbering to pole indices in Reef.Scored(), from 25.3.1's AligningToNode.
        private static readonly int[][] Faces =
        {
            new[] { 6, 7 },
            new[] { 8, 9 },
            new[] { 10, 11 },
            new[] { 0, 1 },
            new[] { 2, 3 },
            new[] { 4, 5 }
        };

        protected override void Start()
        {
            base.Start();

            _reefBase = GetComponent<ReefscapeRobotBase>();
            _commandBase = GetComponent<ReefscapeCommandRobotBase>();

            _blueReef = Vec3ToVec2(GameObject.Find("BlueReef").transform.position);
            _redReef = Vec3ToVec2(GameObject.Find("RedReef").transform.position);

            foreach (var face in GameObject.FindGameObjectsWithTag("ReefFace"))
            {
                if (!face.TryGetComponent<AlignNode>(out var node) || node.LeftNode == null || node.RightNode == null)
                {
                    continue;
                }

                _targetNodes.Add(node);
                _parentLookup.TryAdd(node.LeftNode.transform, node);
                _parentLookup.TryAdd(node.RightNode.transform, node);
            }
        }

        private void Update()
        {
            if (_reefBase == null && _commandBase == null) return;

            bool triggered = _reefBase != null
                ? _reefBase.AutoAlignLeftAction.triggered || _reefBase.AutoAlignRightAction.triggered
                : _commandBase.AutoAlignLeftAction.WasTriggered() || _commandBase.AutoAlignRightAction.WasTriggered();

            if (triggered)
            {
                CacheClosestNodes();
            }
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            _hasTarget = false;
            _followingPrepPoint = false;

            if (_rb == null || (_reefBase == null && _commandBase == null))
            {
                Debug.LogError("Initialization error");
                return;
            }

            RefreshAlignmentTarget();
            HandlePrepPointQueue();

            if (Active)
            {
                var pos = Vec3ToVec2(_rb.transform.position);

                float blueDistance = (_blueReef - pos).magnitude;
                float redDistance = (_redReef - pos).magnitude;
                ReefDistance = Mathf.Min(blueDistance, redDistance);

                var node = _alignmentNode;
                if (node == null) return;

                var nodePosition = Vec3ToVec2(node.position);
                var reef = (_blueReef - nodePosition).sqrMagnitude < (_redReef - nodePosition).sqrMagnitude ? _blueReef : _redReef;
                bool left = _parentLookup[node].LeftNode.transform == node;
                float rk_w = (90f - node.eulerAngles.y) * Mathf.Deg2Rad;

                if (!enableForwardAlign && !enableBackwardsAlign)
                {
                    Debug.LogWarning("Both forwards and backwards alignment is disabled. Defaulting to forward alignment");
                }

                bool forwards = _alignmentForwards;

                var pole = Pole(rk_w, left, reef);
                float heading = (-rk_w * Mathf.Rad2Deg) - (forwards ? 270f : 90f);
                float targetHeading = heading + _offset.theta;
                if (useLegacyOffsets)
                {
                    pole = Vec3ToVec2(node.position + node.rotation * new Vector3(
                        legacyOffset.y + _offset.y, 0f, legacyOffset.x + _offset.x));
                    targetHeading += legacyOffset.theta;
                }
                _followingPrepPoint = _queuedPrepPoints.TryPeek(out var prepPoint);
                if (_followingPrepPoint)
                {
                    pole = Vec3ToVec2(node.position + node.rotation * prepPoint.Position);
                    targetHeading = heading + prepPoint.Rotation;
                }

                AlignPosition(pole.x, pole.y, targetHeading, aimControl);
                _hasTarget = true;

                if (_followingPrepPoint && GetPositionError() <= prepPoint.ClearDistance
                    && Mathf.Abs(GetRotationError()) <= prepPoint.FinalAngleTolerance
                    && (!prepPoint.RequireAtSetpoint || AtPrepPointSetpoint()))
                {
                    _queuedPrepPoints.Dequeue();
                }
            }
        }

        private void RefreshAlignmentTarget()
        {
            if (_rb == null || (_reefBase == null && _commandBase == null))
            {
                Active = false;
                _alignmentNode = null;
                return;
            }

            bool left = _reefBase != null ? _reefBase.AutoAlignLeftAction.IsPressed() : _commandBase.AutoAlignLeftAction.IsPressed();
            bool right = _reefBase != null ? _reefBase.AutoAlignRightAction.IsPressed() : _commandBase.AutoAlignRightAction.IsPressed();
            bool wasActive = Active;
            Active = left || right;
            if (!Active)
            {
                _alignmentNode = null;
                return;
            }

            // Queries can run before this component's Update/FixedUpdate on the first press.
            if (!wasActive) CacheClosestNodes();

            bool perspective = _reefBase != null ? _reefBase.PerspectiveAutoAlign : _commandBase.PerspectiveAutoAlign;
            if (_alignmentNode != null && _alignmentLeft == left && _alignmentPerspective == perspective) return;

            var camera = (_reefBase != null ? _reefBase.GetActiveCamera() : _commandBase.GetActiveCamera()).Value;
            // Movement and score queries share this locked target, including perspective selection.
            _alignmentNode = SelectNode(left, perspective, camera);
            _alignmentLeft = left;
            _alignmentPerspective = perspective;
            if (_alignmentNode == null) return;

            float rk_w = (90f - _alignmentNode.eulerAngles.y) * Mathf.Deg2Rad;
            bool flip = Mathf.Abs(((-rk_w * Mathf.Rad2Deg) - _rb.rotation.eulerAngles.y + 450f) % 360f - 180f) > 90f;
            _alignmentForwards = enableForwardAlign && enableBackwardsAlign ? flip : !enableBackwardsAlign;
        }

        private void HandlePrepPointQueue()
        {
            if (!Active || !_enableLeadIn)
            {
                _queuedPrepPoints.Clear();
            }
            else if (!_wasAligning)
            {
                _queuedPrepPoints.Clear();
                foreach (var point in _prepPoints)
                {
                    _queuedPrepPoints.Enqueue(point);
                }
            }

            _wasAligning = Active;
        }

        private bool AtPrepPointSetpoint()
        {
            return _reefBase != null
                ? _reefBase.AtSetpoint() && _reefBase.CurrentSetpoint != ReefscapeSetpoints.Stow
                : _commandBase.AtSetpoint() && !_commandBase.Stowed();
        }

        private void CacheClosestNodes()
        {
            AlignNode closestFace = null;
            AlignNode secondClosestFace = null;
            float closestDistance = float.MaxValue;
            float secondClosestDistance = float.MaxValue;

            foreach (var face in _targetNodes)
            {
                if (face == null || face.LeftNode == null || face.RightNode == null) continue;

                float distance = Vector3.Distance(transform.position, face.transform.position);
                if (distance < closestDistance)
                {
                    secondClosestFace = closestFace;
                    secondClosestDistance = closestDistance;
                    closestFace = face;
                    closestDistance = distance;
                }
                else if (distance < secondClosestDistance)
                {
                    secondClosestFace = face;
                    secondClosestDistance = distance;
                }
            }

            _closestNode = null;
            _secondClosestNode = null;
            if (closestFace == null || secondClosestFace == null) return;

            _candidates[0] = closestFace.LeftNode.transform;
            _candidates[1] = closestFace.RightNode.transform;
            _candidates[2] = secondClosestFace.LeftNode.transform;
            _candidates[3] = secondClosestFace.RightNode.transform;
            closestDistance = float.MaxValue;
            secondClosestDistance = float.MaxValue;

            // Match 25.3.1: rank the four nodes once when alignment is pressed.
            foreach (var node in _candidates)
            {
                float distance = Vector3.Distance(transform.position, node.position);
                if (distance < closestDistance)
                {
                    _secondClosestNode = _closestNode;
                    secondClosestDistance = closestDistance;
                    _closestNode = node;
                    closestDistance = distance;
                }
                else if (distance < secondClosestDistance)
                {
                    _secondClosestNode = node;
                    secondClosestDistance = distance;
                }
            }
        }

        private Transform SelectNode(bool alignLeft, bool perspective, GameObject camera)
        {
            if (perspective && camera == null) return null;

            if (IsRequestedNode(_closestNode, alignLeft, perspective, camera)) return _closestNode;
            if (IsRequestedNode(_secondClosestNode, alignLeft, perspective, camera)) return _secondClosestNode;

            if (perspective && _closestNode != null && _parentLookup.TryGetValue(_closestNode, out var face) && face != null)
            {
                if (IsRequestedNode(face.LeftNode.transform, alignLeft, true, camera)) return face.LeftNode.transform;
                if (IsRequestedNode(face.RightNode.transform, alignLeft, true, camera)) return face.RightNode.transform;
            }

            return null;
        }

        private bool IsRequestedNode(Transform node, bool alignLeft, bool perspective, GameObject camera)
        {
            if (node == null || !_parentLookup.TryGetValue(node, out var face) || face == null) return false;

            bool cameraFacesNode = perspective && Vector3.Dot(camera.transform.forward, face.transform.forward) > 0f;
            bool left = perspective ? alignLeft != cameraFacesNode : alignLeft;
            return (left ? face.LeftNode : face.RightNode) == node.gameObject;
        }

        /// <summary>
        /// Returns the aligned pole's scored heights in L4, L3, L2 order, or null without a target.
        /// </summary>
        public bool[] GetScoredOnAligningNode()
        {
            RefreshAlignmentTarget();
            if (_alignmentNode == null || !_parentLookup.TryGetValue(_alignmentNode, out var face) || face == null)
                return null;

            var faceTransform = face.transform.parent;
            if (faceTransform == null) return null;

            int faceNumber = ExtractDigits(faceTransform.name) ?? 0;
            if (faceNumber < 0 || faceNumber >= Faces.Length) return null;

            int side = face.LeftNode.transform == _alignmentNode ? 0 : 1;
            var reef = face.GetComponentInParent<Reef>();
            if (reef == null) return null;

            return reef.Scored()[Faces[faceNumber][side]];
        }

        /// <summary>Returns whether the aligned face has high algae.</summary>
        public bool GetNodeAlgaeHigh()
        {
            RefreshAlignmentTarget();
            return _alignmentNode != null && _parentLookup.TryGetValue(_alignmentNode, out var face)
                && face != null && !face.LowAlgae;
        }

        /// <summary>
        /// Returns whether the locked alignment target is the right pole of its reef face.
        /// </summary>
        public bool IsRightFace()
        {
            RefreshAlignmentTarget();
            return _alignmentNode != null && _parentLookup.TryGetValue(_alignmentNode, out var face)
                && face != null && face.RightNode != null && face.RightNode.transform == _alignmentNode;
        }

        private static int? ExtractDigits(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;

            Span<char> buffer = stackalloc char[input.Length];
            int count = 0;
            foreach (char c in input)
            {
                if (c >= '0' && c <= '9') buffer[count++] = c;
            }

            return count > 0 && int.TryParse(buffer[..count], out int result) ? result : (int?)null;
        }

        /// <summary>
        /// Returns <c>true</c> if the auto align controller is active and the robot is in position.
        /// </summary>
        public bool InPosition()
        {
            return Active && _hasTarget && !_followingPrepPoint && GetPositionError() <= positionTolerance;
        }

        /// <summary>
        /// Adds a node-relative approach point to visit before the final target.
        /// Points are visited in insertion order and replayed at the start of each alignment attempt.
        /// </summary>
        /// <param name="position">Offset in the selected node's local axes, in inches.</param>
        /// <param name="rotation">Heading offset in degrees.</param>
        /// <param name="clearDistance">Distance from this point at which to advance, in inches.</param>
        /// <param name="finalAngleTolerance">Maximum absolute heading error at which to advance, in degrees.</param>
        /// <param name="requireAtSetpoint">Also wait for the robot's mechanism setpoint, excluding stow.</param>
        public void AddPrepPoint(Vector3 position, float rotation, float clearDistance, float finalAngleTolerance, bool requireAtSetpoint)
        {
            _prepPoints.Enqueue(new PrepPoint(position * INCH_TO_METERS, rotation, clearDistance * INCH_TO_METERS,
                finalAngleTolerance, requireAtSetpoint));
        }

        /// <summary>
        /// Sets the offset for the controller to apply from the default target position.
        /// </summary>
        /// <param name="x">X (forward/reverse) offset in meters.</param>
        /// <param name="y">Y (strafe) offset in meters.</param>
        /// <param name="theta">Rotational offset in degrees.</param>
        public void SetOffset(float x, float y, float theta)
        {
            SetOffset(new Offset(x, y, theta));
        }

        /// <summary>
        /// Sets the offset for the controller to apply from the default target position.
        /// </summary>
        /// <param name="offset">The offset to apply.</param>
        public void SetOffset(Offset offset)
        {
            _offset = offset;
        }

        /// <summary>
        /// Resets the offset applied to the default target position.
        /// </summary>
        public void ResetOffset()
        {
            _offset = DEFAULT_OFFSET;
        }

        /// <summary>
        /// Enables/disables queued approach points. Enabled by default.
        /// Disabling skips remaining points; enabling takes effect on the next alignment attempt.
        /// </summary>
        /// <param name="enable">If lead-in functionality should be enabled.</param>
        public void EnableLeadIn(bool enable)
        {
            _enableLeadIn = enable;
        }

        protected Vector2 Pole(float angle, bool left, Vector2 reef)
        {
            float r_x = -(x + _offset.x);
            float r_y = (y + _offset.y) * (left ? 1f : -1f);

            return reef + Rotate2(new Vector2(r_x, r_y), angle);
        }

        protected Vector2 Rotate2(Vector2 v, float radians)
        {
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        protected Vector2 Vec3ToVec2(Vector3 vec3)
        {
            return new Vector2(vec3.x, vec3.z);
        }

        private readonly struct PrepPoint
        {
            public readonly Vector3 Position;
            public readonly float Rotation;
            public readonly float ClearDistance;
            public readonly float FinalAngleTolerance;
            public readonly bool RequireAtSetpoint;

            public PrepPoint(Vector3 position, float rotation, float clearDistance, float finalAngleTolerance, bool requireAtSetpoint)
            {
                Position = position;
                Rotation = rotation;
                ClearDistance = clearDistance;
                FinalAngleTolerance = finalAngleTolerance;
                RequireAtSetpoint = requireAtSetpoint;
            }
        }

        /// <summary>
        /// Represents an offset from a target reef position.
        /// </summary>
        [Serializable]
        public struct Offset
        {
            /// <summary>
            /// X (forward/reverse) offset in meters.
            /// </summary>
            public float x;
            /// <summary>
            /// Y (strafe) offset in meters.
            /// </summary>
            public float y;
            /// <summary>
            /// Rotational offset in degrees.
            /// </summary>
            public float theta;

            /// <summary>
            /// Creates a new offset.
            /// </summary>
            /// <param name="x">X (forward/reverse) offset in meters.</param>
            /// <param name="y">Y (strafe) offset in meters.</param>
            /// <param name="theta">Rotational offset in degrees.</param>
            public Offset(float x, float y, float theta)
            {
                this.x = x;
                this.y = y;
                this.theta = theta;
            }

            /// <summary>
            /// Creates a new offset from Vec3.
            /// </summary>
            /// <param name="vec3">X (forward/reverse) offset (l/r, theta, f/b) in meters.</param>
            public Offset(Vector3 vec3)
            {
                this.x = vec3.z;
                this.y = vec3.x;
                this.theta = vec3.y;
            }
        }

        protected const float SIXTH_PI = Mathf.PI / 6f;
        protected const float THIRD_PI = Mathf.PI / 3f;
        protected const float HALF_PI = Mathf.PI / 2f;
        private const float INCH_TO_METERS = 0.0254f;
        protected static readonly Offset DEFAULT_OFFSET = new();
    }
}
