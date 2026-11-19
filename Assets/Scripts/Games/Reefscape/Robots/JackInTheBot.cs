using System;
using System.Collections;
using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Scoring.Scorers;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using RobotFramework.GamePieceSystem;
using Robots.Climbing;
using UnityEngine;

namespace Games.Reefscape.Robots
{
    public class JackInTheBot : ReefscapeRobotBase
    {
        [Header("Robot Components")] 
        [SerializeField] private GenericJoint armJoint;
        [SerializeField] private GenericJoint wristJoint;
        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericRoller leftIntakeRollerJoint;
        [SerializeField] private GenericRoller rightIntakeRollerJoint;
        [SerializeField] private GenericRoller topIntakeRoller;
        [SerializeField] private Transform leftIntakeSensor;
        [SerializeField] private Transform rightIntakeSensor;
        [SerializeField] private Transform algaeSlider;
        [SerializeField] private JITBClimber climber;
        [SerializeField] private Transform coralIntakeTarget;
        
        [Header("Animation Joints (Wheels)")]
        [SerializeField] private GenericAnimationJoint[] intakeWheels;
        [SerializeField] private GenericAnimationJoint[] algaeWheels;
        [SerializeField] private float wheelIntakeSpeed = 500f;

        private ClimbScorer _climbScorer;
        private bool _isScoring = false; // Prevents FixedUpdate from overriding scoring animation
        private bool coralAnimations;
        
        [SerializeField] private Collider l1POSCollider;

        [Header("PID Constants")] 
        [SerializeField] private float armExtensionSafeLimit = 20f;
        [SerializeField] private PidConstants armPidConstants;
        [SerializeField] private PidConstants wristPidConstants;
        [SerializeField] private float pivotStep;
        private float _originalPivotMax;
        
        [Header("Robot Setpoints")] 
        [SerializeField] private JackInTheBotSetpoint stowSetpoint;
        [SerializeField] private JackInTheBotSetpoint coralStowSetpoint;
        [SerializeField] private JackInTheBotSetpoint algaeStowSetpoint;
        [SerializeField] private JackInTheBotSetpoint groundCoralIntakeSetpoint;
        [SerializeField] private JackInTheBotSetpoint groundAlgaeIntakeSetpoint;
        [SerializeField] private JackInTheBotSetpoint stationCoralIntakeSetpoint;
        [SerializeField] private JackInTheBotSetpoint stackAlgaeIntakeSetpoint;
        [SerializeField] private JackInTheBotSetpoint l4Setpoint;
        [SerializeField] private JackInTheBotSetpoint l4BackSetpoint;
        [SerializeField] private JackInTheBotSetpoint l4BackPlaceSetpoint;
        [SerializeField] private JackInTheBotSetpoint bargeSetpoint;
        [SerializeField] private JackInTheBotSetpoint bargePlaceSetpoint;
        [SerializeField] private JackInTheBotSetpoint l3Setpoint;
        [SerializeField] private JackInTheBotSetpoint l3BackSetpoint;
        [SerializeField] private JackInTheBotSetpoint highAlgaeSetpoint;
        [SerializeField] private JackInTheBotSetpoint highAlgaeBackSetpoint;
        [SerializeField] private JackInTheBotSetpoint l2Setpoint;
        [SerializeField] private JackInTheBotSetpoint l2BackSetpoint;
        [SerializeField] private JackInTheBotSetpoint lowAlgaeSetpoint;
        [SerializeField] private JackInTheBotSetpoint lowAlgaeBackSetpoint;
        [SerializeField] private JackInTheBotSetpoint l1Setpoint;
        [SerializeField] private JackInTheBotSetpoint l1VertSetpoint;
        [SerializeField] private JackInTheBotSetpoint l1HighSetpoint;
        [SerializeField] private JackInTheBotSetpoint processorSetpoint;
        [SerializeField] private JackInTheBotSetpoint climbSetpoint;
        [SerializeField] private JackInTheBotSetpoint climbedSetpoint;

