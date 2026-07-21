using System.Collections;
using Games.Reefscape.Enums;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework.Components;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using RobotFramework.GamePieceSystem;
using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods._4481.Remb
{
    public class Rembrant : ReefscapeRobotBase
    {

        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint arm;
        [SerializeField] private GenericJoint climbArm;
        [SerializeField] private GenericJoint climbWrist;
        [SerializeField] private GenericJoint droppyThing;
        [SerializeField] private RembrandtClimber climbHitboxes;

        [Header("Pids")]
        [SerializeField] private PidConstants armPid;
        [SerializeField] private PidConstants elevatorPid;
        [SerializeField] private PidConstants climbArmPid;
        [SerializeField] private PidConstants climbWristPid;
        [SerializeField] private PidConstants droppyThingPid;

        [Header("Setpoints")]
        [SerializeField] private Rembrantsetpoint stow;
        [SerializeField] private Rembrantsetpoint l1;
        [SerializeField] private Rembrantsetpoint l2;
        [SerializeField] private Rembrantsetpoint l3;
        [SerializeField] private Rembrantsetpoint l4;
        [SerializeField] private Rembrantsetpoint lowAlgae;
        [SerializeField] private Rembrantsetpoint highAlgae;
        [SerializeField] private Rembrantsetpoint bargePrep;
        [SerializeField] private Rembrantsetpoint bargePlace;
        [SerializeField] private Rembrantsetpoint groundAlgae;
        [SerializeField] private Rembrantsetpoint lollipop;
        [SerializeField] private Rembrantsetpoint climb;
        [SerializeField] private Rembrantsetpoint climbed;
        [SerializeField] private Rembrantsetpoint climbRetract1;
        [SerializeField] private Rembrantsetpoint climbRetract2;

        [Header("Barge Algae Variables")]
        [SerializeField] private float vertical;
        [SerializeField] private float horizontal;
        [SerializeField] private float bargeDelay;

        [Header("Intakes")]
        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Game Piece States")]
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState coralL4State;
        [SerializeField] private GamePieceState algaeStowState;
        [SerializeField] private GenericAnimationJoint[] algaeRollers;
        [SerializeField] private GenericAnimationJoint[] topCoralRollers;
        [SerializeField] private GenericAnimationJoint[] bottomCoralRollers;
        [SerializeField] private GenericRoller[] rollers;
        [SerializeField] private Collider[] EEcolliders;

        [Header("Audio")]
        [SerializeField] private AudioSource algaeStallSource;
        [SerializeField] private AudioClip algaeStallAudio;
        [SerializeField] private AudioSource algaeRollerSource;
        [SerializeField] private AudioClip algaeRollerAudio;
        [SerializeField] private AudioSource coralRollerSource;
        [SerializeField] private AudioClip coralRollerAudio;
        private float _algaeRollerTargetSpeed;
        private float _coralRollerTargetSpeed;

        private bool algaePlaced = false;
        private bool retractingClimb = false;
        private bool climbing = false;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;




        private float _elevatorTargetHeight;
        private float _armTargetAngle;
        private float _climbArmTargetAngle;
        private float _climbWristTargetAngle;
        private float _droppyThingTargetAngle;
        private string intakeSetpoint;
        private bool _alreadyPlaced = false;
        private bool algaeSpinning = false;

        protected override void Start()
        {
            RobotGamePieceController.SetPreload(coralStowState);
            base.Start();
            algaePlaced = false;
            arm.SetPid(armPid);
            climbArm.SetPid(climbArmPid);
            climbWrist.SetPid(climbWristPid);
            droppyThing.SetPid(droppyThingPid);
            _elevatorTargetHeight = 0;
            _armTargetAngle = 0;
            _climbArmTargetAngle = 0;
            _climbWristTargetAngle = 0;
            _droppyThingTargetAngle = 0;

            RobotGamePieceController.SetPreload(coralStowState);
            SetRobotMode(ReefscapeRobotMode.Coral);

            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[]
            {
        coralStowState,
        coralL4State
        };
            _coralController.intakes.Add(coralIntake);

            _algaeController.gamePieceStates = new[] { algaeStowState };
            _algaeController.intakes.Add(algaeIntake);
            algaeStallSource.clip = algaeStallAudio;
            algaeStallSource.loop = true;
            algaeStallSource.Stop();
            coralRollerSource.clip = coralRollerAudio;
            coralRollerSource.loop = true;
            coralRollerSource.Stop();
            algaeRollerSource.clip = algaeRollerAudio;
            algaeRollerSource.loop = true;
            algaeRollerSource.Stop();
        }

        private void SetSetpoint(Rembrantsetpoint setpoint)
        {
            _elevatorTargetHeight = setpoint.elevatorHeight;
            _armTargetAngle = setpoint.armAngle;
            _climbArmTargetAngle = setpoint.climbArmAngle;
            _climbWristTargetAngle = setpoint.climbWristAngle;
            _droppyThingTargetAngle = setpoint.droppyThingAngle;
        }

        private void UpdateSetpoints()
        {
            elevator.SetTarget(_elevatorTargetHeight);
            arm.SetTargetAngle(_armTargetAngle).noWrap(10).withAxis(JointAxis.X);
            climbArm.SetTargetAngle(_climbArmTargetAngle).withAxis(JointAxis.X);
            climbWrist.SetTargetAngle(_climbWristTargetAngle).noWrap(90).withAxis(JointAxis.X);
            droppyThing.SetTargetAngle(_droppyThingTargetAngle).withAxis(JointAxis.X);
            foreach (var roller in algaeRollers)
            {
                roller.VelocityRoller(_algaeRollerTargetSpeed);
            }
            foreach (var roller in topCoralRollers)
            {
                roller.VelocityRoller(-_coralRollerTargetSpeed);
            }
            foreach (var roller in bottomCoralRollers)
            {
                roller.VelocityRoller(_coralRollerTargetSpeed);
            }
        }




        private void LateUpdate()
        {
            arm.UpdatePid(armPid);
        }
        private void UpdateAlgaeRollers(float speed)
        {
            _algaeRollerTargetSpeed = speed;

        }
        private void UpdateCoralRollers(float speed)
        {
            _coralRollerTargetSpeed = speed;
        }
        private void FixedUpdate()
        {
            if (CurrentSetpoint != ReefscapeSetpoints.Place) _alreadyPlaced = false;
            if (!IntakeAction.IsPressed())
            {
                UpdateAlgaeRollers(0);
                UpdateCoralRollers(0);
            }
            if (!OuttakeAction.IsPressed())
            {
                algaePlaced = false;
            }
            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();
            if (retractingClimb && !climbing)
            {
                retractingClimb = false;
            }
            CurrentCoralStationMode.DropDistance = 0;
            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    if (LastSetpoint != ReefscapeSetpoints.Climbed && !climbing)
                    {
                        SetSetpoint(stow);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                        break;
                    }
                    UpdateAlgaeRollers(0);
                    intakeSetpoint = "Coral";
                    break;
                case ReefscapeSetpoints.Intake:
                    if (climbing)
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                        break;
                    }
                    if (ArmAtSetpoint(stow) && ElevatorAtSetpoint(stow))
                    {
                        CurrentCoralStationMode.DropDistance = 1.45f;
                        _coralController.RequestIntake(coralIntake, true);
                    }
                    _coralController.SetTargetState(coralStowState);
                    if ((CurrentRobotMode == ReefscapeRobotMode.Coral && !hasCoral) ||
                    (LastSetpoint == ReefscapeSetpoints.Barge && !hasAlgae) ||
                    (CurrentRobotMode == ReefscapeRobotMode.Algae && hasAlgae))
                    {
                        SetSetpoint(stow);
                        intakeSetpoint = "Coral";
                    }
                    UpdateCoralRollers(IntakeAction.IsPressed() ? -500 : 0);
                    UpdateAlgaeRollers(0);
                    break;
                case ReefscapeSetpoints.Place:
                    if (climbing)
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                        break;
                    }
                    if (LastSetpoint == ReefscapeSetpoints.Barge)
                    {
                        StartCoroutine(ScoreBargeAlgae());

                        break;
                    }
                    algaePlaced = true;


                    PlacePiece();
                    break;
                case ReefscapeSetpoints.L1:
                    if (climbing)
                    {
                        if (!retractingClimb)
                        {
                            StartCoroutine(RetractClimb());
                            retractingClimb = true;
                        }
                        break;
                    }
                    UpdateAlgaeRollers(0);
                    UpdateCoralRollers(0);
                    SetSetpoint(l1);
                    _coralController.SetTargetState(coralStowState);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetSetpoint(lollipop);
                    UpdateAlgaeRollers(0);
                    break;
                case ReefscapeSetpoints.L2:
                    if (!climbing)
                    {
                        _coralController.SetTargetState(coralStowState);
                        SetSetpoint(l2);
                        UpdateAlgaeRollers(0);
                        UpdateCoralRollers(0);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    if (!climbing)
                    {
                        SetSetpoint(lowAlgae);
                        intakeSetpoint = "Algae";
                        //UpdateAlgaeRollers(-300);
                        UpdateCoralRollers(0);
                        _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed());
                        UpdateAlgaeRollers(IntakeAction.IsPressed() ? -300 : 0);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }

                    break;
                case ReefscapeSetpoints.L3:
                    if (!climbing)
                    {
                        _coralController.SetTargetState(coralStowState);
                        UpdateAlgaeRollers(0);
                        UpdateCoralRollers(0);
                        SetSetpoint(l3);

                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    if (!climbing)
                    {
                        intakeSetpoint = "Algae";
                        SetSetpoint(highAlgae);
                        //UpdateAlgaeRollers(-300);
                        UpdateCoralRollers(0);
                        _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed());
                        UpdateAlgaeRollers(IntakeAction.IsPressed() ? -300 : 0);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }
                    break;
                case ReefscapeSetpoints.L4:
                    if (!climbing)
                    {
                        _coralController.SetTargetState(coralStowState);
                        UpdateCoralRollers(0);
                        SetSetpoint(l4);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }

                    _coralController.SetTargetState(coralL4State);
                    break;
                case ReefscapeSetpoints.Processor:
                    break;
                case ReefscapeSetpoints.Barge:
                    if (!climbing)
                    {
                        SetSetpoint(bargePrep);
                    }
                    else
                    {
                        StartCoroutine(RetractClimb());
                        retractingClimb = true;
                    }
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetState(ReefscapeSetpoints.Stow);
                    break;
                case ReefscapeSetpoints.Climb:
                    climbing = true;
                    SetSetpoint(climb);
                    retractingClimb = false;
                    break;
                case ReefscapeSetpoints.Climbed:
                    climbing = true;
                    SetSetpoint(climbed);
                    retractingClimb = false;
                    break;
            }
            UpdateSetpoints();
            UpdateAudio();
        }

        private void UpdateAudio()
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                if (coralRollerSource.isPlaying || algaeStallSource.isPlaying)
                {
                    coralRollerSource.Stop();
                    algaeStallSource.Stop();
                }

                return;
            }
            if (((CurrentSetpoint != ReefscapeSetpoints.LowAlgae && CurrentSetpoint != ReefscapeSetpoints.HighAlgae && IntakeAction.IsPressed() && !_algaeController.HasPiece() && !_coralController.HasPiece()) || OuttakeAction.IsPressed()) && !coralRollerSource.isPlaying)
            {
                coralRollerSource.Play();

            }
            else if (!IntakeAction.IsPressed() && !OuttakeAction.IsPressed() && coralRollerSource.isPlaying)
            {
                coralRollerSource.Stop();
            }


            else if (IntakeAction.IsPressed() && _coralController.HasPiece() && coralRollerSource.isPlaying)
            {
                coralRollerSource.Stop();
            }
            else if (IntakeAction.IsPressed() && _algaeController.HasPiece() && algaeRollerSource.isPlaying)
            {
                algaeRollerSource.Stop();
            }
            else if ((CurrentSetpoint == ReefscapeSetpoints.LowAlgae || CurrentSetpoint == ReefscapeSetpoints.HighAlgae) && IntakeAction.IsPressed() && !_algaeController.HasPiece() && !algaeRollerSource.isPlaying)
            {
                algaeRollerSource.Play();
            }
            else if (!IntakeAction.IsPressed() && !OuttakeAction.IsPressed() && algaeRollerSource.isPlaying)
            {
                algaeRollerSource.Stop();
            }
            if (_algaeController.HasPiece() && !algaeStallSource.isPlaying)
            {
                algaeStallSource.Play();
            }
            else if (!_algaeController.HasPiece() && algaeStallSource.isPlaying)
            {
                algaeStallSource.Stop();
            }
        }

        public IEnumerator ScoreBargeAlgae()
        {
            if (!algaePlaced)
            {
                SetSetpoint(bargePlace);
                foreach (var col in EEcolliders)
                {
                    col.enabled = false;
                }
                UpdateAlgaeRollers(300);

                yield return new WaitForSeconds(bargeDelay);

                PlacePiece();

                yield return new WaitForSeconds(0.3f);
                foreach (var col in EEcolliders)
                {
                    col.enabled = true;
                }

            }
        }
        public IEnumerator SpinCoralRollers()
        {
            UpdateCoralRollers(-300);
            yield return new WaitForSeconds(1f);
            UpdateCoralRollers(0);
        }
        public IEnumerator SpinAlgaeRollers()
        {
            if (!algaeSpinning)
            {
                algaeSpinning = true;
                UpdateAlgaeRollers(300);
                yield return new WaitForSeconds(0.9f);
                UpdateAlgaeRollers(0);
                algaeSpinning = false;
            }
        }
        public IEnumerator RetractClimb()
        {
            if (!retractingClimb)
            {
                climbHitboxes.forceDeClimb();
                SetSetpoint(climbRetract1);
                while (!ClimbWristAtSetpoint(climbRetract1))
                {
                    yield return new WaitForSeconds(0.1f); // Wait 100ms before checking again
                }
                SetSetpoint(climbRetract2);
                while (!ClimbArmAtSetpoint(climbRetract2))
                {
                    yield return new WaitForSeconds(0.1f); // Wait 100ms before checking again
                }
                SetSetpoint(stow);
                retractingClimb = false;
                climbing = false;
            }
        }
        private void PlacePiece()
        {
            if (_alreadyPlaced) return;
            if (CurrentRobotMode == ReefscapeRobotMode.Coral && _coralController.HasPiece())
            {
                if (LastSetpoint == ReefscapeSetpoints.L4)
                {
                    _coralController.ReleaseGamePieceWithContinuedForce(new Vector3(0, 0, -5), 0.2f, 0.5f);
                    StartCoroutine(SpinCoralRollers());
                }
                else if (LastSetpoint == ReefscapeSetpoints.L3 || LastSetpoint == ReefscapeSetpoints.L2)
                {
                    _coralController.ReleaseGamePieceWithContinuedForce(new Vector3(0, 0, 7), 0.15f, 0.9f);
                    StartCoroutine(SpinCoralRollers());

                }
                else if (CurrentRobotMode == ReefscapeRobotMode.Coral)
                {
                    _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 5));
                    StartCoroutine(SpinCoralRollers());
                }
                _alreadyPlaced = true;
            }
            else if (_algaeController.HasPiece() && LastSetpoint == ReefscapeSetpoints.Barge)
            {

                _algaeController.ReleaseGamePieceWithForce(new Vector3(0, horizontal, vertical));
                _alreadyPlaced = true;



            }
            else if ((CurrentRobotMode == ReefscapeRobotMode.Algae && _algaeController.HasPiece()) || (!_coralController.HasPiece() && _algaeController.HasPiece()))
            {
                _algaeController.ReleaseGamePieceWithForce(new Vector3(0, 0, 1.5f));
                _alreadyPlaced = true;
            }

        }

        private bool ElevatorAtSetpoint(Rembrantsetpoint targetSetpoint)
        {
            bool elevatorAtSetpoint = Utils.InRange(elevator.GetElevatorHeight(), targetSetpoint.elevatorHeight, 2f);

            return elevatorAtSetpoint;
        }

        private bool ArmAtSetpoint(Rembrantsetpoint targetSetpoint)
        {
            bool armAtSetpoint = Utils.InAngularRange(arm.GetSingleAxisAngle(JointAxis.X), targetSetpoint.armAngle, 2f);

            return armAtSetpoint;
        }



        private bool ClimbWristAtSetpoint(Rembrantsetpoint targetSetpoint)
        {
            bool climbAtSetpoint = Utils.InAngularRange(climbWrist.GetSingleAxisAngle(JointAxis.X), targetSetpoint.climbWristAngle, 2f);

            return climbAtSetpoint;
        }
        private bool ClimbArmAtSetpoint(Rembrantsetpoint targetSetpoint)
        {
            bool climbAtSetpoint = Utils.InAngularRange(climbArm.GetSingleAxisAngle(JointAxis.X), targetSetpoint.climbArmAngle, 2f);

            return climbAtSetpoint;
        }
    }
}
