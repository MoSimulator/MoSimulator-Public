using System.Collections;
using UnityEngine;

namespace RobotFramework.Controllers.GamePieceSystem
{
    public abstract class RobotGamePieceControllerBase : MonoBehaviour
    {
        public abstract MoSimLib.MoSimResult<MoSimLib.Void> RunCoroutine(IEnumerator routine);
    }
}
