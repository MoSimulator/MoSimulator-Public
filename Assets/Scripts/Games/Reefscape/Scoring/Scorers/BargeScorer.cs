using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class BargeScorer : MonoBehaviour, IScorer
    {
        [field: SerializeField] public Alliance Alliance { get; private set; }

        private BoxCollider _scoringCollider;
        private Vector3 boxSize;
        private Vector3 boxPosition;
        private Quaternion boxRotation;
        private HashSet<Collider> _scoredAlgae = new HashSet<Collider>();
        private int _algaeCount = 0;
        private OverlapBoxBounds g406Enforcer;
        private LayerMask coralMask;
        private float g406Timer;
        private int g406Counter;

        private void Start()
        {
            _scoringCollider = GetComponent<BoxCollider>();
            if (_scoringCollider == null)
            {
                Debug.LogError("BargeScorer could not find a BoxCollider component.");
                enabled = false;
                return;
            }
            
            boxSize = Utils.MultiplyVectors(_scoringCollider.size, _scoringCollider.transform.lossyScale) / 2;
            boxPosition = _scoringCollider.bounds.center;
            boxRotation = _scoringCollider.transform.rotation;
            
            g406Enforcer = new OverlapBoxBounds(_scoringCollider, false);
            coralMask = LayerMask.GetMask("Coral");
            g406Counter = 0;
            g406Timer = 0;
        }

        private void Update()
        {
            if (BaseGameManager.Instance == null)
            {
                return;
            }
            
            if (BaseGameManager.Instance.GameState == GameState.End)
            {
                return;
            }
            
            if (g406Enforcer.OverlapBox(coralMask).Length > 0)
            {
                if (g406Counter == 0)
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 10)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                } else if (g406Timer >= 3)
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 5)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                }
                else
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 1.5f)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                }
            }

            var results = Physics.OverlapBox(boxPosition, boxSize, boxRotation);

            // Build current set of algae colliders in the zone
            var currentAlgae = new HashSet<Collider>();
            foreach (var result in results)
            {
                if (result == null) continue;
                if (!result.CompareTag("Algae")) continue;
                currentAlgae.Add(result);
                if (!_scoredAlgae.Contains(result))
                {
                    // New algae entered the zone, mark it as scored
                    _scoredAlgae.Add(result);
                    _algaeCount++;
                }
            }

            // Remove algae that have left the zone and adjust count
            var toRemove = new List<Collider>();
            foreach (var scored in _scoredAlgae)
            {
                if (scored == null)
                {
                    toRemove.Add(scored);
                    continue;
                }

                if (!currentAlgae.Contains(scored))
                {
                    toRemove.Add(scored);
                }
            }

            foreach (var rem in toRemove)
            {
                if (_scoredAlgae.Remove(rem))
                {
                    _algaeCount = Mathf.Max(0, _algaeCount - 1);
                }
            }
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            // Mark game as ended when End state is reached

            if (scoreData is ReefscapeScoreData reefscapeScoreData)
            {
                // Add the total points based on counted algae
                reefscapeScoreData.NetPoints += _algaeCount * 4;
                reefscapeScoreData.AlgaeScored += _algaeCount;
                // reefscapeScoreData.SelfPenaltyEarned += g406Counter * 6;
            }
            else
            {
                Debug.LogError("Invalid score data type passed to BargeScorer.");
            }
        }
    }
}