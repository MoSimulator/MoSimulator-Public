using Newtonsoft.Json.Linq;
using UnityEngine.Networking;
using UnityEngine;
using System.Collections;

namespace MoSimLib
{
    /// <summary>
    /// A simple utility class to check the game version against a required version.
    /// This can be used to ensure that players are running on the latest version of the game.
    /// </summary>
    public class GameVersionChecker : MonoBehaviour
    {
        private static GameVersionChecker _instance;
        public static GameVersionChecker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<GameVersionChecker>();
                }
                return _instance;
            }
        }

        private void Awake()
        {
            // The fetch used to run before this check, so every duplicate loaded with a scene fired
            // its own request at the API on the way to being destroyed.
            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(this.gameObject);
            StartCoroutine(GetRequest());
        }

        string RequiredVersion = "";
        /// <summary>
        /// Checks if the current game version matches the required version.
        /// </summary>
        /// <returns>True if the versions match, false otherwise.</returns>
        public bool IsGameVersionValid()
        {
            // In a real implementation, you would retrieve the current game version
            // from a reliable source, such as a server or a configuration file.
            // For this example, we'll just use a hardcoded value.
            string currentVersion = Application.version;
            return currentVersion == RequiredVersion;
        }

        /// <summary>
        /// Fetches the required version, retrying on failure. One dropped request leaves
        /// RequiredVersion empty, which silently blocks every leaderboard submission for the rest of
        /// the session, so a single network blip is not allowed to settle it.
        /// </summary>
        private IEnumerator GetRequest()
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using (UnityWebRequest www = UnityWebRequest.Get("https://api.github.com/repos/mosimulator/mosimulator-public/releases/latest"))
                {
                    yield return www.SendWebRequest();
                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        JObject json = JObject.Parse(www.downloadHandler.text);
                        // Absent when the API answers with an error body rather than a release.
                        var tag = json["tag_name"]?.ToString();
                        if (!string.IsNullOrEmpty(tag))
                        {
                            RequiredVersion = tag.Trim('v');
                            Debug.Log("Required game version: " + RequiredVersion);
                            yield break;
                        }

                        Debug.Log("Version check response had no tag_name: " + www.downloadHandler.text);
                    }
                    else
                    {
                        Debug.Log(www.error);
                    }
                }

                yield return new WaitForSeconds(5f);
            }

            Debug.LogWarning("Version check failed, leaderboard submissions will be skipped this session");
        }


    }
}