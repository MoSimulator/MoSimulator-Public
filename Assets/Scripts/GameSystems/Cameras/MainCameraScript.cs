using MoSimCore.Enums;
using Unity.Cinemachine;
using UnityEngine;

namespace GameSystems.Cameras
{
    public class MainCameraScript : MonoBehaviour
    {
        [SerializeField] protected Camera Camera;

        [SerializeField] private AudioListener listener;

        [SerializeField] private CinemachineBrain brain;

        private void Start()
        {
            Camera = GetComponent<Camera>();
        }

        // private void OnEnable()
        // {
        //     if (listener == null) listener = GetComponent<AudioListener>();
        //     if (listener != null) listener.enabled = false;
        // }

        // private void OnDisable()
        // {
        //     if (listener == null) listener = GetComponent<AudioListener>();
        //     if (listener != null) listener.enabled = false;
        // }

        // public void EnableListener()
        // {
        //     if (listener == null) listener = GetComponent<AudioListener>();
        //     if (listener != null) listener.enabled = true;
        // }

        /// <summary>
        /// Restricts this camera's Cinemachine brain to a single output channel so that, in
        /// split-screen, each brain only evaluates and drives its own side's virtual cameras
        /// instead of every active vcam in the scene.
        /// </summary>
        /// <param name="channel">The channel this brain should listen to.</param>
        public void SetOutputChannel(OutputChannels channel)
        {
            if (brain == null && !TryGetComponent(out brain))
            {
                Debug.LogError("MainCameraScript: CinemachineBrain not found; cannot isolate split-screen channel.");
                return;
            }

            brain.ChannelMask = channel;
        }

        public void SetSplitScreen(Alliance alliance)
        {
            if (Camera == null)
            {
                Debug.LogError("Camera component not found on this GameObject.");
                return;
            }

            if (Camera != null)
            {
                Camera.rect = new Rect(alliance == Alliance.Blue ? -0.5f : 0.5f, 0f, 1f, 1f);
            }

            if (listener == null)
            {
                listener = GetComponent<AudioListener>();
            }

            if (listener != null)
            {
                // The scene-default listener on RobotSpawnController is the only one
                // that should ever be live — camera listeners stay off.
                listener.enabled = false;
            }
            else
            {
                Debug.LogError("AudioListener component not found on this GameObject.");
            }
        }
    }
}