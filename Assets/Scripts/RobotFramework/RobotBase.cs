using System;
using System.Collections;
using System.Linq;
using MoSimCore.BaseClasses;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimCore.Settings;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.Drivetrain;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.Lighting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Users;

namespace RobotFramework
{
    /// <summary>
    /// Abstract base class for all robots in the game.
    /// Handles alliance assignment, camera switching, input mapping, and controller feedback.
    /// Inherit from this class to create game-specific robot implementations.
    /// </summary>
    public abstract class RobotBase : MonoBehaviour
    {
        [Header("Alliance & Appearance")]
        [SerializeField]
        [Tooltip("The alliance this robot belongs to (blue or red).")]
        private Alliance alliance;

        /// <summary>Gets the current alliance of this robot.</summary>
        public Alliance Alliance => alliance;

        /// <summary>
        /// Changes the robot's alliance and updates visual materials accordingly.
        /// </summary>
        /// <param name="newAlliance">The alliance to switch to.</param>
        public MoSimResult<MoSimLib.Void> SetAlliance(Alliance newAlliance)
        {
            var version = MoSim.ActiveVersion;

            SetAllianceV1(newAlliance);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetAllianceV1(Alliance newAlliance)
        {
            alliance = newAlliance;
            //ApplyAllianceVisuals();
        }

        public int TeamNumber { get; set; }

        public int PlayerIndex { get; set; } = -1;

        public string PlayerPrefix { get; set; } = "";

        public float MoveSpeedMultiplier { get; set; } = 1f;

        public float MoveSpeedExponent { get; set; } = 2f;

        public float BrakeTime { get; set; } = 0f;

        public float RotationSpeedMultiplier { get; set; } = 0.6f;

        public float ControllerRumbleStrength { get; set; } = 0.5f;

        public bool ControllerRumbleEnabled { get; set; } = true;

        public bool PerspectiveAutoAlign { get; set; } = true;

        /// <summary>Gets or sets whether the robot uses field-centric or robot-centric movement.</summary>
        public bool IsFieldCentric { get; set; } = true;

        /// <summary>Gets or sets the third-person view camera.</summary>
        public IRobotCamera ThirdPersonCam { get; set; }

        /// <summary>Gets or sets the first-person view camera.</summary>
        public IRobotCamera FirstPersonCam { get; set; }

        /// <summary>Gets or sets the driver station view camera.</summary>
        public IRobotCamera DriverStationCam { get; set; }

        /// <summary>Gets the currently active camera GameObject.</summary>
        public MoSimResult<GameObject> GetActiveCamera()
        {
            var version = MoSim.ActiveVersion;

            return MoSimResult<GameObject>.Success(GetActiveCameraV1());
        }

        private GameObject GetActiveCameraV1() =>
            ThirdPersonCam != null ? ThirdPersonCam.CameraObject :
            FirstPersonCam != null ? FirstPersonCam.CameraObject :
            DriverStationCam?.CameraObject;

        public bool IsCameraFlipped =>
            FirstPersonCam?.CameraObject?.GetComponent<BaseVCamScript>()?.IsFlipped ?? false;

        protected RobotGamePieceControllerBase RobotGamePieceControllerBase { get; private set; }
        protected DriveController DriveController { get; private set; }

        /// <summary>Gets or sets the game piece preloaded on this robot.</summary>
        public GameObject PreloadGamePiece { get; set; }

        private PlayerInput _playerInput;
        protected InputActionMap InputActionMap;

        /// <summary>Gets the translate/movement input action.</summary>
        public InputAction TranslateAction { get; private set; }

        /// <summary>Gets the rotate/turn input action.</summary>
        public InputAction RotateAction { get; private set; }

        /// <summary>Gets the swap camera input action.</summary>
        public InputAction SwapCameraAction { get; private set; }

        /// <summary>Gets the driver station movement input action.</summary>
        public InputAction DriverStationMovementAction { get; private set; }

        /// <summary>Gets the right stick modifier input action.</summary>
        public InputAction RightStickModifierAction { get; private set; }

        /// <summary>Gets the restart/reset input action.</summary>
        public InputAction RestartAction { get; private set; }

        /// <summary>Gets the pause input action.</summary>
        public InputAction PauseAction { get; private set; }

        /// <summary>Gets the drive-speed action keys (slow/precision driving caps).</summary>
        public InputAction DriveSpeed1Action { get; private set; }
        public InputAction DriveSpeed2Action { get; private set; }
        public InputAction DriveSpeed3Action { get; private set; }

        public PlayerDevice SelectedDevice { get; set; } = PlayerDevice.Keyboard;
        
        [Header("Default Spawn")]
        [SerializeField]
        [Tooltip("Will use a custom default spawnpoint when enabled.")]
        public bool useCustomDefaultSpawn = false;
        
        [SerializeField]
        [Tooltip("The Default Spawnpoint if no Custom Spawnpoint is set")]
        public Pose2d defaultSpawn;

        [Serializable]
        public struct Pose2d
        {
            public Vector2 position;
            public float rotation;
        }

        [Header("Bumpers")]
        [SerializeField] 
        private BumperSettings bumpers;
        private VanityBumpers Vanity => bumpers.Vanity;
        
        public bool AllowedVanity => bumpers.Vanity.AllowVanity;

        [Header("RSL")]
        [SerializeField] public RSLSettings RSLSettings;

        /// <summary>Gets whether to enable controller rumble feedback.</summary>
        protected bool DoControllerRumble { get; private set; }

        private float _controllerRumbleStrength = 0.5f;

        private Coroutine _controllerRumbleRoutine;

        private Gamepad ResolvePairedGamepad()
        {
            if (_playerInput == null) return null;
            return _playerInput.devices.FirstOrDefault(d => d is Gamepad) as Gamepad;
        }

        /// <summary>Event triggered when rumble feedback should be applied.</summary>
        protected Action OnRumbleTrigger { get; private set; }

        public static int RobotCount;

        public static int SpawnedCount;

        private int _robotNum;

        private bool _delayedStart;

        private Material _vanityColorMaterialInstance;

        public enum Space { World, RelativeToStart }
        [HideInInspector] public Vector3 RobotAngle;
        [HideInInspector] public Space   GyroRelativity = Space.World;
        Quaternion _referenceRotation;

        [HideInInspector]
        public Boolean useVanity = false;

        private Rigidbody _rb;

        /// <summary>
        /// All rigidbodies that belong to this robot, including its root rigidbody.
        /// Game pieces use this to temporarily ignore collisions with the complete robot.
        /// </summary>
        public Rigidbody[] Rigidbodies { get; private set; }

        [Header("Game Piece Collisions")]
        [SerializeField]
        [Tooltip("Rigidbodies excluded from held game piece collision ignoring (MoSim versions after 25.3.2). Changes apply on the next pickup.")]
        private Rigidbody[] gamePieceCollisionIgnoreExclusions = Array.Empty<Rigidbody>();

        /// <summary>
        /// Robot bodies whose collisions with held game pieces should not be ignored.
        /// Configure before pickup; each body excludes only its own attached colliders.
        /// </summary>
        public Rigidbody[] GamePieceCollisionIgnoreExclusions
        {
            get => gamePieceCollisionIgnoreExclusions;
            set => gamePieceCollisionIgnoreExclusions = value ?? Array.Empty<Rigidbody>();
        }

        /// <summary>
        /// Initializes components and input actions at startup.
        /// </summary>
        protected virtual void Awake()
        {
            SpawnedCount += 1;
            _robotNum = SpawnedCount;
            _rb = GetComponent<Rigidbody>();
            _referenceRotation = _rb.rotation;
            InitializeComponents();
            DelayedStart();
        }

        /// <summary>
        /// Applies alliance visual colors and sets up controller rumble.
        /// </summary>
        protected virtual void Start()
        {
            Rigidbodies = GetComponentsInChildren<Rigidbody>(true);
            if (_rb != null && !Rigidbodies.Contains(_rb))
            {
                Rigidbodies = Rigidbodies.Append(_rb).ToArray();
            }

            ApplyAllianceVisuals();
            SetupControllerRumble();
            Utils.ClearCache();
        }

        /// <summary>
        /// Handles input polling for restart and camera swap actions.
        /// </summary>
        protected virtual void Update()
        {
            if (_delayedStart)
            {
                _delayedStart = false;
                DelayedStart();
                return;
            }

            if (RestartAction.triggered && !BaseGameManager.Instance.IsResetting)
            {
                BaseGameManager.Instance.StartCoroutine(BaseGameManager.Instance.ResetMatch());
            }

            if (RobotCount > 1 && _robotNum == 1 && SelectedDevice != PlayerDevice.Keyboard)
            {
                if (KeyboardActionPressedThisFrame(RestartAction) && !BaseGameManager.Instance.IsResetting)
                {
                    BaseGameManager.Instance.StartCoroutine(BaseGameManager.Instance.ResetMatch());
                }
            }

            if (SwapCameraAction.triggered)
            {
                // Only flip for third person and first person cameras, not driver station
                GameObject activeCamera = GetActiveCameraV1();
                if (activeCamera != null && activeCamera != DriverStationCam?.CameraObject)
                {
                    var baseVCam = activeCamera.GetComponent<BaseVCamScript>();
                    if (baseVCam != null)
                    {
                        baseVCam.FlipCamera();
                    }
                }
            }

            if (DriverStationCam != null)
            {
                var dsCam = DriverStationCam.CameraObject?.GetComponent<BaseVCamScript>();
                if (dsCam != null)
                {
                    var driverStationMovementValue =
                        DriverStationMovementAction != null && DriverStationMovementAction.enabled
                            ? DriverStationMovementAction.ReadValue<Vector2>()
                            : Vector2.zero;

                    dsCam.SetTranslationValue(driverStationMovementValue);
                }
            }
        }

        /// <summary>
        /// Switches this robot's input on or off. The pause menu uses this so a paused match
        /// cannot be driven, reset, or scored in.
        /// </summary>
        /// <param name="isEnabled">True to accept input, false to ignore it.</param>
        public void SetGameplayInputEnabled(bool isEnabled)
        {
            if (_playerInput != null)
            {
                if (isEnabled)
                {
                    _playerInput.ActivateInput();
                }
                else
                {
                    _playerInput.DeactivateInput();
                }

                return;
            }

            if (InputActionMap == null) return;

            if (isEnabled)
            {
                InputActionMap.Enable();
            }
            else
            {
                InputActionMap.Disable();
            }
        }
        protected void HandleGyro()
        {
            Quaternion current = _rb.rotation;

            if (GyroRelativity == Space.World)
            {
                RobotAngle = current.eulerAngles;
            }
            else
            {
                // Delta quaternion from starting pose — no Euler integration.
                Quaternion delta = Quaternion.Inverse(_referenceRotation) * current;
                RobotAngle = delta.eulerAngles;
            }
        }

        private static bool KeyboardActionPressedThisFrame(InputAction action)
        {
            if (action == null || Keyboard.current == null) return false;

            foreach (var binding in action.bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite) continue;

                var path = binding.effectivePath;
                if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>")) continue;

                if (InputControlPath.TryFindControl(Keyboard.current, path) is ButtonControl button
                    && button.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return false;
        }

        protected virtual void DelayedStart()
        {
            _playerInput = GetComponent<PlayerInput>();

            if (Keyboard.current == null && Gamepad.all.Count == 0) return;

            if (RobotCount > 1)
            {
                ApplyDeviceScope();
            }
            else
            {
                var primary = (InputDevice)Gamepad.current ?? Keyboard.current;
                _playerInput.SwitchCurrentControlScheme(primary);

                var user = _playerInput.user;
                if (Keyboard.current != null)
                    InputUser.PerformPairingWithDevice(Keyboard.current, user);

                if (Gamepad.current != null)
                    InputUser.PerformPairingWithDevice(Gamepad.current, user);
            }

            SetupInputActions();
        }

        public MoSimResult<MoSimLib.Void> SetCoopDevice(PlayerDevice playerDevice)
        {
            var version = MoSim.ActiveVersion;

            SetCoopDeviceV1(playerDevice);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void SetCoopDeviceV1(PlayerDevice playerDevice)
        {
            SelectedDevice = playerDevice;
            if (RobotCount > 1) ApplyDeviceScope();
        }

        // Pair directly
        private void ApplyDeviceScope()
        {
            if (_playerInput == null || _playerInput.actions == null) return;

            var device = ResolveDevice(SelectedDevice);
            if (device == null) return;

            var user = _playerInput.user;
            if (user.valid) user.UnpairDevices();

            user = _playerInput.user;
            InputUser.PerformPairingWithDevice(device, user);

            _playerInput.actions.devices = new[] { device };
        }

        private static InputDevice ResolveDevice(PlayerDevice selected)
        {
            switch (selected)
            {
                case PlayerDevice.Keyboard: return Keyboard.current;
                case PlayerDevice.Gamepad0: return PadAt(0);
                case PlayerDevice.Gamepad1: return PadAt(1);
                case PlayerDevice.Gamepad2: return PadAt(2);
                case PlayerDevice.Gamepad3: return PadAt(3);
                default: return Keyboard.current;
            }
        }

        private static InputDevice PadAt(int idx) => Gamepad.all.Count > idx ? Gamepad.all[idx] : null;

        /// <summary>
        /// Retrieves and wires up core robot components (controllers, input, interpolation).
        /// </summary>
        private void InitializeComponents()
        {
            RobotGamePieceControllerBase = GetComponent<RobotGamePieceControllerBase>();
            if (RobotGamePieceControllerBase == null)
            {
                Debug.LogError("RobotGamePieceController component not found on Robot.");
            }

            DriveController = GetComponent<DriveController>();
            if (DriveController == null)
            {
                Debug.LogError("DriveController component not found on Robot.");
            }

            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null)
            {
                Debug.LogError("PlayerInput component not found on Robot.");
            }
            else
            {
                InputActionMap = _playerInput.currentActionMap;
                if (InputActionMap == null)
                    Debug.LogError("No current Action Map found in PlayerInput.");

                var inputActionAsset = _playerInput.actions;
                if (inputActionAsset == null)
                {
                    Debug.LogError("No InputActionAsset found in PlayerInput.");
                }
                else
                {
                    TryLoadBindingOverrides(inputActionAsset, false);
                    TryLoadBindingOverrides(inputActionAsset, true);
                }
            }

            if (RobotGamePieceControllerBase == null)
                Debug.LogError("RobotGamePieceController component not found on Robot.");
            if (DriveController == null)
                Debug.LogError("DriveController component not found on Robot.");

            Utils.TryGetAddComponent<CustomRigidbodyInterpolation>(gameObject);
            Utils.TryGetAddComponent<JointStackStabilizer>(gameObject);
            Utils.TryGetAddComponent<RSLController>(gameObject);
        }

        private static void TryLoadBindingOverrides(InputActionAsset asset, bool isOtherControls)
        {
            var json = SettingsAccess.ReadBindingOverrides(isOtherControls);
            if (string.IsNullOrEmpty(json)) return;

            var sanitized = SettingsAccess.SanitizeBindingOverridesJson(json, out var droppedCount);
            if (droppedCount > 0)
                SettingsAccess.WriteBindingOverrides(isOtherControls, sanitized);
            if (string.IsNullOrEmpty(sanitized)) return;

            try
            {
                asset.LoadBindingOverridesFromJson(sanitized, removeExisting: false);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"Failed to apply rebind overrides ({(isOtherControls ? "controller" : "keyboard")}): {e.Message}");
            }
        }

        /// <summary>
        /// Maps input actions from the PlayerInput component to public properties.
        /// </summary>
        private void SetupInputActions()
        {
            InputActionMap.Enable();
            ApplyControlSchemeBindingMask();

            TranslateAction = InputActionMap.FindAction("Translate");
            RotateAction = InputActionMap.FindAction("Rotate");
            SwapCameraAction = InputActionMap.FindAction("SwapCamera");
            DriverStationMovementAction = InputActionMap.FindAction("DriverStationMovement");
            RightStickModifierAction = InputActionMap.FindAction("RightStickModifier");
            RestartAction = InputActionMap.FindAction("Restart");
            PauseAction = InputActionMap.FindAction("Pause");
            DriveSpeed1Action = InputActionMap.FindAction("DriveSpeed1");
            DriveSpeed2Action = InputActionMap.FindAction("DriveSpeed2");
            DriveSpeed3Action = InputActionMap.FindAction("DriveSpeed3");

            InputActionMap.Enable();

            // Lets the match record whether the driver used a keyboard or a controller.
            MatchInputTracker.Observe(InputActionMap);

            InputSystem.onDeviceChange -= OnInputDeviceChange;
            InputSystem.onDeviceChange += OnInputDeviceChange;
        }

        /// <summary>
        /// Clears the recorded actuation on every action whenever the device set changes.
        ///
        /// These actions are bound to a gamepad control and a keyboard control at the same time, which
        /// turns on the Input System's disambiguation: it remembers how far the control currently
        /// driving an action is actuated, and discards input from any other bound control that arrives
        /// at that same level. That record only falls back down when the driving control itself reports
        /// a lower value, so if the driving control stops reporting while it still holds one - a
        /// controller that sleeps or reconnects mid-match, a keyboard rebuilt when the OS layout or IME
        /// changes - the other control is ignored from then on and the action looks dead. Resetting
        /// drops the record so the next press is taken on its merits.
        /// </summary>
        private void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                case InputDeviceChange.Removed:
                case InputDeviceChange.Reconnected:
                case InputDeviceChange.Disconnected:
                case InputDeviceChange.ConfigurationChanged:
                    break;
                default:
                    return;
            }

