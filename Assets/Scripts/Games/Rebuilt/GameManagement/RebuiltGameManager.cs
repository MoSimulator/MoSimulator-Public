using System;
using System.Collections;
using Games.Rebuilt.FieldScripts;
using Games.Rebuilt.Scoring;
using GameSystems.Management;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.SceneTransitions;
using UnityEngine;

namespace Games.Rebuilt.GameManagement
{
    public class RebuiltGameManager : BaseGameManager
    {
        private RebuiltScoreHandler _scoreHandler;

        private RobotSpawnController _robotSpawnController;
        
        private RebuiltSpawnInitializer _spawnInitializer;

        [Header("Rebuilt Specific Components")] [SerializeField]
        private GameObject rebuiltField;

        [SerializeField] private GameObject gamePieceWorld;

        protected override void InitializeComponents()
        {
            base.InitializeComponents();

            _scoreHandler = FindAnyObjectByType<RebuiltScoreHandler>();
            if (_scoreHandler == null)
            {
                Debug.LogError("RebuiltScoreHandler component is missing.");
            }

            _robotSpawnController = FindAnyObjectByType<RobotSpawnController>();
            if (_robotSpawnController == null)
            {
                Debug.LogError("RobotSpawnController component is missing.");
            }
            
            _spawnInitializer = FindAnyObjectByType<RebuiltSpawnInitializer>();

            if (_spawnInitializer == null)
            {
                Debug.LogError($"RebuiltSpawnInitializer component is missing.");
            }
        }

        protected override void OnGameStateChanged(GameState gameState)
        {
        }

        protected override IEnumerator PerformGameSpecificReset()
        {
            SceneManager.Instance.PlayTransition("CrossFade");

            _scoreHandler.Reset();

            var newGamePieceWorld = Instantiate(gamePieceWorld);
            var newRebuiltField = Instantiate(rebuiltField);
            
            var newPreloadManager = newGamePieceWorld.GetComponent<RebuiltFuelPreloadManager>();

            if (newPreloadManager == null)
            {
                throw new InvalidOperationException($"The new gamePieceWorld does not contain a RebuiltFuelPreloadManager component.");
            }

            _spawnInitializer.SetPreloadManager(newPreloadManager);

            yield return null;

            _scoreHandler.ReRegisterScorers();

            yield return _robotSpawnController.StartMatch();

            yield return null;

            _scoreHandler.ReRegisterScorers();
        }
    }
}