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

namespace Prefabs.Reefscape.Robots.Mods.TestingMod._9496
{
    public class Lynk: ReefscapeRobotBase
    {
        [Header("Components")]
        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint algaeArm;
        [SerializeField] private GenericJoint funnelFlap;
        [SerializeField] private GenericJoint climberBar;
        [SerializeField] private GenericJoint climberFlap;
        
        [Header("PIDS")]
        [SerializeField] private PidConstants algaeArmPid;
        [SerializeField] private PidConstants funnelFlapPid;
        [SerializeField] private PidConstants climberBarPid;
        [SerializeField] private PidConstants climberFlapPid;

        [Header("coral Setpoints")]
        [SerializeField] private LynkSetpoint stow;
        [SerializeField] private LynkSetpoint intake;
        [SerializeField] private LynkSetpoint l1;
        [SerializeField] private LynkSetpoint l1Place;
        [SerializeField] private LynkSetpoint l2;
        [SerializeField] private LynkSetpoint l3;
        [SerializeField] private LynkSetpoint l4;
        [SerializeField] private LynkSetpoint l4Place;
        
        [Header("algae Setpoints")]
        [SerializeField] private LynkSetpoint lowAlgae;
        [SerializeField] private LynkSetpoint highAlgae;
        [SerializeField] private LynkSetpoint bargePrep;
        [SerializeField] private LynkSetpoint bargePlace;
        
        [Header("Intake Componenets")]
        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;
        
        [Header("Game Piece States")]
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState algaeStowState;
        
        [Header("Algae Stall Audio")]
        [SerializeField] private AudioSource algaeStallSource;
        [SerializeField] private AudioClip algaeStallAudio;
        
        [Header("Robot Audio")]
        [SerializeField] private AudioSource rollerSource;
        [SerializeField] private AudioClip intakeClip;
        
        [Header("Funnel Close Audio")]
        [SerializeField] private AudioSource funnelCloseSource;
        [SerializeField] private AudioClip funnelCloseAudio;
        [SerializeField] private BoxCollider coralTrigger;
        private OverlapBoxBounds soundDetector;
        
        
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        private float _elevatorTargetHeight;
        private float _flapTargetAngle;
        private float _climbBarTargetAngle;
        private float _climbFlapTargetAngle;
        private LayerMask coralMask;
        private bool canClack;
        
        protected override void Start()
        {
            base.Start();
            
            algaeArm.SetPid(algaeArmPid);
            funnelFlap.SetPid(funnelFlapPid);
            climberBar.SetPid(climberBarPid);
            climberFlap.SetPid(climberFlapPid);

            _elevatorTargetHeight = 0;
            _flapTargetAngle = 0;
            _climbBarTargetAngle = 0;
            _climbFlapTargetAngle = 0;
            
            RobotGamePieceController.SetPreload(coralStowState);
            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[]
            {
                coralStowState
            };
            _coralController.intakes.Add(coralIntake);

            _algaeController.gamePieceStates = new[] {algaeStowState};
            _algaeController.intakes.Add(algaeIntake);
            
            algaeStallSource.clip = algaeStallAudio;
            algaeStallSource.loop = true;
            algaeStallSource.Stop();
            
            rollerSource.clip = intakeClip;
            rollerSource.loop = true;
            rollerSource.Stop();
            
            funnelCloseSource.clip = funnelCloseAudio;
            funnelCloseSource.loop = false;
            funnelCloseSource.Stop();

            soundDetector = new OverlapBoxBounds(coralTrigger);

            coralMask = LayerMask.GetMask("Coral");
            canClack = true;
        }

        private void LateUpdate()
        {
            algaeArm.UpdatePid(algaeArmPid);
            funnelFlap.UpdatePid(funnelFlapPid);
            climberBar.UpdatePid(climberBarPid);
            climberFlap.UpdatePid(climberFlapPid);
        }

