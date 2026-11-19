using System;
using UnityEngine;

namespace MoSimLib
{
    /// <summary>
    /// A polled delay using Unity's scaled game time. Safe to construct in a field initializer;
    /// call its methods on the Unity main thread.
    /// </summary>
    public sealed class Alarm
    {
        private enum State { Idle, Waiting, WaitingOnce, CompletedOnce }

        private State _state;
        private double _deadline;

        /// <summary>
        /// Starts a delay if idle and returns false until it finishes. Returns true once,
        /// consuming completion; the following call can start a new delay.
        /// </summary>
        public bool wait(float seconds) => Poll(seconds, false);

        /// <summary>
        /// Starts a delay if idle and returns false until it finishes. Once complete,
        /// keeps returning true until reset() is called.
        /// </summary>
        public bool waitOnce(float seconds) => Poll(seconds, true);

        /// <summary>Cancels the current delay and clears any latched completion.</summary>
        public void reset()
        {
            _state = State.Idle;
            _deadline = 0;
        }

        // Both polling methods share one run. Its duration and repeat/once mode are
        // captured on the first call and cannot change while it is running or latched.
        private bool Poll(float seconds, bool once)
        {
            if (_state == State.CompletedOnce)
                return true;

            if (_state == State.Idle &&
                (seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)))
            {
                throw new ArgumentOutOfRangeException(nameof(seconds),
                    "The delay must be a finite, non-negative number of seconds.");
            }

            double now = Time.timeAsDouble;
            if (_state == State.Idle)
            {
                _deadline = now + seconds;
                _state = once ? State.WaitingOnce : State.Waiting;
            }

            if (now < _deadline)
                return false;

            _state = _state == State.WaitingOnce ? State.CompletedOnce : State.Idle;
            return true;
        }
    }
}
