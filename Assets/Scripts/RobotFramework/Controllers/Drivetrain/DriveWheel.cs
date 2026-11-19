using UnityEngine;

namespace RobotFramework.Controllers.Drivetrain
{
    /// <summary>
    /// A wheel in the robot's drivetrain.
    /// Adding this to a wheel collider under a DriveController's Drivetrain
    /// Parent game object will include it in the robot's drivetrain.
    /// </summary>
    public class DriveWheel : MonoBehaviour
    {
        /// <summary>
        /// The angle of the wheel in degrees.
        /// </summary>
        public float WheelAngle;

        [Tooltip("Passive rotational damping of the wheel collider. Nonzero values add rolling resistance on top of DriveController's motor model and chassis drag.")]
        [SerializeField, Min(0f)] private float wheelDampingRate = 0f;

        private WheelCollider _wheelCollider;
        private DriveController _controller;
        private bool _customTraction;
        private WheelFrictionCurve _nativeForwardFriction;
        private WheelFrictionCurve _nativeSidewaysFriction;

        public WheelCollider Collider
        {
            get
            {
                if (_wheelCollider == null) _wheelCollider = GetComponent<WheelCollider>();
                return _wheelCollider;
            }
        }

        // Keep native suspension/support, but let DriveController own all tread forces.
        public void SetCustomTraction(bool enabled)
        {
            var collider = Collider;
            if (collider == null || _customTraction == enabled) return;
            if (enabled)
            {
                _nativeForwardFriction = collider.forwardFriction;
                _nativeSidewaysFriction = collider.sidewaysFriction;
                var forward = _nativeForwardFriction;
                var sideways = _nativeSidewaysFriction;
                forward.stiffness = 0f;
                sideways.stiffness = 0f;
                collider.forwardFriction = forward;
                collider.sidewaysFriction = sideways;
            }
            else
            {
                collider.forwardFriction = _nativeForwardFriction;
                collider.sidewaysFriction = _nativeSidewaysFriction;
            }
            _customTraction = enabled;
        }

        private void OnDisable()
        {
            SetCustomTraction(false);
        }

        private void FixedUpdate()
        {
            if (_controller == null) _controller = GetComponentInParent<DriveController>();
            if (_controller != null && !_controller.isActiveAndEnabled)
                _controller.UpdatePassiveTraction();
        }

        private void Update()
        {
            if (Collider == null) return;
            if (WheelAngle < 0) WheelAngle = -WheelAngle + 180;
            if (WheelAngle < 0) WheelAngle = -WheelAngle + 180;
            if (WheelAngle > 360) WheelAngle -= 360;
            if (WheelAngle > 360) WheelAngle -= 360;

            _wheelCollider.steerAngle = WheelAngle;
            // Propulsion is applied to the chassis by DriveController. Let the
            // passive collider roll freely unless extra rolling resistance is requested.
            _wheelCollider.wheelDampingRate = Mathf.Max(0f, wheelDampingRate);
            _wheelCollider.brakeTorque = 0.0000000f;
            _wheelCollider.motorTorque = 0.000000000000000000000000000001f;
        }
    }
}
