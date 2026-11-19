#nullable enable
using System;
using System.Collections.Generic;
using MoSimLib;

namespace RobotFramework
{
    public class StateCommandRobotBase : ScheduledRobotBase
    {
        public readonly Dictionary<Type, StateBinding> Bindings = new();
        protected readonly Dictionary<Type, IRobotState> States = new();
        protected IRobotState? CurrentState;
        protected Type? LastState;
    
        protected override void Start()
        {
            base.Start();
            Scheduler.AddProtectedSystem(Tick);
        }
    
        protected virtual void Tick()
        {
            if (CurrentState == null) return;

            var next = CurrentState.CheckTransition();
            if (next != null)
            {
                TransitionTo(next);
                return;
            }

            CurrentState.OnMechanismUpdate();
            CurrentState.OnGamePieceUpdate();
        }

        private void TransitionTo(IRobotState next)
        {
            CurrentState?.OnExit();
            if (CurrentState != null) LastState = CurrentState.GetType();
            CurrentState = next;
            CurrentState.OnEnter();
        }
    
        protected StateBinding InState<T>() where T : IRobotState
        {
            var version = MoSim.ActiveVersion;
            return InStateV1<T>();
        }

        private StateBinding InStateV1<T>() where T : IRobotState
        {
            var key = typeof(T);
            if (Bindings.TryGetValue(key, out var binding)) return binding;
            binding = new StateBinding();
            Bindings[key] = binding;
            return binding;
        }
    
        protected bool WasLastState<T>() where T : IRobotState
        {
            var version = MoSim.ActiveVersion;
            return WasLastStateV1<T>();
        }

        private bool WasLastStateV1<T>() where T : IRobotState
        {
            return LastState == typeof(T);
        }
    }

    public abstract class RobotState<TRobot> : IRobotState
        where TRobot : StateCommandRobotBase
    {
        protected readonly TRobot Robot;

        protected RobotState(TRobot robot) => Robot = robot;
        
        protected IRobotState? TransitionBehaviour(Func<IRobotState?> defaultBehaviour, IRobotState state)
        {
            var version = MoSim.ActiveVersion;
            return TransitionBehaviourV1(defaultBehaviour, state);
        }

        private IRobotState? TransitionBehaviourV1(Func<IRobotState?> defaultBehaviour, IRobotState state)
        {
            IRobotState? output = null;
            var defaultAct = Robot.Bindings.GetValueOrDefault(state.GetType());
            output = defaultAct?.CheckTransitionAction != null 
                ? defaultAct.CheckTransitionAction.Invoke() 
                : defaultBehaviour.Invoke();
                
            return output ?? Robot.Bindings.GetValueOrDefault(state.GetType())?.AdditionalTransitionAction?.Invoke();
        }

        public virtual IRobotState? CheckTransition() => null;
        public virtual void OnEnter()          { }
        public virtual void OnExit()           { }
        public virtual void OnMechanismUpdate() { }
        public virtual void OnGamePieceUpdate()  { }
    }

    public sealed class StateBinding
    {
        public Action? EnterAction      { get; private set; }
        internal Action? ExitAction       { get; private set; }
        public Action? MechanismsAction { get; private set; }
        public Action? GamePieceAction  { get; private set; }
    
        public Func<IRobotState>? CheckTransitionAction { get; private set; }
    
        public Func<IRobotState>? AdditionalTransitionAction { get; private set; }

        public StateBinding OnEnter(Action a)
        {
            var version = MoSim.ActiveVersion;
            return OnEnterV1(a);
        }

        private StateBinding OnEnterV1(Action a) { EnterAction = a; return this; }

        public StateBinding OnExit(Action a)
        {
            var version = MoSim.ActiveVersion;
            return OnExitV1(a);
        }

        private StateBinding OnExitV1(Action a) { ExitAction = a; return this; }

        public StateBinding OnMechanisms(Action a)
        {
            var version = MoSim.ActiveVersion;
            return OnMechanismsV1(a);
        }

        private StateBinding OnMechanismsV1(Action a) { MechanismsAction = a; return this; }

        public StateBinding OnGamePiece(Action a)
        {
            var version = MoSim.ActiveVersion;
            return OnGamePieceV1(a);
        }

        private StateBinding OnGamePieceV1(Action a) { GamePieceAction = a; return this; }
    
        public StateBinding OnCheckTransition(Func<IRobotState> a)
        {
            var version = MoSim.ActiveVersion;
            return OnCheckTransitionV1(a);
        }

        private StateBinding OnCheckTransitionV1(Func<IRobotState> a)
        {
            CheckTransitionAction = a;
            return this;
        }

        public StateBinding OnAdditionalTransition(Func<IRobotState> a)
        {
            var version = MoSim.ActiveVersion;
            return OnAdditionalTransitionV1(a);
        }

        private StateBinding OnAdditionalTransitionV1(Func<IRobotState> a)
        {
            AdditionalTransitionAction = a;
            return this;
        }
    }


    public interface IRobotState
    {
        IRobotState? CheckTransition();
        void OnEnter();
        void OnExit();
        void OnMechanismUpdate();
        void OnGamePieceUpdate();
    }
}
