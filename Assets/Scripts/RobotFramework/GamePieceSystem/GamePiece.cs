using System;
using RobotFramework.Interfaces;
using UnityEngine;

namespace RobotFramework.GamePieceSystem
{
    [Serializable]
    public class GamePiece<TData> where TData : IGamePieceData
    {
        public Rigidbody rigidbody;
        public GameObject gameObject;
        public Transform transform;
        public GameObject owner;
        
        public TData GamePieceData { get; private set; }
        
        public bool isScored;
        
        public MoSimLib.MoSimResult<MoSimLib.Void> Initialize(TData data, GameObject gameObject)
        {
            var version = MoSimLib.MoSim.ActiveVersion;
            InitializeV1(data, gameObject);
            return MoSimLib.MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void InitializeV1(TData data, GameObject gameObject)
        {
            GamePieceData = data;
            this.gameObject = gameObject;
        }
    }
}
