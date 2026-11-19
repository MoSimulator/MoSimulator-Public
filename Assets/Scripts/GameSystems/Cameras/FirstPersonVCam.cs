using MoSimCore.BaseClasses;
using Unity.Cinemachine;
using UnityEngine;

namespace GameSystems.Cameras
{
    public class FirstPersonVCam : BaseVCamScript
    {
        public CinemachineThirdPersonFollow Follow { get; private set; }

        private bool _isFlipped;
        public override bool IsFlipped => _isFlipped;

        private Transform _stabilizedTarget;

        private void Start()
        {
            Follow = GetComponent<CinemachineThirdPersonFollow>();
            Follow.CameraDistance += 0.25f;
            Follow.VerticalArmLength += 0.25f;

            _stabilizedTarget = new GameObject("FP_StabilizedTarget").transform;
        }

        private void OnDestroy()
        {
            if (_stabilizedTarget != null)
                Destroy(_stabilizedTarget.gameObject);
        }

        private void Update()
        {
            if (TargetRobot == null || _stabilizedTarget == null) return;

            _stabilizedTarget.position = TargetRobot.position;
            var fwd = TargetRobot.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f)
                _stabilizedTarget.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

            if (Vcam != null && Vcam.Follow != _stabilizedTarget)
            {
                Vcam.Follow = _stabilizedTarget;
                Vcam.LookAt = _stabilizedTarget;
            }
        }

        public override void SetCameraTarget(Transform target)
        {
            if (target == null)
            {
                Debug.LogError("Target is null");
                return;
            }
            TargetRobot = target;
            if (Vcam == null)
            {
                Debug.LogError("Virtual Camera is not assigned.");
                Vcam = GetComponent<CinemachineCamera>();
                if (Vcam == null)
                {
                    Debug.LogError("CinemachineCamera component not found on this GameObject.");
                    return;
                }
            }
            Vcam.Follow = _stabilizedTarget != null ? _stabilizedTarget : target;
            Vcam.LookAt = _stabilizedTarget != null ? _stabilizedTarget : target;
        }

        public override void FlipCamera()
        {
            if (Follow == null) return;
            _isFlipped = !_isFlipped;
            var s = Follow.ShoulderOffset;
            s.z = _isFlipped ? 2f * Follow.CameraDistance : 0f;
            Follow.ShoulderOffset = s;
        }
    }
}
