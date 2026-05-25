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

        [SerializeField] private PidConstants armPid;
        [SerializeField] private PidConstants elevatorPid;
        [SerializeField] private PidConstants climbArmPid;
        [SerializeField] private PidConstants climbWristPid;
        [SerializeField] private PidConstants droppyThingPid;

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


        [SerializeField] private float vertical;
        [SerializeField] private float horizontal;
        [SerializeField] private float bargeDelay;

        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState coralL4State;
        [SerializeField] private GamePieceState algaeStowState;
        [SerializeField] private GenericAnimationJoint[] algaeRollers;
        [SerializeField] private GenericAnimationJoint[] topCoralRollers;
        [SerializeField] private GenericAnimationJoint[] bottomCoralRollers;
        [SerializeField] private GenericRoller[] rollers;
        [SerializeField] private Collider[] EEcolliders;

        [SerializeField] private AudioSource algaeStallSource;
        [SerializeField] private AudioClip algaeStallAudio;
        [SerializeField] private AudioSource algaeRollerSource;
        [SerializeField] private AudioClip algaeRollerAudio;
        [SerializeField] private AudioSource coralRollerSource;
        [SerializeField] private AudioClip coralRollerAudio;
        private float _algaeRollerTargetSpeed;
        private float _coralRollerTargetSpeed;

        private bool algaePlaced = false;
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
            Debug.Log(_armTargetAngle);
            arm.SetTargetAngle(_armTargetAngle).noWrap(10).withAxis(JointAxis.X);
            climbArm.SetTargetAngle(_climbArmTargetAngle).withAxis(JointAxis.X);
            climbWrist.SetTargetAngle(_climbWristTargetAngle).withAxis(JointAxis.X);
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
            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();
            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    SetSetpoint(stow);
                    UpdateAlgaeRollers(0);
                    intakeSetpoint = "Coral";
                    break;
                case ReefscapeSetpoints.Intake:
                    // _algaeController.RequestIntake(algaeIntake, true);
                    _coralController.RequestIntake(coralIntake, true);
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
                    if (LastSetpoint == ReefscapeSetpoints.Barge)
                    {
                        StartCoroutine(ScoreBargeAlgae());
                        algaePlaced = true;

                        break;
                    }


                    PlacePiece();
                    break;
                case ReefscapeSetpoints.L1:
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
                    _coralController.SetTargetState(coralStowState);
                    SetSetpoint(l2);
                    UpdateAlgaeRollers(0);
                    UpdateCoralRollers(0);
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(lowAlgae);
                    intakeSetpoint = "Algae";
                    //UpdateAlgaeRollers(-300);
                    UpdateCoralRollers(0);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed());
                    UpdateAlgaeRollers(IntakeAction.IsPressed() ? -300 : 0);
                    break;
                case ReefscapeSetpoints.L3:
                    _coralController.SetTargetState(coralStowState);
                    UpdateAlgaeRollers(0);
                    UpdateCoralRollers(0);
                    SetSetpoint(l3);
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    intakeSetpoint = "Algae";
                    SetSetpoint(highAlgae);
                    //UpdateAlgaeRollers(-300);
                    UpdateCoralRollers(0);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed());
                    UpdateAlgaeRollers(IntakeAction.IsPressed() ? -300 : 0);
                    break;
                case ReefscapeSetpoints.L4:
                    UpdateCoralRollers(0);
                    SetSetpoint(l4);
                    _coralController.SetTargetState(coralL4State);
                    break;
                case ReefscapeSetpoints.Processor:
                    break;
                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargePrep);
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetState(ReefscapeSetpoints.Stow);
                    break;
                case ReefscapeSetpoints.Climb:
                    SetSetpoint(climb);
                    break;
                case ReefscapeSetpoints.Climbed:
                    SetSetpoint(climbed);
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
                algaePlaced = true;
                SetSetpoint(bargePlace);
                foreach (var col in EEcolliders)
                {
                    col.enabled = false;
                }
                UpdateAlgaeRollers(300);

                yield return new WaitForSeconds(bargeDelay);

                PlacePiece();

                yield return new WaitForSeconds(0.3f);
                UpdateAlgaeRollers(0);
                foreach (var col in EEcolliders)
                {
                    col.enabled = true;
                }
                Debug.Log("Scored algae on barge");
                _armTargetAngle += 1;
                SetSetpoint(stow);
                algaePlaced = false;
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
        private void PlacePiece()
        {
            if (_algaeController.HasPiece() && ((CurrentRobotMode == ReefscapeRobotMode.Algae) || (!_coralController.HasPiece() && LastSetpoint == ReefscapeSetpoints.Barge)))
            {
                _algaeController.ReleaseGamePieceWithForce(new Vector3(0, horizontal, vertical));

                StartCoroutine(SpinAlgaeRollers());


            }
            else if (CurrentRobotMode == ReefscapeRobotMode.Algae)
            {
                _algaeController.ReleaseGamePieceWithForce(new Vector3(0, 2, 0));
                StartCoroutine(SpinAlgaeRollers());
            }


            else
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
            }
        }
    }
}