        private ReefscapeSetpoints _previousSetpoint = ReefscapeSetpoints.Stow;

        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        [Header("Game Piece Intakes")] 
        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Game Piece States")] 
        [SerializeField] private string currentState;
        [SerializeField] private GamePieceState coralIntakeState;
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState coralBackStowState;
        [SerializeField] private GamePieceState coralFrontStowState;
        [SerializeField] private GamePieceState coralL1TargetState;
        [SerializeField] private GamePieceState algaeStowState;
        [SerializeField] private GamePieceState algaeHomeState;
        [SerializeField] private float algaeEjectForce;

        [Header("Intake Audio")] [SerializeField]
        private AudioSource intakeAudioSource;

        [SerializeField] private AudioClip intakeClip;
        [SerializeField] private AudioSource algaeStallSource;
        [SerializeField] private AudioClip algaeStallClip;

        [Header("Target Setpoints")] [SerializeField]
        private float targetArmAngle;

        [SerializeField] private float targetWristAngle;
        [SerializeField] private float targetArmDistance;
        private bool _robotSpectialPressed;
        private bool _stationMode;

        protected override void Start()
        {
            base.Start();
            _climbScorer = gameObject.GetComponent<ClimbScorer>();
            armJoint.SetPid(armPidConstants);
            wristJoint.SetPid(wristPidConstants);
            _originalPivotMax = armPidConstants.Max;

            targetArmAngle = stowSetpoint.armAngle;
            targetWristAngle = stowSetpoint.wristAngle;
            targetArmDistance = stowSetpoint.armDistance;

            RobotGamePieceController.SetPreload(coralStowState);

            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[] { coralIntakeState, coralStowState, coralBackStowState, coralFrontStowState, coralL1TargetState };
            _coralController.intakes.Add(coralIntake);

            _algaeController.gamePieceStates = new[] { algaeStowState, algaeHomeState };
            _algaeController.intakes.Add(algaeIntake);
            
            _robotSpectialPressed = false;
            _stationMode = false;
            
            intakeAudioSource.clip = intakeClip;
            intakeAudioSource.loop = true;
            intakeAudioSource.playOnAwake = false;
            
            algaeStallSource.clip = algaeStallClip;
            algaeStallSource.loop = true;
            algaeStallSource.playOnAwake = false;
            coralAnimations = true;
        }

        private void LateUpdate()
        {
            armJoint.UpdatePid(armPidConstants);
            wristJoint.UpdatePid(wristPidConstants);
        }

