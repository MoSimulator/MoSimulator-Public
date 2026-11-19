using System.Collections.Generic;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Scoring;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;

namespace FieldScripts
{
    public class Trough : MonoBehaviour, IScorer
    {
        public Alliance Alliance { get; private set; }
        private Reef _reef;

        [SerializeField] private BoxCollider[] troughTriggers;

        private Vector3[] troughBounds;
        private Vector3[] troughCenters;
        private Quaternion[] troughNormals;

        [SerializeField] private List<GameObject> troughObjects = new List<GameObject>();
        [SerializeField] private List<GameObject> scoredInAuto = new List<GameObject>();
        [SerializeField] private List<GameObject> stillScored = new List<GameObject>();

        private void Awake()
        {
            _reef = GetComponentInParent<Reef>();
            if (_reef == null)
            {
                Debug.LogError("Trough could not find a Reef component in parent objects.");
                enabled = false;
                return;
            }

            Alliance = _reef.Alliance;
        }

        private void Start()
        {
            troughBounds = new Vector3[troughTriggers.Length];
            troughCenters = new Vector3[troughTriggers.Length];
            troughNormals = new Quaternion[troughTriggers.Length];

            for (int i = 0; i < troughTriggers.Length; i++)
            {
                troughBounds[i] = Utils.MultiplyVectors(troughTriggers[i].size, troughTriggers[i].transform.lossyScale) / 2;
                troughCenters[i] = troughTriggers[i].bounds.center;
                troughNormals[i] = troughTriggers[i].transform.rotation;
            }
        }

        private void Update()
        {
            // 1. Safety check for the GameManager instance
            if (BaseGameManager.Instance == null) return;

            // 2. Lock scoring logic if match has ended
            // This prevents the lists from being cleared, keeping points active on the UI
            if (BaseGameManager.Instance.GameState == GameState.End) return;

            // 3. Reset lists for the new frame scan
            troughObjects.Clear();
            scoredInAuto.Clear();
            scoredInAuto.AddRange(stillScored);
            stillScored.Clear();

            for (int i = 0; i < troughTriggers.Length; i++)
            {
                CheckCoral(i);
            }
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is not ReefscapeScoreData reefscapeData) return;

            // Calculate Auto bonuses
            if (BaseGameManager.Instance != null && BaseGameManager.Instance.GameState == GameState.Auto)
            {
                foreach (var col in troughObjects)
                {
                    if (scoredInAuto.Contains(col)) continue;
                    scoredInAuto.Add(col);
                }
            }

            // Apply points based on the frozen troughObjects list
            foreach (GameObject go in troughObjects)
            {
                if (scoredInAuto.Contains(go))
                {
                    stillScored.Add(go);
                    reefscapeData.CoralPoints += 3;
                    reefscapeData.CoralScored += 1;
                }
                else
                {
                    reefscapeData.CoralPoints += 2;
                    reefscapeData.CoralScored += 1;
                }
            }
        }

        private void CheckCoral(int index)
        {
            foreach (var result in Physics.OverlapBox(troughCenters[index],
                         troughBounds[index],
                         troughNormals[index]))
            {
                if (!result.CompareTag("Coral")) continue;

                if (troughObjects.Contains(result.gameObject)) continue;

                troughObjects.Add(result.gameObject);

                var piece = result.GetComponentInParent<ReefscapeGamePieceController>();
                if (piece != null) piece.IsScored = true;
            }
        }

        // Call this via a Match Reset event if points persist between match restarts
        public void ResetTrough()
        {
            troughObjects.Clear();
            scoredInAuto.Clear();
            stillScored.Clear();
        }
    }
}