            if (InputActionMap == null) return;

            foreach (var action in InputActionMap.actions)
            {
                action.Reset();
            }
        }

        private void ApplyControlSchemeBindingMask()
        {
            var schemeName = _playerInput?.defaultControlScheme;
            var asset = _playerInput?.actions;
            if (string.IsNullOrEmpty(schemeName) || !asset) return;

            foreach (var scheme in asset.controlSchemes.Where(scheme => scheme.name == schemeName))
            {
                asset.bindingMask = new InputBinding { groups = scheme.bindingGroup };
                return;
            }
        }

        /// <summary>
        /// Initializes gamepad rumble based on player preferences.
        /// </summary>
        private void SetupControllerRumble()
        {
            DoControllerRumble = ControllerRumbleEnabled && !Mathf.Approximately(ControllerRumbleStrength, 0f);
            _controllerRumbleStrength = Mathf.Clamp01(ControllerRumbleStrength);

            OnRumbleTrigger += () =>
            {
                if (_controllerRumbleRoutine != null)
                {
                    StopCoroutine(_controllerRumbleRoutine);
                }

                _controllerRumbleRoutine = StartCoroutine(ControllerRumbleRoutine(0.5f, _controllerRumbleStrength));
            };
        }

        /// <summary>
        /// Provides haptic feedback by vibrating the gamepad for a specified duration.
        /// </summary>
        /// <param name="duration">How long to vibrate in seconds.</param>
        /// <param name="strength">The intensity of vibration (0-1).</param>
        /// <returns>An enumerator for coroutine execution.</returns>
        private IEnumerator ControllerRumbleRoutine(float duration, float strength)
        {
            var gamepad = ResolvePairedGamepad();
            if (gamepad == null || !DoControllerRumble)
                yield break;

            gamepad.SetMotorSpeeds(strength, strength);
            yield return new WaitForSeconds(duration);
            gamepad.SetMotorSpeeds(0f, 0f);
        }