        private void FixedUpdate()
        {
            bool armAtAngle = Utils.InAngularRange(armJoint.GetSingleAxisAngle(JointAxis.X), -targetArmAngle, 8f);

            armJoint.SetTargetAngle(targetArmAngle).withAxis(JointAxis.X).flipDirection();
            wristJoint.SetTargetAngle(targetWristAngle).withAxis(JointAxis.X).flipDirection().noWrap(-30);
            elevator.SetTarget(armAtAngle || targetArmDistance < armExtensionSafeLimit ? targetArmDistance : 0f);

            var canIntakeCoral = _coralController.currentStateNum == 0 && IntakeAction.IsPressed() && _algaeController.currentStateNum == 0;
            var canIntakeAlgae = _algaeController.currentStateNum == 0 && IntakeAction.IsPressed() && _coralController.currentStateNum == 0;
            var realStep = pivotStep;

            if (algaeIntake.GamePiece != null)
            {
                var localSliderSpace = algaeIntake.transform.InverseTransformPoint(algaeIntake.GamePiece.transform.position).x;
                algaeSlider.localPosition = new Vector3(-localSliderSpace, algaeSlider.localPosition.y, algaeSlider.localPosition.z);
            }

            if (Utils.WithinAngularRange(armJoint.GetSingleAxisAngle(JointAxis.X), targetArmAngle, 15f))
                armPidConstants.Max = Mathf.Max(armPidConstants.Max - (realStep * Time.fixedDeltaTime), realStep);
            else
                armPidConstants.Max = Mathf.Min(armPidConstants.Max + (realStep * Time.fixedDeltaTime), _originalPivotMax);

            l1POSCollider.enabled = (CurrentIntakeMode == ReefscapeIntakeMode.L1);

            var readState = _coralController.GetCurrentState();
            if (readState != null)
            {
                currentState = readState.name;
            }
            
            UpdateIntakeAudio();

            if (BaseGameManager.Instance.RobotState == RobotState.Disabled) return;

            _algaeController.SetTargetState(_algaeController.currentStateNum > 0 ? algaeHomeState : algaeStowState);
            CheckStationMode();
            
            if ((_previousSetpoint == ReefscapeSetpoints.RobotSpecial && IntakeAction.IsPressed()) || (_stationMode && IntakeAction.IsPressed() && CurrentSetpoint != ReefscapeSetpoints.HighAlgae && CurrentSetpoint != ReefscapeSetpoints.LowAlgae && CurrentRobotMode != ReefscapeRobotMode.Algae))
            {
                SetState(ReefscapeSetpoints.RobotSpecial);
            }

            // --- IMPROVED WHEEL LOGIC ---
            // We only run this if we are NOT in the middle of a scoring coroutine
            if (!_isScoring)
            {
                bool isIntaking = (CurrentSetpoint == ReefscapeSetpoints.Intake || CurrentSetpoint == ReefscapeSetpoints.RobotSpecial || CurrentSetpoint == ReefscapeSetpoints.Stack || CurrentSetpoint == ReefscapeSetpoints.HighAlgae || CurrentSetpoint == ReefscapeSetpoints.LowAlgae) && IntakeAction.IsPressed();

                if (isIntaking && (CurrentRobotMode == ReefscapeRobotMode.Coral && CurrentSetpoint != ReefscapeSetpoints.HighAlgae && CurrentSetpoint != ReefscapeSetpoints.LowAlgae) && !_coralController.HasPiece() && !_algaeController.HasPiece())
                {
                    foreach (var wheel in intakeWheels)
                        wheel.VelocityRoller(wheelIntakeSpeed).useAxis(JointAxis.X);
                    foreach (var wheel in algaeWheels)
                        wheel.VelocityRoller(-wheelIntakeSpeed).useAxis(JointAxis.X);
                } else if (isIntaking &&
                           (CurrentRobotMode == ReefscapeRobotMode.Algae ||
                            CurrentSetpoint != ReefscapeSetpoints.HighAlgae ||
                            CurrentSetpoint != ReefscapeSetpoints.LowAlgae) && !_algaeController.HasPiece() && !_coralController.HasPiece())
                {
                    foreach (var wheel in intakeWheels)
                        wheel.VelocityRoller(0).useAxis(JointAxis.X);
                    foreach (var wheel in algaeWheels)
                        wheel.VelocityRoller(wheelIntakeSpeed).useAxis(JointAxis.X);
                }
                else
                {
                    // Regular stopping of rollers
                    leftIntakeRollerJoint.OverrideRpm(0);
                    rightIntakeRollerJoint.OverrideRpm(0);
                    topIntakeRoller.OverrideRpm(0);
                    
                    // Explicitly stop wheel animations
                    foreach (var wheel in intakeWheels)
                        wheel.VelocityRoller(0).useAxis(JointAxis.X);
                    foreach (var wheel in algaeWheels)
                        wheel.VelocityRoller(0).useAxis(JointAxis.X);
                }
            }

            // Climber and Drive modifiers remain the same...
            if (_climbScorer.AutoClimbTriggered && CurrentSetpoint == ReefscapeSetpoints.Climb && climber.WingsOpen())
                SetState(ReefscapeSetpoints.Climbed);
            else if (!_climbScorer.AutoClimbTriggered && CurrentSetpoint == ReefscapeSetpoints.Climbed)
                SetState(ReefscapeSetpoints.Climb);

            if (CurrentSetpoint is ReefscapeSetpoints.Climb or ReefscapeSetpoints.Climbed) DriveController.SetDriveMp(0.5f);
            else if (CurrentSetpoint is ReefscapeSetpoints.Barge || LastSetpoint == ReefscapeSetpoints.Barge) DriveController.SetDriveMp(0.8f);
            else DriveController.SetDriveMp(1);

            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    if (_coralController.currentStateNum != 0 || _algaeController.currentStateNum != 0)
                    {
                        SetSetpoint(_coralController.currentStateNum > 0 ? CurrentIntakeMode == ReefscapeIntakeMode.L1 ? l1HighSetpoint : coralStowSetpoint : algaeStowSetpoint);
                        _coralController.SetTargetState(coralStowState);
                        break;
                    }
                    SetSetpoint(stowSetpoint);
                    _coralController.SetTargetState(coralStowState);
                    break;
                case ReefscapeSetpoints.Intake:
                    if (LastSetpoint == ReefscapeSetpoints.RobotSpecial) { SetState(ReefscapeSetpoints.RobotSpecial); break; }
                    SetSetpoint(CurrentRobotMode == ReefscapeRobotMode.Coral ? groundCoralIntakeSetpoint : groundAlgaeIntakeSetpoint);
                    _coralController.SetTargetState(CurrentIntakeMode == ReefscapeIntakeMode.L1 ? coralL1TargetState : coralIntakeState);
                    _coralController.RequestIntake(coralIntake, canIntakeCoral && CurrentRobotMode == ReefscapeRobotMode.Coral);
                    _algaeController.RequestIntake(algaeIntake, canIntakeAlgae && CurrentRobotMode == ReefscapeRobotMode.Algae);
                    break;
                case ReefscapeSetpoints.Place:
                    if (_previousSetpoint != ReefscapeSetpoints.Place)
                    {
                        coralAnimations = _coralController.HasPiece();
                    }
                    StartCoroutine(PlaceGamePiece(LastSetpoint, readState));
                    break;
                case ReefscapeSetpoints.L1:
                    SetSetpoint(CurrentIntakeMode == ReefscapeIntakeMode.L1 ? l1Setpoint : l1VertSetpoint);
                    break;
                case ReefscapeSetpoints.Processor:
                    SetSetpoint(processorSetpoint);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetSetpoint(stackAlgaeIntakeSetpoint);
                    _algaeController.RequestIntake(algaeIntake, canIntakeAlgae);
                    break;
                case ReefscapeSetpoints.L2:
                    SetSetpoint(FacingReef ? l2Setpoint : l2BackSetpoint);
                    _coralController.SetTargetState(FacingReef ? coralFrontStowState : coralBackStowState);
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(FacingReef ? lowAlgaeSetpoint : lowAlgaeBackSetpoint);
                    _algaeController.RequestIntake(algaeIntake, canIntakeAlgae);
                    break;
                case ReefscapeSetpoints.L3:
                    SetSetpoint(FacingReef ? l3Setpoint : l3BackSetpoint);
                    _coralController.SetTargetState(FacingReef ? coralFrontStowState : coralBackStowState);
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    SetSetpoint(FacingReef ? highAlgaeSetpoint : highAlgaeBackSetpoint);
                    _algaeController.RequestIntake(algaeIntake, canIntakeAlgae);
                    break;
                case ReefscapeSetpoints.L4:
                    SetSetpoint(FacingReef ? l4Setpoint : l4BackSetpoint);
                    _coralController.SetTargetState(FacingReef ? coralFrontStowState : coralBackStowState);
                    break;
                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargeSetpoint);
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetSetpoint(stationCoralIntakeSetpoint);
                    _coralController.SetTargetState(coralStowState);
                    _coralController.RequestIntake(coralIntake, canIntakeCoral);
                    break;
                case ReefscapeSetpoints.Climb:
                    SetSetpoint(climbSetpoint);
                    climber.Climb();
                    break;
                case ReefscapeSetpoints.Climbed:
                    StartCoroutine(RotateArmFirst(climbedSetpoint));
                    climber.NotClimbing();
                    _coralController.SetTargetState(coralStowState);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Raycast logic remains the same...
            if (CurrentIntakeMode == ReefscapeIntakeMode.L1)
            {
                _coralController.MoveIntake(coralIntake, coralL1TargetState.stateTarget);
                _coralController.SetTargetState(coralL1TargetState);
                if (leftIntakeRollerJoint.gameObject.activeSelf)
                {
                    leftIntakeRollerJoint.gameObject.SetActive(false);
                    rightIntakeRollerJoint.gameObject.SetActive(false);
                }
            }
            else
            {
                _coralController.MoveIntake(coralIntake, coralIntakeTarget);
                if (!leftIntakeRollerJoint.gameObject.activeSelf)
                {
                    leftIntakeRollerJoint.gameObject.SetActive(true);
                    rightIntakeRollerJoint.gameObject.SetActive(true);
                }

                var rayDirection = coralIntakeState.stateTarget.forward;
                var distance = 0.0254f * 5f;
                var coralMask = LayerMask.GetMask("Coral");
                var coralRight = Physics.Raycast(rightIntakeSensor.position, rayDirection, distance, coralMask);
                var coralLeft = Physics.Raycast(leftIntakeSensor.position, rayDirection, distance, coralMask);

                if (IntakeAction.IsPressed() && CurrentSetpoint != ReefscapeSetpoints.LowAlgae && CurrentSetpoint != ReefscapeSetpoints.HighAlgae)
                {
                    if (coralRight && coralLeft)
                    {
                        leftIntakeRollerJoint.OverrideRpm(9000);
                        rightIntakeRollerJoint.OverrideRpm(9000);
                    }
                }
            }

