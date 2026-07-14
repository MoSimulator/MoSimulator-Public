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
    public class Robonauts : ReefscapeRobotBase
    {
        [Header("Robot Components")]
        [SerializeField]
        private GenericJoint armJoint;

        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint intakeJoint;
        [SerializeField] private GenericJoint algaeArmsJoint;
        [SerializeField] private GenericJoint coralFlap;
        [SerializeField] private GenericJoint bargeFlap;
        [SerializeField] private Transform algaeTarget;
        [SerializeField] private Transform algaeSlider;
        [SerializeField] private GenericRoller algaeRoller;
        [SerializeField] private GenericJoint algaeSpringL;
        [SerializeField] private GenericJoint algaeSpringR;
        [SerializeField] private RobonautsClimber climber;
        [SerializeField] private ClimbScorer scorer;

        [Header("Physics Rollers")]
        [SerializeField]
        private GenericRoller[] physRollers;

        [Header("PID Constants")]
        [SerializeField]
        private PidConstants armPidConstants;

        [SerializeField] private PidConstants intakePidConstants;
        [SerializeField] private PidConstants algaeArmsPidConstants;

        [Header("Robot Setpoints")]
        [SerializeField]
        private RobotnautsSetpoint stowSetpoint;

        [SerializeField] private RobotnautsSetpoint coralStowSetpoint;
        [SerializeField] private RobotnautsSetpoint algaeStowSetpoint;
        [SerializeField] private RobotnautsSetpoint coralIntakeSetpoint;
        [SerializeField] private RobotnautsSetpoint algaeGroundIntakeSetpoint;
        [SerializeField] private RobotnautsSetpoint coralStationSetpoint;
        [SerializeField] private RobotnautsSetpoint l1Setpoint;
        [SerializeField] private RobotnautsSetpoint l1ModeSetpoint;
        [SerializeField] private RobotnautsSetpoint l2Setpoint;
        [SerializeField] private RobotnautsSetpoint lowAlgaeSetpoint;
        [SerializeField] private RobotnautsSetpoint l3Setpoint;
        [SerializeField] private RobotnautsSetpoint highAlgaeSetpoint;
        [SerializeField] private RobotnautsSetpoint l4Setpoint;
        [SerializeField] private RobotnautsSetpoint bargeSetpoint;
        [SerializeField] private RobotnautsSetpoint processorSetpoint;
        [SerializeField] private RobotnautsSetpoint stackAlgaeIntakeSetpoint;

        [Header("Game Piece Intakes")]
        [SerializeField]
        private ReefscapeGamePieceIntake coralIntake;

        [SerializeField] private ReefscapeGamePieceIntake coralStationIntake;

        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Game Piece States")]
        [SerializeField] private GamePieceState coralIntakeState;
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState handoffIntakeState;
        [SerializeField] private GamePieceState midwayHandoffState;
        [SerializeField] private GamePieceState coralPreloadState;
        [SerializeField] private GamePieceState algaeStowState;
        [SerializeField] private GamePieceState l1CoralStow;
        [Header("Audio")]
        [SerializeField]
        private AudioSource intakeAudioSource;
        [SerializeField] private AudioClip intakeClip;
        [SerializeField] private AudioSource algaeAudioSource;
        [SerializeField] private AudioClip algaeClip;

        [Header("Target Setpoints")]
        [SerializeField]
        private float targetArmAngle;

        [SerializeField] private float targetElevatorHeight;
        [SerializeField] private float targetIntakeAngle;
        [SerializeField] private float targetAlgaeArmsAngle;

        [Header("Intake Wheels")]
        [SerializeField]
        private GenericAnimationJoint[] intakeWheels;

        [SerializeField] private float intakeWheelSpeed = 300f;

        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode algaeController;

        private ReefscapeAutoAlign align;

        private bool _alreadyPlaced;
        private bool _canIntakeCoral;
        private bool _canBuffer;
        private bool _wasCoral;
        private bool preAligned;
        private bool StationMode;

        private bool _robotSpectialPressed;
        private bool _isScoring = false;

        private ReefscapeSetpoints previousSetpoint = ReefscapeSetpoints.Stow;

        private ReefscapeSetpoints? bufferedSetpoint;

        protected override void Start()
        {
            base.Start();

            armJoint.SetPid(armPidConstants);
            intakeJoint.SetPid(intakePidConstants);
            algaeArmsJoint.SetPid(algaeArmsPidConstants);
            coralFlap.SetPid(intakePidConstants);
            bargeFlap.SetPid(intakePidConstants);
            algaeSpringL.SetPid(intakePidConstants);
            algaeSpringR.SetPid(intakePidConstants);

            targetArmAngle = coralStowSetpoint.armAngle;
            targetElevatorHeight = 0;
            targetIntakeAngle = coralStowSetpoint.intakeAngle;
            targetAlgaeArmsAngle = coralStowSetpoint.algaeArmsAngle;

            RobotGamePieceController.SetPreload(coralPreloadState);
            coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            coralController.gamePieceStates = new[]
                { coralIntakeState, coralStowState, handoffIntakeState, midwayHandoffState, coralPreloadState, l1CoralStow };
            coralController.intakes.Add(coralIntake);
            coralController.intakes.Add(coralStationIntake);

            algaeController.gamePieceStates = new[] { algaeStowState };
            algaeController.intakes.Add(algaeIntake);

            intakeAudioSource.clip = intakeClip;
            intakeAudioSource.loop = true;
            intakeAudioSource.Stop();

            algaeAudioSource.clip = algaeClip;
            algaeAudioSource.loop = true;
            algaeAudioSource.Stop();

            _alreadyPlaced = false;
            _canIntakeCoral = true;
            bufferedSetpoint = null;
            _canBuffer = true;
            _robotSpectialPressed = false;
            StationMode = false;

            align = gameObject.GetComponent<ReefscapeAutoAlign>();
            preAligned = false;
        }

        private void LateUpdate()
        {
            armJoint.UpdatePid(armPidConstants);
            intakeJoint.UpdatePid(intakePidConstants);
            algaeArmsJoint.UpdatePid(algaeArmsPidConstants);
        }

        private void FixedUpdate()
        {
            if (coralController.HasPiece() || CurrentSetpoint == ReefscapeSetpoints.Place || (CurrentRobotMode != ReefscapeRobotMode.Coral && !algaeController.HasPiece()))
            {
                foreach (var roller in physRollers)
                {
                    roller.flipVelocity();
                }
            }

            if (algaeController.HasPiece() || CurrentSetpoint == ReefscapeSetpoints.Place)
            {
                algaeRoller.flipVelocity();
            }

            UpdateAudio();

            algaeController.SetTargetState(algaeStowState);

            if (CurrentIntakeMode == ReefscapeIntakeMode.L1 && coralController.HasPiece())
            {
                bufferedSetpoint = ReefscapeSetpoints.L1;
                if (CurrentSetpoint == ReefscapeSetpoints.Place)
                {
                    bufferedSetpoint = null;
                }
            }

            //overide actions
            AlgaeSlider();
            AutoAlignOffsets();
            CheckStationMode();
            RobonautsIntakeSequence();

            //input overides
            ClearBuffers();

            //run primary loop
            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    bool hasCoral = coralController.HasPiece();
                    bool hasAlgae = algaeController.HasPiece();
                    if (hasAlgae)
                    {
                        SetSetpoint(algaeStowSetpoint);
                    }
                    else if (hasCoral)
                    {
                        SetSetpoint(coralStowSetpoint);
                    }
                    else
                    {
                        SetSetpoint(stowSetpoint);
                    }

                    algaeController.RequestIntake(algaeIntake, false);
                    coralController.RequestIntake(coralIntake, false);
                    break;
                case ReefscapeSetpoints.Intake:
                    if (CurrentRobotMode == ReefscapeRobotMode.Algae && algaeController.currentStateNum == 0 && (coralController.currentStateNum >= midwayHandoffState.stateNum || !coralController.HasPiece()))
                    {
                        SetSetpoint(algaeGroundIntakeSetpoint);
                    }
                    else
                    {
                        if (StationMode)
                        {
                            SetSetpoint(coralStationSetpoint);
                            coralController.RequestIntake(coralStationIntake, (CurrentRobotMode == ReefscapeRobotMode.Coral || algaeController.HasPiece()) && !coralController.HasPiece());
                            coralController.RequestIntake(coralIntake, false);

                        }
                        else
                        {
                            SetSetpoint(coralIntakeSetpoint);
                            coralController.RequestIntake(coralIntake, (CurrentRobotMode == ReefscapeRobotMode.Coral || algaeController.HasPiece()) && !coralController.HasPiece());
                            coralController.RequestIntake(coralStationIntake, false);
                        }

                        if (coralController.HasPiece())
                        {
                            SetRobotMode(ReefscapeRobotMode.Coral);
                        }

                    }

                    algaeController.RequestIntake(algaeIntake, CurrentRobotMode == ReefscapeRobotMode.Algae && algaeController.currentStateNum == 0);

                    break;
                case ReefscapeSetpoints.Place:
                    if (OuttakeAction.triggered)
                    {
                        if (coralController.HasPiece() && algaeController.HasPiece())
                        {
                            StartCoroutine(PlaceCoroutine());
                            switch (CurrentRobotMode)
                            {
                                case ReefscapeRobotMode.Algae:
                                    SetRobotMode(ReefscapeRobotMode.Coral);
                                    break;
                                case ReefscapeRobotMode.Coral:
                                    _wasCoral = true;
                                    SetRobotMode(ReefscapeRobotMode.Algae);
                                    break;
                            }
                        }
                        else
                        {
                            StartCoroutine(PlaceCoroutine());
                        }
                    }
                    break;
                case ReefscapeSetpoints.L1:
                    bool l1Mode = CurrentIntakeMode == ReefscapeIntakeMode.L1;
                    SetSetpoint(l1Mode ? l1ModeSetpoint : l1Setpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.L2:
                    SetSetpoint(l2Setpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(lowAlgaeSetpoint);
                    var canIntakeAlgaeLow = algaeController.currentStateNum == 0 &&
                                           IntakeAction.IsPressed();
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, canIntakeAlgaeLow);
                    break;
                case ReefscapeSetpoints.L3:
                    SetSetpoint(l3Setpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    SetSetpoint(highAlgaeSetpoint);
                    var canIntakeAlgaeHigh = algaeController.currentStateNum == 0 &&
                                             IntakeAction.IsPressed();
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, canIntakeAlgaeHigh);
                    break;
                case ReefscapeSetpoints.L4:
                    SetSetpoint(l4Setpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargeSetpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.Processor:
                    SetSetpoint(processorSetpoint);
                    coralController.RequestIntake(coralIntake, false);
                    algaeController.RequestIntake(algaeIntake, false);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetSetpoint(stackAlgaeIntakeSetpoint);
                    var canIntakeAlgaeStack = algaeController.currentStateNum == 0 &&
                                             IntakeAction.IsPressed();
                    algaeController.RequestIntake(algaeIntake, canIntakeAlgaeStack);
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetState(previousSetpoint);
                    break;
                case ReefscapeSetpoints.Climb:
                    climber.Climb();
                    SetSetpoint(coralStationSetpoint);
                    if (scorer.AutoClimbTriggered)
                    {
                        SetState(ReefscapeSetpoints.Climbed);
                    }
                    break;
                case ReefscapeSetpoints.Climbed:
                    SetSetpoint(coralStationSetpoint);
                    climber.NotClimbing();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            previousSetpoint = CurrentSetpoint;
            UseSetpoint();
        }

        private void SetSetpoint(RobotnautsSetpoint setpoint)
        {
            targetArmAngle = setpoint.armAngle;
            targetElevatorHeight = setpoint.elevatorHeight;
            targetIntakeAngle = setpoint.intakeAngle;
            targetAlgaeArmsAngle = setpoint.algaeArmsAngle;
        }

        private void UseSetpoint()
        {
            armJoint.SetTargetAngle(targetArmAngle).withAxis(JointAxis.X).flipDirection().noWrap(-45);
            elevator.SetTarget(targetElevatorHeight);
            intakeJoint.SetTargetAngle(targetIntakeAngle).withAxis(JointAxis.X).flipDirection();
            algaeArmsJoint.SetTargetAngle(targetAlgaeArmsAngle).withAxis(JointAxis.X).flipDirection();
            coralFlap.SetTargetAngle(0).withAxis(JointAxis.X);
            bargeFlap.SetTargetAngle(0).withAxis(JointAxis.X);
            algaeSpringL.SetLinearTarget(0).withAxis(JointAxis.X).useAutomaticStartingOffset();
            algaeSpringR.SetLinearTarget(0).withAxis(JointAxis.X).useAutomaticStartingOffset();
        }

        private void ClearBuffers()
        {
            if (StationMode && CurrentIntakeMode == ReefscapeIntakeMode.L1)
            {
                StationMode = false;
            }

            if (_wasCoral && !coralController.HasPiece() && !algaeController.HasPiece())
            {
                SetRobotMode(ReefscapeRobotMode.Coral);
                _wasCoral = false;
            }

            if (previousSetpoint == ReefscapeSetpoints.Place && CurrentSetpoint != ReefscapeSetpoints.Place)
            {
                _alreadyPlaced = false;
            }

            if (bufferedSetpoint.HasValue && _canBuffer)
            {
                SetState(bufferedSetpoint.Value);
                bufferedSetpoint = null;
            }
            else
            {
                _canBuffer = true;
            }
        }

        private void CheckStationMode()
        {
            if (RobotSpecialAction.IsPressed() && !_robotSpectialPressed && BaseGameManager.Instance.RobotState == RobotState.Enabled && (!coralController.HasPiece() || coralController.currentStateNum >= midwayHandoffState.stateNum))
            {
                StationMode = !StationMode;
            }

            if (StationMode)
            {
                CurrentCoralStationMode.DropType = DropType.Station;
            }
            else
            {
                CurrentCoralStationMode.DropType = DropType.Ground;
            }

            _robotSpectialPressed = RobotSpecialAction.IsPressed();
        }
        private void AutoAlignOffsets()
        {
            if (!AutoAlignLeftAction.IsPressed() && !AutoAlignRightAction.IsPressed())
            {
                preAligned = false;
            }

            float l4Distance = 5f;
            float lOtherDistance = 5.5f;
            if (!preAligned && (AutoAlignLeftAction.IsPressed() || AutoAlignRightAction.IsPressed()))
            {
                l4Distance = -5;
                lOtherDistance = -5;
                if (align.getDistance() < 0.0254f * 6f && !AutoAlignLeftAction.triggered && !AutoAlignRightAction.triggered)
                {
                    preAligned = true;
                }
            }
            float zOffset = CurrentSetpoint == ReefscapeSetpoints.L4 ? l4Distance : lOtherDistance;
            align.offset = new Vector3(0, 0, zOffset);
        }

        private void RobonautsIntakeSequence()
        {
            _canIntakeCoral = (Utils.InRange(elevator.GetElevatorHeight(), coralIntakeSetpoint.elevatorHeight, 1) &&
                              Utils.InAngularRange(armJoint.GetSingleAxisAngle(JointAxis.X), coralIntakeSetpoint.algaeArmsAngle, 3));

            if (CurrentIntakeMode == ReefscapeIntakeMode.L1)
            {
                coralController.SetTargetState(l1CoralStow);
            }
            else if (StationMode)
            {
                coralController.SetTargetState(coralStowState);
            }
            else if ((_canIntakeCoral || coralController.currentStateNum >= midwayHandoffState.stateNum) && CurrentIntakeMode != ReefscapeIntakeMode.L1)
            {
                coralController.SetTargetState(coralController.currentStateNum switch
                {
                    0 => coralIntakeState,
                    1 => handoffIntakeState,
                    2 => midwayHandoffState,
                    3 => coralStowState,
                    _ => coralStowState,
                });
            }
            else if (!_canIntakeCoral)
            {
                coralController.SetTargetState(coralIntakeState);
            }

            if ((coralController.currentStateNum < coralStowState.stateNum || !coralController.atTarget) &&
                coralController.HasPiece())
            {
                if (CurrentSetpoint != ReefscapeSetpoints.Intake && CurrentSetpoint != ReefscapeSetpoints.Stow)
                    bufferedSetpoint = CurrentSetpoint;
                _canBuffer = false;
                SetState(ReefscapeSetpoints.Intake);
            }

            if (!_isScoring)
            {
                if (IntakeAction.IsPressed())
                {
                    // SWAPPED DIRECTION: Algae is now 1f, Coral is now -1f
                    float direction = (CurrentRobotMode == ReefscapeRobotMode.Algae) ? 1f : -1f;
                    foreach (var intakeWheel in intakeWheels)
                    {
                        intakeWheel.VelocityRoller(intakeWheelSpeed * direction);
                    }
                }
                else
                {
                    foreach (var intakeWheel in intakeWheels)
                    {
                        intakeWheel.VelocityRoller(0);
                    }
                }
            }
        }

        private IEnumerator PlaceCoroutine()
        {
            if (_alreadyPlaced) yield break;

            _isScoring = true;
            PlaceGamePiece();

            // Reversed logic: Scored speed is the opposite of whatever the current mode's intake direction is
            float currentIntakeDirection = (CurrentRobotMode == ReefscapeRobotMode.Algae) ? 1f : -1f;
            float scoreSpeed = -intakeWheelSpeed * currentIntakeDirection;

            float timer = 0;
            while (timer < 0.5f)
            {
                foreach (var wheel in intakeWheels) wheel.VelocityRoller(scoreSpeed);
                timer += Time.deltaTime;
                yield return null;
            }

            foreach (var wheel in intakeWheels) wheel.VelocityRoller(0);
            _isScoring = false;
        }

        private void PlaceGamePiece()
        {
            if (_alreadyPlaced)
            {
                return;
            }

            if (CurrentRobotMode == ReefscapeRobotMode.Coral && coralController.HasPiece())
            {
                Vector3 force = new Vector3(0, 0, -3.5f);
                float time = 0.0f;
                float maxSpeed = 0.5f;

                if (LastSetpoint == ReefscapeSetpoints.L4)
                {
                    time = 0.5f;
                    force = new Vector3(0, 0, -6.0f);
                }
                if (LastSetpoint == ReefscapeSetpoints.L1)
                {
                    time = 0.15f;
                    force = CurrentIntakeMode == ReefscapeIntakeMode.L1 ? new Vector3(0.5f, 0, 0) : new Vector3(0, 0, -0.25f);
                    maxSpeed = 0.01f;
                }
                if (LastSetpoint == ReefscapeSetpoints.L2)
                {
                    time = 0.15f;
                    force = new Vector3(0, 0, -6.0f);
                    maxSpeed = 0.6f;
                }
                else if (LastSetpoint == ReefscapeSetpoints.L3)
                {
                    time = 0.15f;
                    force = new Vector3(0, 0, -6.0f);
                    maxSpeed = 0.4f;
                }

                coralController.ReleaseGamePieceWithContinuedForce(force, time, maxSpeed);
                _alreadyPlaced = true;
            }
            else if ((CurrentRobotMode == ReefscapeRobotMode.Algae && algaeController.HasPiece()) || !coralController.HasPiece() && algaeController.HasPiece())
            {
                Vector3 force = new Vector3(0, 4.0f, 0);
                algaeController.ReleaseGamePieceWithForce(force);
                _alreadyPlaced = true;
            }
        }

        private void AlgaeSlider()
        {
            if (algaeIntake.GamePiece != null)
            {
                var localSliderSpaceX = algaeTarget.transform.InverseTransformPoint(algaeIntake.GamePiece.transform.position).x;
                algaeSlider.localPosition = new Vector3(localSliderSpaceX, 0, 0);
            }
        }

        private void UpdateAudio()
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                if (intakeAudioSource.isPlaying || algaeAudioSource.isPlaying)
                {
                    intakeAudioSource.Stop();
                    algaeAudioSource.Stop();
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

            if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum > 0 && !algaeAudioSource.isPlaying)
            {
                algaeAudioSource.Play();
            }
            else if (RobotGamePieceController.GetPieceByName("Algae").currentStateNum == 0 && algaeAudioSource.isPlaying)
            {
                algaeAudioSource.Stop();
            }
        }
    }

    [Serializable]
    public struct RobotnautsSetpoint
    {
        [Tooltip("Deg")] public float armAngle;
        [Tooltip("Inch")] public float elevatorHeight;
        [Tooltip("Deg")] public float intakeAngle;
        [Tooltip("Deg")] public float algaeArmsAngle;
    }
}