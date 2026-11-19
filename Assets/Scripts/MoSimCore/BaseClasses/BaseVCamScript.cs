using MoSimCore.Interfaces;
using Unity.Cinemachine;
using UnityEngine;

namespace MoSimCore.BaseClasses
{
    /// <summary>
    /// Abstract base class for robot camera controllers using Cinemachine.
    /// Provides common camera functionality for following and viewing robots.
    /// </summary>
    public abstract class BaseVCamScript : MonoBehaviour, IRobotCamera
    {
        /// <summary>Gets the Cinemachine virtual camera component.</summary>
        protected CinemachineCamera Vcam { get; set; }

        /// <summary>Gets the camera GameObject.</summary>
        public GameObject CameraObject => gameObject;

        /// <summary>Gets the camera's transform.</summary>
        public Transform Transform => transform;
        public virtual bool IsFlipped => false;
        public virtual void SetTranslationValue(Vector2 value) { }

        /// <summary>The transform of the robot currently being tracked by this camera.</summary>
        protected Transform TargetRobot;

        /// <summary>
        /// Initializes the Cinemachine camera component.
        /// </summary>
        protected void Awake()
        {
            Vcam = GetComponent<CinemachineCamera>();
        }

        /// <summary>
        /// Sets the target robot for the camera to follow.
        /// Implement this to configure camera tracking behavior.
        /// </summary>
        /// <param name="target">The transform of the target robot.</param>
        public abstract void SetCameraTarget(Transform target);

        /// <summary>
        /// Rotates the camera 180 degrees around the Y axis for alliance-swapped views.
        /// Override to customize flip behavior.
        /// </summary>
        public virtual void FlipCamera()
        {
            transform.Rotate(0f, 180f, 0f, Space.World);
        }

        /// <summary>
        /// Sets the vertical field of view on every virtual camera on this rig. Reaches child cameras
        /// for the same reason <see cref="SetOutputChannel"/> does: the driver station rig keeps its
        /// camera on a child, so the component is not on the camera it is setting.
        /// </summary>
        /// <param name="degrees">Vertical field of view.</param>
        public void SetFieldOfView(float degrees)
        {
            foreach (var cam in GetComponentsInChildren<CinemachineCamera>(true))
            {
                var lens = cam.Lens;
                lens.FieldOfView = degrees;
                cam.Lens = lens;
            }
        }

        /// <summary>
        /// Assigns the Cinemachine output channel for every virtual camera on this rig
        /// (including any nested child cameras, e.g. the driver station rig). Used to
        /// isolate each split-screen brain so it only evaluates its own side's cameras.
        /// </summary>
        /// <param name="channel">The output channel this rig should drive.</param>
        public void SetOutputChannel(OutputChannels channel)
        {
            var cams = GetComponentsInChildren<CinemachineCamera>(true);
            foreach (var cam in cams)
            {
                cam.OutputChannel = channel;
            }
        }
    }
}
