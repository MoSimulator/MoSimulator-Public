using MoSimCore.Enums;
using RobotFramework.Controllers.GamePieceSystem;
using Games.Reefscape.Enums;
using Games.Reefscape.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using UnityEngine;

namespace RobotFramework.Controllers.Lighting
{
    public class LedStripController : MonoBehaviour
    {
        // Start is called before the first frame update
        public GameObject[] leds;
        private Material LEDs;
        public Shader shaderGraphShader;
        public Texture hasAlgae;
        public Texture Disabled;
        public Texture Intake;
        public Texture hasCoral;
        public Texture CoralMode;
        public Texture AlgaeMode;
        public Texture L1Mode;
        public Texture prepClimb;
        public Texture Climbed;
        public Texture AutoAligning;
        public Texture Processor;

        protected RobotGamePieceControllerBase GamePieceManager { get; private set; }
        protected Games.Reefscape.Robots.ReefscapeRobotBase ReefscapeRobotBase { get; private set; }
        protected Games.Reefscape.Robots.ReefscapeCommandRobotBase ReefscapeCommandRobotBase { get; private set; }
        
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;
        
        private void Start()
        {
            GamePieceManager = GetComponent<RobotGamePieceControllerBase>();
            ReefscapeRobotBase = GetComponent<Games.Reefscape.Robots.ReefscapeRobotBase>();
            ReefscapeCommandRobotBase = GetComponent<Games.Reefscape.Robots.ReefscapeCommandRobotBase>();
            LEDs = new Material(shaderGraphShader);
            
            foreach (var led in leds)
            {
                led.GetComponent<Renderer>().material = LEDs;
            }
            
            // Initialize the controllers like in Lynk script
            var robotGamePieceController = GetComponent<RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>>();
            if (robotGamePieceController != null)
            {
                _coralController = robotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
                _algaeController = robotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());
            }
        }
        
        private void Update()
        {
            // Check for pieces using the controllers like in Lynk script
            bool hasAlgaePiece = _algaeController != null && _algaeController.HasPiece();
            bool hasCoralPiece = _coralController != null && _coralController.HasPiece();
            if (ReefscapeRobotBase != null)
            {
                if ((ReefscapeRobotBase is null || GamePieceManager is null) ||
                    BaseGameManager.Instance.RobotState == RobotState.Disabled)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Disabled);
                }
                else if (ReefscapeRobotBase.CurrentSetpoint == ReefscapeSetpoints.Climb)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", prepClimb);
                }
                else if (ReefscapeRobotBase.CurrentSetpoint == ReefscapeSetpoints.Climbed)
                {
                    LEDs.SetFloat("_X", 3f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Climbed);
                }
                else if (ReefscapeRobotBase.AutoAlignLeftAction.IsPressed() ||
                         ReefscapeRobotBase.AutoAlignRightAction.IsPressed())
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 0);
                    LEDs.SetTexture("_Texture2D", AutoAligning);
                }
                else if (hasAlgaePiece && ReefscapeRobotBase.CurrentSetpoint == ReefscapeSetpoints.Processor)
                {
                    LEDs.SetFloat("_X", 1.5f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Processor);
                }
                else if (hasAlgaePiece)
                {
                    LEDs.SetFloat("_X", 0f);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", hasAlgae);
                }
                else if (hasCoralPiece)
                {
                    LEDs.SetFloat("_X", 1.5f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", hasCoral);
                }
                else if (ReefscapeRobotBase.IntakeAction.IsPressed())
                {
                    if (ReefscapeRobotBase.CurrentIntakeMode == ReefscapeIntakeMode.Normal)
                    {
                        LEDs.SetFloat("_X", 0);
                        LEDs.SetFloat("_Y", 0.0f);
                        LEDs.SetFloat("_intensity", 20);
                        LEDs.SetTexture("_Texture2D", Intake);
                    }
                }
                else if (ReefscapeRobotBase.CurrentRobotMode == ReefscapeRobotMode.Algae)
                {
                    LEDs.SetFloat("_X", 0f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", AlgaeMode);
                }
                else if (ReefscapeRobotBase.CurrentIntakeMode == ReefscapeIntakeMode.L1)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", L1Mode);
                }
                else if (ReefscapeRobotBase.CurrentRobotMode == ReefscapeRobotMode.Coral)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", CoralMode);
                }
                else
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Disabled);
                }
            } else if (ReefscapeCommandRobotBase != null)
            {
                if ((ReefscapeCommandRobotBase is null || GamePieceManager is null) ||
                    BaseGameManager.Instance.RobotState == RobotState.Disabled)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Disabled);
                }
                else if (ReefscapeCommandRobotBase.Climb())
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", prepClimb);
                }
                else if (ReefscapeCommandRobotBase.Climbed())
                {
                    LEDs.SetFloat("_X", 3f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Climbed);
                }
                else if (ReefscapeCommandRobotBase.AutoAlignLeftAction ||
                         ReefscapeCommandRobotBase.AutoAlignRightAction)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 0);
                    LEDs.SetTexture("_Texture2D", AutoAligning);
                }
                else if (hasAlgaePiece && ReefscapeCommandRobotBase.Processor())
                {
                    LEDs.SetFloat("_X", 1.5f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Processor);
                }
                else if (hasAlgaePiece)
                {
                    LEDs.SetFloat("_X", 0f);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", hasAlgae);
                }
                else if (hasCoralPiece)
                {
                    LEDs.SetFloat("_X", 1.5f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", hasCoral);
                }
                else if (ReefscapeCommandRobotBase.IntakeAction)
                {
                    if (ReefscapeCommandRobotBase.CurrentIntakeMode == ReefscapeIntakeMode.Normal)
                    {
                        LEDs.SetFloat("_X", 0);
                        LEDs.SetFloat("_Y", 0.0f);
                        LEDs.SetFloat("_intensity", 20);
                        LEDs.SetTexture("_Texture2D", Intake);
                    }
                }
                else if (ReefscapeCommandRobotBase.CurrentRobotMode == ReefscapeRobotMode.Algae)
                {
                    LEDs.SetFloat("_X", 0f);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", AlgaeMode);
                }
                else if (ReefscapeCommandRobotBase.CurrentIntakeMode == ReefscapeIntakeMode.L1)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", L1Mode);
                }
                else if (ReefscapeCommandRobotBase.CurrentRobotMode == ReefscapeRobotMode.Coral)
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.0f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", CoralMode);
                }
                else
                {
                    LEDs.SetFloat("_X", 0);
                    LEDs.SetFloat("_Y", 0.5f);
                    LEDs.SetFloat("_intensity", 20);
                    LEDs.SetTexture("_Texture2D", Disabled);
                }
            }
        }
    }
}