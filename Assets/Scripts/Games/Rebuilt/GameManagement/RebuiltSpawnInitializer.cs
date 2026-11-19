using System;
using System.Collections.Generic;
using Games.Rebuilt.Robots;
using GameSystems.Management;
using RobotFramework;
using UnityEngine;

namespace Games.Rebuilt.GameManagement
{
    public class RebuiltSpawnInitializer : MonoBehaviour, IRobotSpawnHandler
    {
        [SerializeField]
        private RebuiltFuelPreloadManager preloadManager;

        public void HandleSpawnedRobots(IReadOnlyList<RobotBase> spawnedRobots)
        {
            if (spawnedRobots == null)
            {
                throw new ArgumentNullException(nameof(spawnedRobots));
            }

            if (preloadManager == null)
            {
                throw new InvalidOperationException($"RebuiltSpawnInitializer requires a RebuiltFuelPreloadManager.");
            }

            int preloadCount = 0;

            foreach (RobotBase robot in spawnedRobots)
            {
                if (robot is not RebuiltRobotBase rebuiltRobot)
                {
                    continue;
                }

                foreach (GameObject preload in rebuiltRobot.preloads)
                {
                    if (preload == null)
                    {
                        continue;
                    }

                    preload.transform.SetParent(preloadManager.CenterFuelParent, true);

                    preloadCount++;
                }
            }

            preloadManager.RemoveFuel(preloadCount);
        }

        public void SetPreloadManager(RebuiltFuelPreloadManager newPreloadManager)
        {
            if (newPreloadManager == null)
            {
                throw new ArgumentNullException(nameof(newPreloadManager));
            }

            preloadManager = newPreloadManager;
        }
    }
}