            _previousSetpoint = CurrentSetpoint;
        }

        private IEnumerator PlaceGamePiece(ReefscapeSetpoints lastSetpoint, GamePieceState readState)
        {
            _isScoring = true; // Lock FixedUpdate intake wheels
            
            // Front (FacingReef) -> Spin Same Way (+)
            // Back (Not FacingReef) -> Spin Opposite Way (-)
            if (coralAnimations)
            {
                float speed = FacingReef ? wheelIntakeSpeed : -wheelIntakeSpeed;

                foreach (var wheel in intakeWheels)
                    wheel.VelocityRoller(speed).useAxis(JointAxis.X);
                foreach (var wheel in algaeWheels)
                    wheel.VelocityRoller(-speed).useAxis(JointAxis.X);
            } else
            {
                foreach (var wheel in intakeWheels)
                    wheel.VelocityRoller(0).useAxis(JointAxis.X);
                foreach (var wheel in algaeWheels)
                    wheel.VelocityRoller(-wheelIntakeSpeed).useAxis(JointAxis.X);
            }

            if (lastSetpoint is ReefscapeSetpoints.Barge)
            {
                targetArmAngle = bargePlaceSetpoint.armAngle;
                targetWristAngle = bargePlaceSetpoint.wristAngle;
                targetArmDistance = bargePlaceSetpoint.armDistance;
                yield return new WaitForSeconds(0.075f);
            }
            else if ((lastSetpoint == ReefscapeSetpoints.L1 && CurrentIntakeMode != ReefscapeIntakeMode.L1))
            {
                leftIntakeRollerJoint.OverrideRpm(2000);
                rightIntakeRollerJoint.OverrideRpm(-2000);
                topIntakeRoller.OverrideRpm(-2000);
            }
            else if ((lastSetpoint is not ReefscapeSetpoints.Processor && !FacingReef))
            {
                leftIntakeRollerJoint.OverrideRpm(2000);
                rightIntakeRollerJoint.OverrideRpm(-2000);
                topIntakeRoller.OverrideRpm(8000);
            } if ((lastSetpoint is not ReefscapeSetpoints.Processor && FacingReef))
            {
                leftIntakeRollerJoint.OverrideRpm(-2000);
                rightIntakeRollerJoint.OverrideRpm(2000);
                topIntakeRoller.OverrideRpm(-8000);
            }

            Vector3 force;
            if (CurrentIntakeMode == ReefscapeIntakeMode.L1 || (readState != null && readState.stateNum == coralL1TargetState.stateNum))
                force = new Vector3(1, 0, 0);
            else
            {
                force = FacingReef ? new Vector3(0, 0, -8) : new Vector3(0, 0, 8);
                if (LastSetpoint == ReefscapeSetpoints.L1) force = new Vector3(0, 0, 2f);
            }

            _coralController.ReleaseGamePieceWithForce(force);
            _algaeController.ReleaseGamePieceWithForce(new Vector3(0, algaeEjectForce, 0));

            if (lastSetpoint is ReefscapeSetpoints.L4 && !FacingReef)
            {
                yield return new WaitForSeconds(0.05f);
                targetArmAngle = l4BackPlaceSetpoint.armAngle;
                targetWristAngle = l4BackPlaceSetpoint.wristAngle;
                targetArmDistance = l4BackPlaceSetpoint.armDistance;
            }

            // Wait until game pieces are released (state becomes 0) or timeout after 0.5s
            float timer = 0f;
            while ((_coralController.currentStateNum != 0 || _algaeController.currentStateNum != 0) && timer < 0.5f)
            {
                timer += Time.deltaTime;
                yield return null;
            }
            
            // Explicitly stop wheels
            foreach (var wheel in intakeWheels) 
                wheel.VelocityRoller(0).useAxis(JointAxis.X);
            foreach (var wheel in algaeWheels) 
                wheel.VelocityRoller(0).useAxis(JointAxis.X);
                
            _isScoring = false; // Release lock
        }

