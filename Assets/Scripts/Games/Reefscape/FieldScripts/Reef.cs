using Games.Reefscape.Scoring.Scorers;
using MoSimCore.Enums;
using UnityEngine;

namespace Games.Reefscape.FieldScripts
{
    public class Reef : MonoBehaviour
    {
        [field: SerializeField] public Alliance Alliance { get; private set; } = Alliance.Blue;


        [SerializeField] private ReefPoleScorer[] _scorers;

        /// <summary>
        /// returns if a branch is scored Follows https://firstfrc.blob.core.windows.net/frc2025/Manual/2025GameManual.pdf page 24 and l4:0, l3:1, l2:2
        /// </summary>
        /// <returns></returns>
        public bool[][] Scored()
        {
            bool[][] result = new bool[12][];
            for (int i = 0; i < 12; i++)
            {
                result[i] = _scorers[i].GetScoredHeights();
            }
            
            return result;
        }
    }
}