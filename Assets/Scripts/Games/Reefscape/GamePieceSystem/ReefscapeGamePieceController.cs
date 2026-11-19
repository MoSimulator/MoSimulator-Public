using RobotFramework.Controllers.GamePieceSystem;

namespace Games.Reefscape.GamePieceSystem
{
    public class ReefscapeGamePieceController : GamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>
    {
        protected new void Start()
        {
            base.Start();

            if (gamePiece.rigidbody != null)
            {
                gamePiece.rigidbody.solverIterations = 6;
                gamePiece.rigidbody.solverVelocityIterations = 2;
            }
        }
    }
}