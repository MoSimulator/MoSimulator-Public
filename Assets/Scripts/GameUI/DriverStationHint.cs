using GameSystems.Cameras;
using GameSystems.Management;
using GameSystems.Settings;
using RobotFramework;
using UnityEngine;

namespace GameUI
{
    /// <summary>
    /// Keeps a hint only for a match watched from the driver station, the one view where the camera
    /// can be moved and nothing on screen says how.
    /// </summary>
    public class DriverStationHint : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Shown only when the camera binding has a button to hold alongside it - holding the " +
                 "right stick in, in the games that ask for that.")]
        private GameObject[] modifierParts;

        private void Awake()
        {
            if (!IsDriverStationView())
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>Waits for a robot to spawn, since the binding to describe comes with it.</summary>
        private void Update()
        {
            var robot = FindFirstObjectByType<RobotBase>();
            var action = robot != null ? robot.DriverStationMovementAction : null;
            if (action == null)
            {
                return;
            }

            var needsModifier = false;
            foreach (var binding in action.bindings)
            {
                needsModifier |= binding.isPartOfComposite && binding.name == "modifier";
            }

            foreach (var part in modifierParts)
            {
                part.SetActive(needsModifier);
            }

            enabled = false;
        }

        private static bool IsDriverStationView()
        {
            var match = MatchManager.Instance;
            if (match == null)
            {
                return false;
            }

            var players = match.MatchSettings != null && match.MatchSettings.gameMode == GameMode.LocalMultiplayer
                ? 2
                : 1;

            for (var i = 0; i < players; i++)
            {
                if (match.GetOrCreatePlayerSettings(i).cameraMode is CameraMode.DriverStationStatic
                    or CameraMode.DriverStationDynamic)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
