using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using MoSimLib;
using UnityEngine.InputSystem;

namespace RobotFramework
{
    /// <summary>
    /// How the driver controlled the robot during a match.
    /// The numeric values are stored alongside leaderboard scores, so do not renumber them.
    /// </summary>
    public enum MatchInputMethod
    {
        Keyboard = 0,
        Controller = 1
    }

    /// <summary>
    /// Records which input method drove a match, for storing next to a leaderboard score.
    ///
    /// A match can legitimately mix keyboard and controller, so this picks one representative answer:
    /// the first input after driver control begins. Autonomous is ignored because the driver is not
    /// steering yet, and endgame counts because it is still driver control.
    ///
    /// Nothing polls. Listeners are attached to the robots' own action maps, and they are torn down
    /// the moment an answer is captured, so once the driver moves for the first time this costs
    /// nothing for the rest of the match.
    /// </summary>
    public static class MatchInputTracker
    {
        private static readonly List<InputActionMap> ObservedMaps = new();
        private static BaseTimerManager _timerManager;
        private static bool _listening;

        /// <summary>
        /// Whether an input method was actually observed this match. False means the driver never
        /// touched anything after autonomous, in which case <see cref="InputMethod"/> is a fallback
        /// rather than a measurement.
        /// </summary>
        public static bool HasInputMethod { get; private set; }

        /// <summary>
        /// The input method used this match, falling back to <see cref="MatchInputMethod.Keyboard"/>
        /// when nothing was observed. Pair with <see cref="HasInputMethod"/> to tell a real keyboard
        /// match apart from that fallback.
        /// </summary>
        public static MatchInputMethod InputMethod { get; private set; } = MatchInputMethod.Keyboard;

        /// <summary>
        /// The input method used this match. Call at the end of the match, once the driver has
        /// finished, for the final answer.
        /// </summary>
        public static MoSimResult<MatchInputMethod> GetMatchInputMethod()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<MatchInputMethod>.Success(GetMatchInputMethodV1());
        }

        private static MatchInputMethod GetMatchInputMethodV1() => InputMethod;

        /// <summary>
        /// The input method used this match as the stored value: 0 for keyboard, 1 for controller.
        /// </summary>
        public static MoSimResult<int> GetMatchInputMethodValue()
        {
            var version = MoSim.ActiveVersion;
            return MoSimResult<int>.Success((int)GetMatchInputMethodV1());
        }

        /// <summary>
        /// Starts watching an action map for driver input. Robots call this as they wire up input;
        /// there is no need to call it from gameplay code.
        /// </summary>
        public static MoSimResult<MoSimLib.Void> Observe(InputActionMap map)
        {
            var version = MoSim.ActiveVersion;
            ObserveV1(map);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private static void ObserveV1(InputActionMap map)
        {
            if (map == null)
            {
                return;
            }

            EnsureMatchHook();

            if (!ObservedMaps.Contains(map))
            {
                ObservedMaps.Add(map);
            }

            // Nothing to listen for if this match already has its answer.
            if (HasInputMethod)
            {
                return;
            }

            map.actionTriggered -= OnActionTriggered;
            map.actionTriggered += OnActionTriggered;
            _listening = true;
        }

        /// <summary>
        /// Stops watching a map. Robots call this when they are destroyed so a torn-down robot's map
        /// is not held onto.
        /// </summary>
        public static MoSimResult<MoSimLib.Void> StopObserving(InputActionMap map)
        {
            var version = MoSim.ActiveVersion;
            StopObservingV1(map);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private static void StopObservingV1(InputActionMap map)
        {
            if (map == null)
            {
                return;
            }

            map.actionTriggered -= OnActionTriggered;
            ObservedMaps.Remove(map);
        }

        /// <summary>
        /// Clears the captured answer and starts listening again. Runs automatically at the start of
        /// every match, including restarts.
        /// </summary>
        public static void Rearm()
        {
            var version = MoSim.ActiveVersion;
            RearmV1();
        }

        private static void RearmV1()
        {
            HasInputMethod = false;
            InputMethod = MatchInputMethod.Keyboard;
            StartListening();
        }

        private static void OnActionTriggered(InputAction.CallbackContext context)
        {
            if (HasInputMethod)
            {
                return;
            }

            // Only count the driver acting on a control, not releasing one or an action being cancelled.
            if (context.phase != InputActionPhase.Started && context.phase != InputActionPhase.Performed)
            {
                return;
            }

            if (!IsDriverControlActive())
            {
                return;
            }

            var device = context.control?.device;
            if (device == null)
            {
                return;
            }

            InputMethod = device is Gamepad ? MatchInputMethod.Controller : MatchInputMethod.Keyboard;
            HasInputMethod = true;

            // Locked in for this match - drop the listeners so no further input runs through here.
            StopListening();
        }

        /// <summary>
        /// True once the driver is in control. Autonomous does not count, and neither does the hold
        /// between autonomous and teleop, which is still reported as autonomous.
        /// </summary>
        private static bool IsDriverControlActive()
        {
            if (_timerManager == null)
            {
                return false;
            }

            var state = _timerManager.CurrentGameState;
            return state is GameState.Teleop or GameState.Endgame;
        }

        /// <summary>
        /// Finds the match timer and re-arms on every match start. Retried on each Observe call, since
        /// the first robot may register before the timer exists.
        /// </summary>
        private static void EnsureMatchHook()
        {
            if (_timerManager != null)
            {
                return;
            }

            _timerManager = UnityEngine.Object.FindFirstObjectByType<BaseTimerManager>();
            if (_timerManager == null)
            {
                return;
            }

            _timerManager.OnMatchStart -= Rearm;
            _timerManager.OnMatchStart += Rearm;
        }

        private static void StartListening()
        {
            if (_listening)
            {
                return;
            }

            foreach (var map in ObservedMaps)
            {
                if (map == null) continue;

                map.actionTriggered -= OnActionTriggered;
                map.actionTriggered += OnActionTriggered;
            }

            _listening = true;
        }

        private static void StopListening()
        {
            foreach (var map in ObservedMaps)
            {
                if (map == null) continue;

                map.actionTriggered -= OnActionTriggered;
            }

            _listening = false;
        }
    }
}
