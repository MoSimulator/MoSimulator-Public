using MoSimCore.BaseClasses;
using Unity.Cinemachine;
using UnityEngine;

namespace GameSystems.Cameras
{
    public class ThirdPersonVCam : BaseVCamScript
    {
        private void Update()
        {
            if (TargetRobot != null && Vcam != null && Vcam.Follow != TargetRobot)
            {
                Vcam.Follow = TargetRobot;
                Vcam.LookAt = TargetRobot;
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

            Vcam.Follow = target;
            Vcam.LookAt = target;
        }
    }
}