        private void VanityLogic(bool showVanity)
        {

            if (Vanity.EnableOnVanity != null)
            {
                foreach (var obj in Vanity.EnableOnVanity)
                    obj.gameObject.SetActive(showVanity);
            }

            if (Vanity.DisableOnVanity != null)
            {
                foreach (var obj in Vanity.DisableOnVanity)
                    obj.gameObject.SetActive(!showVanity);
            }

            if (!showVanity) return;

            Material mat = null;
            
            switch (Vanity.VanityType)
            {
                case VanityType.NoAllianceColors:
                    break;
                case VanityType.CustomMaterial:
                    if (!Vanity.VanityMaterial || bumpers.bumperMeshes == null) break;
                    foreach (var obj in bumpers.bumperMeshes)
                    {
                        var mats = obj.sharedMaterials;
                        mats[bumpers.materialIndex] = Vanity.VanityMaterial;

                        obj.sharedMaterials = mats;
                    }

                    break;
                case VanityType.CustomColor:
                    if (!bumpers.blueBumperMaterial) break;
                    
                    if (_vanityColorMaterialInstance) Destroy(_vanityColorMaterialInstance);
                    _vanityColorMaterialInstance = new Material(bumpers.blueBumperMaterial) { color = Vanity.VanityColor };
                    
                    if (bumpers.bumperMeshes == null) break;
                    foreach (var obj in bumpers.bumperMeshes)
                    {
                        var mats = obj.sharedMaterials;
                        mats[bumpers.materialIndex] = _vanityColorMaterialInstance;

                        obj.sharedMaterials = mats;
                    }

                    break;
            }
        }