        private void CheckStationMode()
        {
            if (RobotSpecialAction.IsPressed() && !_robotSpectialPressed && BaseGameManager.Instance.RobotState == RobotState.Enabled)
                _stationMode = !_stationMode;

            CurrentCoralStationMode.DropType = _stationMode ? DropType.Station : DropType.Ground;
            _robotSpectialPressed = RobotSpecialAction.IsPressed();
        }
        
        private void UpdateIntakeAudio()
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                if (intakeAudioSource.isPlaying || algaeStallSource.isPlaying)
                {
                    intakeAudioSource.Stop();
                    algaeStallSource.Stop();
                }

                return;
            }

            if ((IntakeAction.IsPressed() || OuttakeAction.IsPressed() || CurrentSetpoint is ReefscapeSetpoints.Climb) &&
                !intakeAudioSource.isPlaying)
            {
                intakeAudioSource.Play();
            }
            else if (!IntakeAction.IsPressed() && !OuttakeAction.IsPressed() && CurrentSetpoint is not ReefscapeSetpoints.Climb &&
                     intakeAudioSource.isPlaying)
            {
                intakeAudioSource.Stop();
            }

            if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0 && !algaeStallSource.isPlaying)
            {
                algaeStallSource.Play();
            }
            else if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum == 0 && algaeStallSource.isPlaying)
            {
                algaeStallSource.Stop();
            }
        }
        
        private IEnumerator RotateArmFirst(JackInTheBotSetpoint setpoint)
        {
            targetArmAngle = setpoint.armAngle;
            targetWristAngle = setpoint.wristAngle;
            yield return new WaitForSeconds(0.65f);
            targetArmDistance = setpoint.armDistance;
        }

        private void SetSetpoint(JackInTheBotSetpoint setpoint)
        {
            targetArmAngle = setpoint.armAngle;
            targetWristAngle = setpoint.wristAngle;
            targetArmDistance = setpoint.armDistance;
        }
    }

    [Serializable]
    public struct JackInTheBotSetpoint
    {
        [Tooltip("Deg")] public float armAngle;
        [Tooltip("Deg")] public float wristAngle;
        [Tooltip("Inch")] public float armDistance;
    }
}