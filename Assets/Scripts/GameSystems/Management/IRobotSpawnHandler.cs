using System.Collections.Generic;
using RobotFramework;

namespace GameSystems.Management
{
    public interface IRobotSpawnHandler
    {
        void HandleSpawnedRobots(IReadOnlyList<RobotBase> spawnedRobots);
    }
}