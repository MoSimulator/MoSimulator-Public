﻿using System.Collections;
using MoSimCore.BaseClasses.GameManagement.AudioManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace MoSimCore.BaseClasses.GameManagement
{
    /// <summary>
    /// Abstract base class for managing game state, timing, and audio in game modes.
    /// Implements the singleton pattern and coordinates timer and audio managers.
    /// Inherit from this class to create game-specific manager implementations.
    /// </summary>
    public abstract class BaseGameManager : MonoBehaviour
    {
        /// <summary>Gets the singleton instance of the game manager.</summary>
        public static BaseGameManager Instance { get; private set; }

        /// <summary>The timer manager responsible for match timing and state transitions.</summary>
        protected BaseTimerManager TimerManager;
        
        /// <summary>The audio manager responsible for match sound effects.</summary>
        protected BaseAudioManager AudioManager;

        [SerializeField]
        [Tooltip("Array of GameObject tags to destroy when resetting the match.")]
        private string[] tagsToDestroy;

        /// <summary>Gets the current state of the game (Auto, Teleop, Endgame, End).</summary>
        public GameState GameState => TimerManager.CurrentGameState;
        
        /// <summary>Gets the current state of robots (Enabled or Disabled).</summary>
        public RobotState RobotState => TimerManager.CurrentRobotState;
        
        /// <summary>Gets the current match timer value in seconds.</summary>
        public float Timer => TimerManager.Timer;
        
        /// <summary>Gets whether the game is currently performing a reset operation.</summary>
        public bool IsResetting { get; protected set; }

        /// <summary>Gets whether the game was cheated in this match</summary>
        public bool wasCheated = false;

        /// <summary>
        /// Whether the game has finished whatever loading it does before a match can start. Override
        /// in a game that builds something up front, so the clock does not run while it is loading.
        /// </summary>
        protected virtual bool IsReady => true;

        /// <summary>
        /// Initializes the singleton instance and sets up manager components.
        /// Override to add game-specific initialization.
        /// </summary>
        protected virtual void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializeComponents();
            
            if (TimerManager != null)
            {
                if (IsReady)
                {
                    TimerManager.StartMatch();
                }
                else
                {
                    StartCoroutine(StartMatchWhenReady());
                }
            }
        }

        /// <summary>
        /// Holds the match until <see cref="IsReady"/> comes up. The timer has to be reset and paused
        /// first: nothing has set the clock yet, and its own Update would otherwise run it straight
        /// from zero into <see cref="GameState.End"/> with the robots enabled before the wait is over.
        /// </summary>
        private IEnumerator StartMatchWhenReady()
        {
            TimerManager.ResetTimer();
            TimerManager.PauseTimer();

            yield return new WaitUntil(() => IsReady);

            TimerManager.StartMatch();
        }
        
        /// <summary>
        /// Retrieves and sets up the timer and audio manager components.
        /// Subscribes to timer events to trigger audio cues at appropriate times.
        /// </summary>
        protected virtual void InitializeComponents()
        {
            TimerManager = GetComponent<BaseTimerManager>();
            if (TimerManager == null)
            {
                Debug.LogError("BaseTimerManager component is missing.");
            }

            AudioManager = GetComponent<BaseAudioManager>();
            if (AudioManager == null)
            {
                Debug.LogError("BaseAudioManager component is missing.");
            }

            if (TimerManager == null || AudioManager == null) return;
            TimerManager.OnMatchStart += AudioManager.PlayMatchStart;
            TimerManager.OnAutoEnd += AudioManager.PlayMatchEnd;
            TimerManager.OnTeleopStart += AudioManager.PlayTeleopStart;
            TimerManager.OnEndgameStart += AudioManager.PlayEndgameStart;
            TimerManager.OnMatchEnd += AudioManager.PlayMatchEnd;

            TimerManager.OnGameStateChanged += OnGameStateChanged;
        }
        
        /// <summary>
        /// Called when the game state changes (e.g., Auto to Teleop).
        /// Implement this to handle game-specific state transition logic.
        /// See ReefscapeGameManager for an example implementation.
        /// </summary>
        /// <param name="gameState">The new game state.</param>
        protected abstract void OnGameStateChanged(GameState gameState);

        /// <summary>
        /// Resets the match to its initial state.
        /// Stops audio, resets timers, destroys tagged objects, and performs game-specific cleanup.
        /// Override to customize the reset sequence.
        /// </summary>
        /// <returns>An enumerator for coroutine execution.</returns>
        public virtual IEnumerator ResetMatch()
        {
            IsResetting = true;
            AudioManager.StopAll();
            TimerManager.StopAllTimerCoroutines();
            TimerManager.ResetTimer();
            wasCheated = false;
            DestroyObjects();
            yield return PerformGameSpecificReset();

            if (!IsReady)
            {
                yield return new WaitUntil(() => IsReady);
            }

            TimerManager.StartMatch();
            IsResetting = false;
        }
        
        /// <summary>
        /// Performs game-specific reset operations.
        /// Implement this to reset scoring, game pieces, or other game-specific state.
        /// </summary>
        /// <returns>An enumerator for coroutine execution.</returns>
        protected abstract IEnumerator PerformGameSpecificReset();

        /// <summary>
        /// Destroys all GameObjects with tags specified in the tagsToDestroy array.
        /// Used to clean up game pieces and other dynamic objects when resetting.
        /// </summary>
        protected virtual void DestroyObjects()
        {
            foreach (var tagToDestroy in tagsToDestroy)
            {
                var objectsToDestroy = GameObject.FindGameObjectsWithTag(tagToDestroy);
                if (objectsToDestroy == null) continue;
                foreach (var obj in objectsToDestroy)
                {
                    Destroy(obj);
                }
            }
        }
    }
}