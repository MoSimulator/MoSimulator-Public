using System;
using System.Collections.Generic;
using System.ComponentModel;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimLib;

namespace RobotFramework
{
    public abstract class ScheduledRobotBase: RobotBase
    {
        protected Scheduler Scheduler;
        private MoSimActionScheduler _actions;
        private bool _lateInitDone;
        
        protected override void Start()
        {
            base.Start();
            Scheduler = new Scheduler();
            _actions = new MoSimActionScheduler();
            
            Init();
        }
        
        protected virtual void FixedUpdate()
        {
            HandleGyro();

            if (!_lateInitDone)
            {
                _lateInitDone = true;
                LateInit();
            }

            if (BaseGameManager.Instance.RobotState == RobotState.Enabled)
            {
                Scheduler.RunSubsystems();
            }

            Scheduler.RunProtectedSubsystems();
        }

        protected virtual void LateUpdate()
        {
            Scheduler.RunLateRecursive();
        }

        protected override void Update()
        {
            base.Update();
            Scheduler.RunRecursive();
        }

        /// <summary>
        /// Set the robot up. Runs from Start, so anything another component builds in ITS Start may
        /// not exist yet - Unity gives no ordering between two Starts. Use <see cref="LateInit"/> for
        /// whatever has to see the rest of the robot finished.
        /// </summary>
        protected virtual void Init()
        {}

        /// <summary>
        /// The rest of the setup, run once on the first physics step. Every Start in the scene has
        /// happened by then, so this is the first moment a robot can reach what they built: a
        /// GenericJoint's Rigidbody, an InteractionRoller's trigger and core colliders, and anything
        /// else that is only there once its own component has started.
        /// </summary>
        /// <remarks>
        /// Ignoring collisions belongs here rather than in <see cref="Init"/>. Physics.IgnoreCollision
        /// registers a pair of colliders that exist at the moment it is called and does nothing for
        /// one created afterwards, so a call made from Init silently misses every collider a component
        /// builds in its own Start - which on a robot with rollers is most of what it touches with.
        /// </remarks>
        protected virtual void LateInit()
        {}
        
        
    }
    
    public class MoSimActionScheduler {
        public void AddActionsToPool(MoSimInput[] actions)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                if (action.IsPressed() && !action.GetWasPressed())
                {
                    action.ChangeTriggered(true);
                }
            }
        }
        
        public void ResetActionsPool(MoSimInput[] actions)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                action.ChangeWasPressed(action.IsPressed());
                action.ChangeTriggered(false);
            }
        }
    }

    public class Scheduler
    {
        private List<Action> _actions;
        private List<Action> _protectedActions;
        private List<Action> _lateUpdateActions;
        private List<Action> _updateActions;
        
        /// <summary>
        /// Add a primary system function to the scheduler
        /// </summary>
        /// <param name="subsystems"></param>
        public MoSimResult<MoSimLib.Void> AddSubsystems(params Action[] subsystems)
        {
            var version = MoSim.ActiveVersion;
            AddSubsystemsV1(subsystems);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void AddSubsystemsV1(params Action[] subsystems)
        {
            _actions ??= new List<Action>();

            foreach (var action in subsystems)
            {
                _actions.Add(action);
            }
        }

        public MoSimResult<MoSimLib.Void> AddProtectedSystem(params Action[] function)
        {
            var version = MoSim.ActiveVersion;
            AddProtectedSystemV1(function);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void AddProtectedSystemV1(params Action[] function)
        {
            _protectedActions ??= new List<Action>();

            foreach (var action in function)
            {
                _protectedActions.Add(action);
            }
        }
        
        /// <summary>
        /// things that need to happen at a high frequency AFTER a frame update, should not be used for subsystems
        /// </summary>
        /// <param name="function"></param>
        public MoSimResult<MoSimLib.Void> AddLateRecursive(params Action[] function)
        {
            var version = MoSim.ActiveVersion;
            AddLateRecursiveV1(function);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void AddLateRecursiveV1(params Action[] function)
        {
            _lateUpdateActions ??= new List<Action>();
            
            _lateUpdateActions.AddRange(function);
        }

        /// <summary>
        /// things that need to happen at a high frequency, Avoid when possible, likley to cause timing issues
        /// </summary>
        /// <param name="function"></param>
        public MoSimResult<MoSimLib.Void> AddRecursive(params Action[] function)
        {
            var version = MoSim.ActiveVersion;
            AddRecursiveV1(function);
            return MoSimResult<MoSimLib.Void>.Success(default);
        }

        private void AddRecursiveV1(params Action[] function)
        {
            _updateActions ??= new List<Action>();
            _updateActions.AddRange(function);
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void RunSubsystems()
        {
            if (_actions == null) return;
            foreach (var action in _actions)
            {
                action();
            }
        }
        
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void RunProtectedSubsystems()
        {
            if (_protectedActions == null) return;
            foreach (var action in _protectedActions)
            {
                action();
            }
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void RunLateRecursive()
        {
            if (_lateUpdateActions == null) return;
            foreach (var action in _lateUpdateActions)
            {
                action();
            }
        }
        
        [EditorBrowsable(EditorBrowsableState.Never)]
        internal void RunRecursive()
        {
            if (_updateActions == null) return;
            foreach (var action in _updateActions)
            {
                action();
            }
        }
    }
}