        private void FixedUpdate()
        {
            bool hasAlgae = _algaeController.HasPiece();
            bool hasCoral = _coralController.HasPiece();
            
            _algaeController.SetTargetState(algaeStowState);
            _coralController.SetTargetState(coralStowState);
            
            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    SetSetpoint(stow);
                    break;
                case ReefscapeSetpoints.Intake:
                    SetSetpoint(intake);

                    _algaeController.RequestIntake(algaeIntake, CurrentRobotMode == ReefscapeRobotMode.Algae && !hasAlgae && !hasCoral);
                    _coralController.RequestIntake(coralIntake, !hasCoral && !hasAlgae);
                    break;
                case ReefscapeSetpoints.Place:
                    if (LastSetpoint == ReefscapeSetpoints.Barge)
                    {
                        SetSetpoint(bargePlace);
                    } 
                    else if (LastSetpoint == ReefscapeSetpoints.L4)
                    {
                        SetSetpoint(l4Place);
                    } else if (LastSetpoint == ReefscapeSetpoints.L1)
                    {
                        SetSetpoint(l1Place);
                    }
                    PlacePiece();
                    break;
                case ReefscapeSetpoints.L1:
                    SetSetpoint(l1);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetSetpoint(intake);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae && !hasCoral);
                    _coralController.RequestIntake(coralIntake, false);
                    break;
                case ReefscapeSetpoints.L2:
                    SetSetpoint(l2);
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    SetSetpoint(lowAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae && !hasCoral);
                    _coralController.RequestIntake(coralIntake, false);
                    break;
                case ReefscapeSetpoints.L3:
                    SetSetpoint(l3);
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    SetSetpoint(highAlgae);
                    _algaeController.RequestIntake(algaeIntake, IntakeAction.IsPressed() && !hasAlgae && !hasCoral);
                    _coralController.RequestIntake(coralIntake, false);
                    break;
                case ReefscapeSetpoints.L4:
                    SetSetpoint(l4);
                    break;
                case ReefscapeSetpoints.Processor:
                    SetSetpoint(stow);
                    break;
                case ReefscapeSetpoints.Barge:
                    SetSetpoint(bargePrep);
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetState(ReefscapeSetpoints.Stow);
                    break;
                case ReefscapeSetpoints.Climb:
                    _climbBarTargetAngle = -120;
                    _climbFlapTargetAngle = -115;
                    _flapTargetAngle = -110;
                    break;
                case ReefscapeSetpoints.Climbed:
                    _climbBarTargetAngle = 5;
                    break;
            }
            
            UpdateSetpoints();
            UpdateAudio();
        }

        private void PlacePiece()
        {
            if (_algaeController.HasPiece())
            {
                if (LastSetpoint == ReefscapeSetpoints.Barge)
                {
                    _algaeController.ReleaseGamePieceWithForce(new Vector3(0, 10, 1.5f));
                }
                else
                {
                    _algaeController.ReleaseGamePieceWithForce(new Vector3(0, 0, 1.5f));
                }
            }
            else
            {
                if (LastSetpoint == ReefscapeSetpoints.L4)
                {
                    _coralController.ReleaseGamePieceWithContinuedForce(new Vector3(0, 0, 5.5f), 1f, 0.5f);
                }
                else if (LastSetpoint == ReefscapeSetpoints.L1)
                {
                    _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 2));
                }
                else
                {
                    _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 6));
                }
            }
        }

        private void SetSetpoint(LynkSetpoint setpoint)
        {
            _elevatorTargetHeight = setpoint.elevatorHeight;
        }

        private void UpdateSetpoints()
        {
            elevator.SetTarget(_elevatorTargetHeight);
            algaeArm.SetTargetAngle(0).withAxis(JointAxis.X);
            funnelFlap.SetTargetAngle(_flapTargetAngle).withAxis(JointAxis.X);
            climberBar.SetTargetAngle(_climbBarTargetAngle).withAxis(JointAxis.X);
            climberFlap.SetTargetAngle(_climbFlapTargetAngle).withAxis(JointAxis.X);
        }

        private void UpdateAudio()
        {
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
            {
                if (rollerSource.isPlaying || algaeStallSource.isPlaying)
                {
                    rollerSource.Stop();
                    algaeStallSource.Stop();
                }

                return;
            }

            if (((IntakeAction.IsPressed() && !_coralController.HasPiece() && !_coralController.HasPiece()) ||
                 OuttakeAction.IsPressed()) &&
                !rollerSource.isPlaying)
            {
                rollerSource.Play();
            }
            else if (!IntakeAction.IsPressed() && !OuttakeAction.IsPressed() && rollerSource.isPlaying)
            {
                rollerSource.Stop();
            }
            else if (IntakeAction.IsPressed() && (_coralController.HasPiece() || _algaeController.HasPiece()))
            {
                rollerSource.Stop();
            }

            if (_algaeController.HasPiece() && !algaeStallSource.isPlaying)
            {
                algaeStallSource.Play();
            }
            else if (!_algaeController.HasPiece() && algaeStallSource.isPlaying)
            {
                algaeStallSource.Stop();
            }


            var a = soundDetector.OverlapBox(coralMask);
            if (a.Length > 0)
            {
                if (canClack && !funnelCloseSource.isPlaying)
                {
                    funnelCloseSource.Play();
                    canClack = false;
                }
            }
            else
            {
                canClack = true;
            }
        }
    }
}