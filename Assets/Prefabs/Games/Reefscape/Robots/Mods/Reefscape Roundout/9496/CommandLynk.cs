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
    public class CommandLynk : ReefscapeCommandRobotBase
    {
        [Header("Components")] [SerializeField]
        private GenericElevator elevator;

        [SerializeField] private GenericJoint algaeArm;
        [SerializeField] private GenericJoint funnelFlap;
        [SerializeField] private GenericJoint climberBar;
        [SerializeField] private GenericJoint climberFlap;

        [Header("PIDS")] [SerializeField] private PidConstants algaeArmPid;
        [SerializeField] private PidConstants funnelFlapPid;
        [SerializeField] private PidConstants climberBarPid;
        [SerializeField] private PidConstants climberFlapPid;

        [Header("coral Setpoints")] [SerializeField]
        private LynkSetpoint stow;

        [SerializeField] private LynkSetpoint intake;
        [SerializeField] private LynkSetpoint l1;
        [SerializeField] private LynkSetpoint l1Place;
        [SerializeField] private LynkSetpoint l2;
        [SerializeField] private LynkSetpoint l3;
        [SerializeField] private LynkSetpoint l4;
        [SerializeField] private LynkSetpoint l4Place;

        [Header("algae Setpoints")] [SerializeField]
        private LynkSetpoint lowAlgae;

        [SerializeField] private LynkSetpoint highAlgae;
        [SerializeField] private LynkSetpoint bargePrep;
        [SerializeField] private LynkSetpoint bargePlace;

        [Header("Intake Componenets")] [SerializeField]
        private ReefscapeGamePieceIntake coralIntake;

        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Game Piece States")] [SerializeField]
        private GamePieceState coralStowState;

        [SerializeField] private GamePieceState algaeStowState;

        [Header("Algae Stall Audio")] [SerializeField]
        private AudioSource algaeStallSource;

        [SerializeField] private AudioClip algaeStallAudio;

        [Header("Robot Audio")] [SerializeField]
        private AudioSource rollerSource;

        [SerializeField] private AudioClip intakeClip;

        [Header("Funnel Close Audio")] [SerializeField]
        private AudioSource funnelCloseSource;

        [SerializeField] private AudioClip funnelCloseAudio;
        [SerializeField] private BoxCollider coralTrigger;
        private OverlapBoxBounds soundDetector;

        private float _elevatorTargetHeight;
        private float _flapTargetAngle;
        private float _climbBarTargetAngle;
        private float _climbFlapTargetAngle;
        private LayerMask coralMask;

        private bool canClack;
        
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        // Start is called before the first frame update
        protected override void Init()
        {
            algaeArm.SetPid(algaeArmPid);
            funnelFlap.SetPid(funnelFlapPid);
            climberBar.SetPid(climberBarPid);
            climberFlap.SetPid(climberFlapPid);
            
            SetupGamePieceController();
            SetStartupValues();
            SetupStates();
            
            
            Scheduler.AddProtectedSystem(UpdateJoints, UpdateAudio);
            Scheduler.AddLateRecursive(UpdatePids);
        }

        private void SetupGamePieceController()
        {
            var coralGraph = new GamePieceGraph()
                .AddNode(coralStowState, isPreload: true);
            var algaeGraph = new GamePieceGraph()
                .AddNode(algaeStowState);
            
            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _coralController.gamePieceStates = coralGraph.GetStates();
            _coralController.intakes.Add(coralIntake);
            
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());
            _algaeController.gamePieceStates = algaeGraph.GetStates();
            _algaeController.intakes.Add(algaeIntake);
            
            RobotGamePieceController.SetPreload(coralStowState);
            
            CoralPathController.SetGraph(coralGraph, coralStowState);
            AlgaePathController.SetGraph(algaeGraph, algaeStowState);
        }

        private void SetStartupValues()
        {
            _elevatorTargetHeight = 0;
            _flapTargetAngle = 0;
            _climbBarTargetAngle = 0;
            _climbFlapTargetAngle = 0;
            
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
        
        private void SetSetpoint(LynkSetpoint setpoint)
        {
            _elevatorTargetHeight = setpoint.elevatorHeight;
        }

        private void UpdatePids()
        {
            algaeArm.UpdatePid(algaeArmPid);
            funnelFlap.UpdatePid(funnelFlapPid);
            climberBar.UpdatePid(climberBarPid);
            climberFlap.UpdatePid(climberFlapPid);
        }
        
        private void UpdateJoints()
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

        private void StowPieces(bool requestIntake = false, bool dontCoralIntake = false)
        {
            AlgaePathController.SendTo(algaeStowState);
            CoralPathController.SendTo(coralStowState);

            if (requestIntake)
            {
                _algaeController.RequestIntake(algaeIntake, (CurrentRobotMode == ReefscapeRobotMode.Algae || dontCoralIntake) && !_algaeController.HasPiece() && !_coralController.HasPiece());
                _coralController.RequestIntake(coralIntake, !_algaeController.HasPiece() && !_coralController.HasPiece() && !dontCoralIntake);
            }
            else
            {
                _algaeController.RequestIntake(algaeIntake, false);
                _coralController.RequestIntake(coralIntake, false);
            }
        }
        
        private void PlacePiece()
        {
            if (_algaeController.HasPiece())
            {
                if (WasLastState<BargeState>())
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
                if (WasLastState<L4State>())
                {
                    _coralController.ReleaseGamePieceWithContinuedForce(new Vector3(0, 0, 5.5f), 1f, 0.5f);
                }
                else if (WasLastState<L1State>())
                {
                    _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 2));
                }
                else
                {
                    _coralController.ReleaseGamePieceWithForce(new Vector3(0, 0, 6));
                }
            }
        }

        private void SetupStates()
        {
            InState<StowState>()
                .OnMechanisms(() => SetSetpoint(stow))
                .OnGamePiece(() => StowPieces());

            InState<IntakeState>()
                .OnMechanisms(() => SetSetpoint(intake))
                .OnGamePiece(() => StowPieces(IntakeAction));

            InState<PlaceState>()
                .OnMechanisms(() =>
                {
                    if (WasLastState<BargeState>())
                    {
                        SetSetpoint(bargePlace);
                    } 
                    else if (WasLastState<L4State>())
                    {
                        SetSetpoint(l4Place);
                    } else if (WasLastState<L1State>())
                    {
                        SetSetpoint(l1Place);
                    }
                })
                .OnGamePiece(PlacePiece);
            
            InState<L1State>()
                .OnMechanisms(() => SetSetpoint(l1))
                .OnGamePiece(() => StowPieces());
            
            InState<L2State>()
                .OnMechanisms(() => SetSetpoint(l2))
                .OnGamePiece(() => StowPieces());
            
            InState<L3State>()
                .OnMechanisms(() => SetSetpoint(l3))
                .OnGamePiece(() => StowPieces());
            
            InState<L4State>()
                .OnMechanisms(() => SetSetpoint(l4))
                .OnGamePiece(() => StowPieces());
            
            InState<StackState>()
                .OnMechanisms(() => SetSetpoint(intake))
                .OnGamePiece(() => StowPieces(IntakeAction, true));
            
            InState<LowAlgaeState>()
                .OnMechanisms(() => SetSetpoint(lowAlgae))
                .OnGamePiece(() => StowPieces(IntakeAction, true));
            
            InState<HighAlgaeState>()
                .OnMechanisms(() => SetSetpoint(highAlgae))
                .OnGamePiece(() => StowPieces(IntakeAction, true));
            
            InState<BargeState>()
                .OnMechanisms(() => SetSetpoint(bargePrep))
                .OnGamePiece(() => StowPieces());
            
            InState<RobotSpecialState>()
                .OnCheckTransition(() => new StowState(this));
            
            InState<ClimbState>()
                .OnMechanisms(() =>
                {
                    _climbBarTargetAngle = -120;
                    _climbFlapTargetAngle = -115;
                    _flapTargetAngle = -110;
                })
                .OnGamePiece(() => StowPieces());
            
            InState<ClimbedState>()
                .OnMechanisms(() =>
                {
                    _climbBarTargetAngle = 5;
                })
                .OnGamePiece(() => StowPieces());
        }

    }
}
