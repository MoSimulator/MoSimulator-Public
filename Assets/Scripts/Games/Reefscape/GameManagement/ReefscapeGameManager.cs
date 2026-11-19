using System.Collections;
using System.Collections.Generic;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.Scoring;
using GameSystems.Management;
using GameSystems.Settings;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.SceneTransitions;
using UnityEngine;

namespace Games.Reefscape.GameManagement
{
    public class ReefscapeGameManager : BaseGameManager
    {
        private ReefscapeScoreHandler _scoreHandler;
        
        private RobotSpawnController _robotSpawnController;

        [Header("Reefscape Specific Components")]
        [SerializeField] private GameObject reefscapeField;
        [SerializeField] private GameObject gamePieceWorld;
        
        private BargeLightController _bargeLightController;

        private CoralPool _coralPool;

        protected override bool IsReady => _coralPool == null || _coralPool.IsWarm;

        protected override void InitializeComponents()
        {
            base.InitializeComponents();
            
            _scoreHandler = FindAnyObjectByType<ReefscapeScoreHandler>();
            if (_scoreHandler == null)
            {
                Debug.LogError("ReefscapeScoreHandler component is missing.");
            }
            
            _robotSpawnController = FindAnyObjectByType<RobotSpawnController>();
            if (_robotSpawnController == null)
            {
                Debug.LogError("RobotSpawnController component is missing.");
            }

            _bargeLightController = FindAnyObjectByType<BargeLightController>();
            if (_bargeLightController == null)
            {
                Debug.LogError("BargeLightController component is missing.");
            }

            EnsureCoralPool();
        }

        /// <summary>
        /// Builds the coral pool the first time the scene loads. It outlives the field and the game
        /// piece world, so a restart finds it already here and skips straight past this. The prefab
        /// comes from a station rather than a field of its own, so there is nothing to wire up.
        /// </summary>
        private void EnsureCoralPool()
        {
            if (CoralPool.Instance)
            {
                _coralPool = CoralPool.Instance;
                return;
            }

            var station = FindAnyObjectByType<CoralStation>();
            if (station == null || station.CoralPrefab == null)
            {
                Debug.LogError("No CoralStation with a coral prefab to build the coral pool from.");
                return;
            }

            _coralPool = CoralPool.Create(station.CoralPrefab, ActiveAlliances());
        }

        /// <summary>
        /// Only an alliance that actually fields a robot needs coral, so a singleplayer match builds
        /// half the pool. Mirrors how <see cref="RobotSpawnController"/> decides who is playing.
        /// </summary>
        private static IEnumerable<Alliance> ActiveAlliances()
        {
            // Both, rather than a guess. Getting this wrong leaves one alliance's stations asking for
            // a pool that was never built, and they just never feed.
            if (MatchManager.Instance == null)
            {
                Debug.LogWarning("No MatchManager when building the coral pool; building both alliances.");
                return new[] { Alliance.Blue, Alliance.Red };
            }

            var p1 = MatchManager.Instance.GetOrCreatePlayerSettings(0);

            if (MatchManager.Instance.MatchSettings == null)
            {
                Debug.LogWarning("No match settings when building the coral pool; building both alliances.");
                return new[] { Alliance.Blue, Alliance.Red };
            }

            if (MatchManager.Instance.MatchSettings.gameMode != GameMode.LocalMultiplayer)
            {
                return new[] { p1.alliance };
            }

            var p2 = MatchManager.Instance.GetOrCreatePlayerSettings(1);

            return p1.alliance == p2.alliance
                ? new[] { p1.alliance }
                : new[] { p1.alliance, p2.alliance };
        }

        /// <summary>
        /// Coral is reclaimed rather than destroyed, so a restart never rebuilds it. Reclaiming runs
        /// before the base pass for two reasons: the tag sweep then finds none of ours, and a carried
        /// coral gets its collider child back before the robot holding it is destroyed.
        /// </summary>
        protected override void DestroyObjects()
        {
            if (_coralPool)
            {
                _coralPool.ReclaimAll();
            }

            // Timing kept commented out for the next restart stall: the base pass is six
            // FindGameObjectsWithTag sweeps, and the pool adds a lot for them to walk past.
            // var watch = System.Diagnostics.Stopwatch.StartNew();
            base.DestroyObjects();
            // Debug.Log($"[ResetTiming] tagSweeps={watch.Elapsed.TotalMilliseconds:F1} ms");
        }

        protected override void OnGameStateChanged(GameState gameState)
        {
            if (gameState == GameState.Endgame)
            {
                if (_bargeLightController == null)
                {
                    Debug.LogError("BargeLightController component is missing.");
                    return;
                }
                _bargeLightController.StartCoroutine(_bargeLightController.StartEndgameSequence());
            }
        }

        protected override IEnumerator PerformGameSpecificReset()
        {
            // Timing kept commented out for the next time a restart stall needs localising. Every
            // figure was synchronous work on one frame except robotSpawn, which is wall clock and
            // includes the waits inside it.
            // var watch = new System.Diagnostics.Stopwatch();

            SceneManager.Instance.PlayTransition("CrossFade");

            // watch.Restart();
            _scoreHandler.Reset();
            // var resetMs = watch.Elapsed.TotalMilliseconds;

            // watch.Restart();
            var newGamePieceWorld = Instantiate(gamePieceWorld);
            // var worldMs = watch.Elapsed.TotalMilliseconds;

            // watch.Restart();
            var newField = Instantiate(reefscapeField);
            // var fieldMs = watch.Elapsed.TotalMilliseconds;

            yield return null;

            _bargeLightController = newField.GetComponentInChildren<BargeLightController>();

            yield return new WaitForSeconds(0.2f);

            // watch.Restart();
            _scoreHandler.ReRegisterScorers();
            // var scorersMs = watch.Elapsed.TotalMilliseconds;

            // watch.Restart();
            yield return _robotSpawnController.StartMatch();
            // var spawnMs = watch.Elapsed.TotalMilliseconds;

            // Wait a frame for robots to fully initialize
            yield return null;

            // Set up scorers on the new robots with the new start line colliders
            // watch.Restart();
            _scoreHandler.EnsureScorersOnRobots();
            _scoreHandler.ReRegisterScorers();
            // var robotScorersMs = watch.Elapsed.TotalMilliseconds;

            // Debug.Log($"[ResetTiming] scoreReset={resetMs:F1} gamePieceWorld={worldMs:F1} " +
            //           $"field={fieldMs:F1} scorers={scorersMs:F1} robotSpawn={spawnMs:F1} " +
            //           $"robotScorers={robotScorersMs:F1} ms");
        }
    }
}