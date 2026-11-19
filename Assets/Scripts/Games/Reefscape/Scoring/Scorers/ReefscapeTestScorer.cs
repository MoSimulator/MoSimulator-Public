using MoSimCore.Enums;
using MoSimCore.Interfaces;
using UnityEngine;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ReefscapeTestScorer : MonoBehaviour, IScorer
    {
        [SerializeField] private MoSimLib.SecureFloat scoreValue = 10;
        
        public MoSimLib.SecureFloat ScoreValue => scoreValue;
        
        [field: SerializeField] public Alliance Alliance { get; } = Alliance.Blue;
        
        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is ReefscapeScoreData reefscapeData)
            {
                reefscapeData.CoralPoints += ScoreValue;
                reefscapeData.CoralScored += 1;
            }
        }
    }
}
