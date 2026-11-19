using UnityEngine;

namespace GameSystems.Management
{
    public class MatchRuntimeService : MonoBehaviour
    {
        private static MatchRuntimeService _instance;

        /// <summary>
        /// The active runtime service.
        ///
        /// Resolves the scene instance on demand rather than waiting for Awake. Unity does not order
        /// Awake between objects, and MenuManager activates menu roots from inside its own Awake - so a
        /// menu's OnEnable can reach this before this component has started. CurrentMatchData is a field
        /// initialiser, so an instance found this way is already usable.
        /// </summary>
        public static MatchRuntimeService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<MatchRuntimeService>(FindObjectsInactive.Include);
                }

                return _instance;
            }
        }

        [field: SerializeField]
        public MatchData CurrentMatchData { get; private set; } = new();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}