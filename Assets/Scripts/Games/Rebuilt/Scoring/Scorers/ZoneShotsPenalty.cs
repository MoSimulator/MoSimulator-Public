using MoSimCore.Enums;
using MoSimCore.Interfaces;
using UnityEngine;

namespace Games.Rebuilt.Scoring
{
    public class ZoneShotsPenalty : MonoBehaviour, IScorer
    {
        [field: SerializeField] public Alliance Alliance { get; private set; }
        
        public static int RedInstances;
        public static int BlueInstances;
        
        void Start()
        {
            RedInstances = 0;
            BlueInstances = 0;
        }
        
        private void Awake()
        {
            RedInstances = 0;
            BlueInstances = 0;
        }
        
        void IScorer.AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is RebuiltScoreData rebuiltScoreData)
            {
                if (Alliance == Alliance.Blue)
                {
                    rebuiltScoreData.PenaltyEarned += calculatePoints(BlueInstances);
                }
                else if (Alliance == Alliance.Red)
                {
                    rebuiltScoreData.PenaltyEarned += calculatePoints(RedInstances);
                }
            }
        }
        
        private MoSimLib.SecureFloat calculatePoints(int instances)
        {
            MoSimLib.SecureFloat totalPoints = instances * 5;
            return totalPoints;
        }
    }
}