        /// <summary>
        /// Applies alliance-specific materials to the robot's bumpers.
        /// </summary>
        private void ApplyAllianceVisuals()
        {
            var showVanity = useVanity && Vanity.AllowVanity;
    
            VanityLogic(showVanity);

            if (showVanity) return;

            if (bumpers.bumperMeshes == null) return;
            
            foreach (var mesh in bumpers.bumperMeshes)
            {
                var mats = mesh.sharedMaterials;

                switch (alliance)
                {
                    case Alliance.Blue:
                        mats[bumpers.materialIndex] = bumpers.blueBumperMaterial;
                        break;
                    case Alliance.Red:
                        mats[bumpers.materialIndex] = bumpers.redBumperMaterial;
                        break;
                    default:
                        Debug.LogError("Unknown alliance type.");
                        break;
                }

                mesh.sharedMaterials = mats;
            }
        }

        /// <summary>
        /// Cleans up controller rumble and input tracking on robot destruction.
        /// </summary>
        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnInputDeviceChange;
            MatchInputTracker.StopObserving(InputActionMap);

            if (_controllerRumbleRoutine != null)
            {
                StopCoroutine(_controllerRumbleRoutine);
                _controllerRumbleRoutine = null;
            }
            
            if (_vanityColorMaterialInstance != null)
                Destroy(_vanityColorMaterialInstance);

            var gamepad = ResolvePairedGamepad() ?? Gamepad.current;
            gamepad?.SetMotorSpeeds(0f, 0f);
        }
    }
}
