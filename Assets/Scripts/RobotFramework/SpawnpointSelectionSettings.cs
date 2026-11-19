using RobotFramework.Controllers.PidSystems;
using UnityEngine;

namespace RobotFramework
{
    public class SpawnpointSelectionSettings : MonoBehaviour
    {
        [HideInInspector] public PidConstants movePid = new PidConstants(15, 0, 1, 0, 0);
        [HideInInspector] public PidConstants rotatePid = new PidConstants(15, 0, 1, 0, 0);
        
        [SerializeField] public MeshRenderer[] bumperMeshes = null;
        [SerializeField] [Tooltip("Which Material Index is the Bumper Color")]
        public int materialIndex;
    }
}