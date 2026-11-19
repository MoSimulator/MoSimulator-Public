using Games.Reefscape.Enums;
using RobotFramework.Interfaces;
using UnityEngine;
using UnityEngine.Serialization;

namespace Games.Reefscape.GamePieceSystem
{
    [CreateAssetMenu(menuName = "Games/Reefscape/ReefscapeGamePieceData", order = 1000)]
    public class ReefscapeGamePieceData : ScriptableObject, IGamePieceData
    {
        public new string name;
        
        [FormerlySerializedAs("pieceType")] public ReefscapeGamePieceType reefscapeGamePieceType;
        public new bool hasSymmetry = true;
        public new SymmetryType symmetryType = SymmetryType.XZAxis;
        
        public string Name => name;
        public bool HasSymmetry => hasSymmetry;
        public SymmetryType SymmetryType => symmetryType;
    }
}