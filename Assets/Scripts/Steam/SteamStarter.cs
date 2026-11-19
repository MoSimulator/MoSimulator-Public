using UnityEngine;
using System.Collections;
using Steamworks;

namespace Steam
{
    public class SteamStarter : MonoBehaviour
    {
        public const uint AppId = 4398690;

        /// <summary>
        /// Set once Start has tried to initialise Steam, whether or not it succeeded. Check
        /// SteamClient.IsValid for the result; this only says it is no longer worth waiting.
        /// </summary>
        public static bool HasAttemptedInitialization { get; private set; }

        private void Start()
        {
            try
            {
                Debug.Log("[SteamStarter]: Initializing Steam API");
                SteamClient.Init(AppId, true);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[SteamStarter]: Failed to initialize Steam API");
                Debug.LogException(e);
            }
            finally
            {
                HasAttemptedInitialization = true;
            }
        }
    }
}