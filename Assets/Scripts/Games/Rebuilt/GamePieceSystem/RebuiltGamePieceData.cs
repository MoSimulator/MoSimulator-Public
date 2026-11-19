using RobotFramework.Interfaces;
using UnityEngine;

namespace Games.Rebuilt.GamePieceSystem
{
    [CreateAssetMenu(menuName = "Games/Rebuilt/RebuiltGamePieceData", order = 200)]
    public class RebuiltGamePieceData : ScriptableObject, IGamePieceData
    {
        public new string name;
        
        public bool hasSymmetry;
        public SymmetryType symmetryType = SymmetryType.XYZAxis;
        
        public string Name => name;
        public bool HasSymmetry => hasSymmetry;
        public SymmetryType SymmetryType => symmetryType;
    }
}