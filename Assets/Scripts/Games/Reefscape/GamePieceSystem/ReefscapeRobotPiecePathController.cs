using RobotFramework.Controllers.GamePieceSystem;

namespace Games.Reefscape.GamePieceSystem
{
    public class ReefscapeRobotPiecePathController : RobotPiecePathController<ReefscapeGamePiece, ReefscapeGamePieceData>
    {
            public ReefscapeRobotPiecePathController(GamePieceGraph graph, ReefscapeRobotGamePieceController.GamePieceControllerNode node) 
                : base(graph, node)
            {}
        
            public ReefscapeRobotPiecePathController(ReefscapeRobotGamePieceController.GamePieceControllerNode node) 
                : base(node)
            {}
    }
}