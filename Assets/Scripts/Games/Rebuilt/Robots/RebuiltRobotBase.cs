using System;
using Games.Rebuilt.FieldScripts;
using MoSimCore.Enums;
using MoSimLib;
using RobotFramework;
using UnityEngine;

namespace Games.Rebuilt.Robots
{
    public class RebuiltRobotBase : ScheduledRobotBase
    {
        
        public MoSimInput FaceDown { get; private set; }
        public MoSimInput ClimbAction { get; private set; }

        public MoSimInput ManualShoot { get; private set; }
        public MoSimInput AutoPass { get; private set; }

        public MoSimInput DpadUp { get; private set; }
        public MoSimInput DpadL { get; private set; }
        public MoSimInput DpadR { get; private set; }
        public MoSimInput RobotSpecial { get; private set; }
        public MoSimInput RobotSpecial2 { get; private set; }
        public MoSimInput RobotSpecial3 { get; private set; }

        public new MoSimInput OuttakeAction { get; private set; }

        public new MoSimInput IntakeAction { get; private set; }

        public new MoSimInput StowAction { get; private set; }

        [HideInInspector] public Collider BumperCollider;

        private MoSimActionScheduler actions;

        private float maxWidth;

        [Header("Outpost Settings")]
        [SerializeField] public bool OpenChuteOnStart = false;
        [SerializeField] public float OpenDistance = 1;

        [Header("Preloads")]
        [SerializeField] public GameObject[] preloads;

        private MoSimInput[] cachedInputs;


        protected override void Start()
        {
            base.Start();
            Games.Rebuilt.GamePieceSystem.RebuiltGamePieceManager.GetInstance()?.RegisterRobot(this);
            actions = new MoSimActionScheduler();
            BumperCollider = GetComponent<BoxCollider>();
            var bounds = BumperCollider.bounds;
            var x = bounds.extents.x;
            var y = bounds.extents.y;
            maxWidth = Mathf.Sqrt((x * x) + (y * y));

            var outposts = FindObjectsByType(typeof(Outpost), FindObjectsSortMode.None);
            if (outposts.Length == 0)
            {
                Debug.LogError("No Outpost found in the scene!");
            }

            foreach (var coralStation in outposts)
            {
                var station = (Outpost)coralStation;
                if (Alliance == Alliance.Blue && station.Alliance == Alliance.Blue ||
                    Alliance == Alliance.Red && station.Alliance == Alliance.Red)
                {
                    station.Robots.Add(this);
                }
            }

            cachedInputs = new MoSimInput[] {
                FaceDown,
                ClimbAction, ManualShoot, AutoPass,
                DpadUp, DpadL, DpadR, RobotSpecial, RobotSpecial2, RobotSpecial3, StowAction,
                OuttakeAction, IntakeAction
            };
        }

        protected override void DelayedStart()
        {
            base.DelayedStart();
            SetupInputActions();
        }

        protected override void Awake()
        {
            base.Awake();

            SetupInputActions();
        }

        private void OnValidate()
        {
            if (preloads != null && preloads.Length > 8)
            {
                Debug.LogWarning("Preload limit reached. The maximum number of preloads is 8.");
                Array.Resize(ref preloads, 8);
            }
        }

        protected bool RobotSpecialTriggered()
        {
            return RobotSpecial.WasTriggered();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            actions.ResetActionsPool(cachedInputs);
        }

        protected override void Update()
        {
            base.Update();
            actions.AddActionsToPool(cachedInputs);
        }

        private void SetupInputActions()
        {
            FaceDown = new MoSimInput(InputActionMap.FindAction("FaceDown"));
            ClimbAction = new MoSimInput(InputActionMap.FindAction("Climb"));

            ManualShoot = new MoSimInput(InputActionMap.FindAction("ManualShoot"));
            AutoPass = new MoSimInput(InputActionMap.FindAction("AutoPass"));

            DpadUp = new MoSimInput(InputActionMap.FindAction("DpadUp"));
            DpadL = new MoSimInput(InputActionMap.FindAction("DpadL"));
            DpadR = new MoSimInput(InputActionMap.FindAction("DpadR"));
            RobotSpecial = new MoSimInput(InputActionMap.FindAction("RobotSpecial1"));
            RobotSpecial2 = new MoSimInput(InputActionMap.FindAction("RobotSpecial2"));
            RobotSpecial3 = new MoSimInput(InputActionMap.FindAction("RobotSpecial3"));

            StowAction = new MoSimInput(InputActionMap.FindAction("Stow"));
            OuttakeAction = new MoSimInput(InputActionMap.FindAction("AutoShoot"));
            IntakeAction = new MoSimInput(InputActionMap.FindAction("Intake"));
        }

        /// <summary>
        /// return if the robot in the neutral zone
        /// </summary>
        /// <returns></returns>
        public bool InNeutralZone()
        {
            return transform.localPosition.x + maxWidth < 3.632f && transform.localPosition.x + maxWidth > -3.632f;
        }

        /// <summary>
        /// return in the robot is under the trench (or if a point is under trench)
        /// </summary>
        /// <returns></returns>
        public bool UnderTrench(Transform point = null, float distance = 0f)
        {
            if (distance < 0f) distance = 0f;

            var underTrench1X = false;
            var underTrench2X = false;

            var underTrenchZ = false;

            if (point == null)
            {
                underTrench1X = transform.localPosition.x + maxWidth < (3.7f + distance) && transform.localPosition.x + maxWidth > (3.59f - distance);
                underTrench2X = transform.localPosition.x + maxWidth > (-3.7f - distance) && transform.localPosition.x + maxWidth < (-3.59f + distance);

                underTrenchZ = transform.localPosition.z + maxWidth < -2.7f || transform.localPosition.z + maxWidth > 2.7f;
            }
            else
            {
                var localPoint = transform.parent.InverseTransformPoint(point.position);

                underTrench1X = localPoint.x < (3.7f + distance) && localPoint.x > (3.59f - distance);
                underTrench2X = localPoint.x > (-3.7f - distance) && localPoint.x < (-3.59f + distance);

                underTrenchZ = localPoint.z < -2.7f || localPoint.z > 2.7f;
            }

            return (underTrench1X || underTrench2X) && underTrenchZ;
        }

        /// <summary>
        /// Returns if the alliance shift is currently running
        /// </summary>
        /// <returns></returns>
        public bool ShiftActive()
        {
            return (RebuiltShifts.ActiveHub == ActiveHub.Red && Alliance == Alliance.Red) ||
                    (RebuiltShifts.ActiveHub == ActiveHub.Blue && Alliance == Alliance.Blue) ||
                    RebuiltShifts.ActiveHub == ActiveHub.Both;
        }

        /// <summary>
        /// Get if the robot is in its alliance zone
        /// </summary>
        /// <returns></returns>
        public bool InAllianceZone()
        {
            if (Alliance == Alliance.Blue)
            {
                return transform.localPosition.x + maxWidth > 4.25f;
            }
            else
            {
                return transform.localPosition.x - maxWidth < -4.25f;
            }
        }
    }


}
