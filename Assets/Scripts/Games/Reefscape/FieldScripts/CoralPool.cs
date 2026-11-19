using System.Collections;
using System.Collections.Generic;
using Games.Reefscape.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Games.Reefscape.FieldScripts
{
    /// <summary>
    /// Owns every coral the stations hand out, for as long as the scene is loaded. Coral is built
    /// once and then moves between two tiers instead of being created and destroyed:
    ///
    ///     dormant -> field -> dormant
    ///
    /// Dormant coral is switched off and left at the scene root, which is exactly how the old
    /// station buffer held it. The only thing that differs is when it is built: this pool fills once
    /// on load, so a drop never waits on an Instantiate and a restart never rebuilds anything.
    ///
    /// A restart reclaims coral instead of destroying it, so the one-time build is never repeated.
    /// </summary>
    public class CoralPool : MonoBehaviour
    {
        public static CoralPool Instance { get; private set; }

        [Tooltip("Coral available to each alliance for a whole match. The real field supplies 60 per alliance.")]
        [SerializeField] private int coralPerAlliance = 60;

        [Tooltip("Coral instantiated per second once the match is over, so testing can continue past the cap. The extras are destroyed on the next restart.")]
        [SerializeField] private float postMatchInstantiateRate = 2f;

        [Tooltip("Milliseconds per frame the initial build is allowed to spend instantiating.")]
        [SerializeField] private float buildBudgetMs = 2f;

        /// <summary>False until every alliance has its full pool built.</summary>
        public bool IsWarm { get; private set; }

        private GameObject _coralPrefab;

        private readonly Dictionary<Alliance, AlliancePool> _pools = new();

        /// <summary>Alliances already reported as having no pool, so the warning lands once.</summary>
        private readonly HashSet<Alliance> _warnedMissing = new();

        /// <summary>Everything a coral is addressed by, resolved once so the drop path allocates nothing.</summary>
        private class PooledCoral
        {
            public GameObject gameObject;
            public Transform transform;
            public Rigidbody rigidbody;
            public ReefscapeGamePieceController controller;
        }

        private class AlliancePool
        {
            public readonly Queue<PooledCoral> Dormant = new();
            public readonly List<PooledCoral> Issued = new();

            /// <summary>How many coral exist for this alliance in both tiers.</summary>
            public int Built;

            /// <summary>How many of those were built past the cap after the match ended.</summary>
            public int Overflow;

            public float NextGrow;
        }

        public static CoralPool Create(GameObject coralPrefab, IEnumerable<Alliance> alliances)
        {
            if (Instance)
            {
                return Instance;
            }

            var pool = new GameObject("CoralPool").AddComponent<CoralPool>();
            pool.Initialize(coralPrefab, alliances);
            return pool;
        }

        private void Initialize(GameObject coralPrefab, IEnumerable<Alliance> alliances)
        {
            Instance = this;
            _coralPrefab = coralPrefab;

            foreach (var alliance in alliances)
            {
                if (!_pools.ContainsKey(alliance))
                {
                    _pools[alliance] = new AlliancePool();
                }
            }

            StartCoroutine(Build());
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private IEnumerator Build()
        {
            var total = System.Diagnostics.Stopwatch.StartNew();
            var frame = System.Diagnostics.Stopwatch.StartNew();

            foreach (var pool in _pools.Values)
            {
                while (pool.Built < coralPerAlliance)
                {
                    BuildOne(pool);

                    if (frame.Elapsed.TotalMilliseconds < buildBudgetMs) continue;

                    yield return null;
                    frame.Restart();
                }
            }

            IsWarm = true;

            Debug.Log($"[CoralPool] Built {_pools.Count * coralPerAlliance} coral in " +
                      $"{total.Elapsed.TotalMilliseconds:F0} ms across {_pools.Count} alliance(s).");
        }

        private void BuildOne(AlliancePool pool)
        {
            // Left at the scene root rather than gathered under a pool parent, and switched off in
            // place. Transforms sharing a root are packed into one buffer, so parenting the whole
            // pool together would make every hand-out cut a subtree out of a 2,600-entry hierarchy
            // and compact what was left - a cost that grows with the pool. Storing them the way the
            // old station buffer did keeps each hand-out a move out of its own 44-entry hierarchy.
            var coral = Instantiate(_coralPrefab);
            coral.SetActive(false);

            pool.Dormant.Enqueue(new PooledCoral
            {
                gameObject = coral,
                transform = coral.transform,
                rigidbody = coral.GetComponent<Rigidbody>(),
                controller = coral.GetComponent<ReefscapeGamePieceController>()
            });

            pool.Built++;
        }

        /// <summary>
        /// The per-alliance cap is the real field's coral supply, so it holds for the whole match.
        /// Once the match is over, letting people keep testing matters more than the cap does, so the
        /// pool grows again - slowly, and <see cref="ReclaimAll"/> destroys the extras on the next
        /// restart to put the count back where it started.
        /// </summary>
        private bool TryGrow(AlliancePool pool)
        {
            if (BaseGameManager.Instance == null || BaseGameManager.Instance.GameState != GameState.End) return false;
            if (postMatchInstantiateRate <= 0f || Time.unscaledTime < pool.NextGrow) return false;

            BuildOne(pool);
            pool.Overflow++;
            pool.NextGrow = Time.unscaledTime + 1f / postMatchInstantiateRate;
            return true;
        }

        /// <summary>
        /// Hands a coral to a station. Fails once an alliance has all of its coral on the field, and
        /// the station simply stops feeding - the same way a real one runs out.
        /// </summary>
        public bool TryTake(Alliance alliance, Transform parent, Vector3 position, Quaternion rotation, out Rigidbody rb)
        {
            rb = null;

            if (!IsWarm) return false;

            if (!_pools.TryGetValue(alliance, out var pool))
            {
                // Once per alliance: a station asking for a pool that was never built feeds nothing at
                // all, and that is otherwise a silent failure for one whole side of the field.
                if (_warnedMissing.Add(alliance))
                {
                    Debug.LogWarning($"[CoralPool] No pool for {alliance}, so its stations cannot feed. " +
                                     $"Built: {string.Join(", ", _pools.Keys)}.");
                }

                return false;
            }

            PooledCoral coral = null;
            while (coral == null)
            {
                if (pool.Dormant.Count == 0 && !TryGrow(pool)) return false;

                coral = pool.Dormant.Dequeue();
                if (coral.gameObject) break;

                pool.Built--;
                coral = null;
            }

            // Placed and parented while still switched off, so the coral never exists as a live body
            // anywhere but the spawn point and cannot be shoved out of whatever it would otherwise
            // have been left inside.
            coral.transform.SetPositionAndRotation(position, rotation);
            coral.transform.SetParent(parent, true);
            coral.gameObject.SetActive(true);

            rb = coral.rigidbody;
            rb.position = position;
            rb.rotation = rotation;
            rb.isKinematic = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.detectCollisions = true;
            rb.useGravity = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            pool.Issued.Add(coral);
            return true;
        }

        /// <summary>
        /// Pulls every coral back to the dormant tier, drops any post-match extras and replaces
        /// anything unusable. Has to run
        /// before the game manager destroys the robots: a carried coral has its collider child
        /// parented onto the robot, and losing that would leave the coral permanently unusable.
        /// </summary>
        public void ReclaimAll()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var reclaimed = 0;

            foreach (var pool in _pools.Values)
            {
                reclaimed += pool.Issued.Count;

                foreach (var coral in pool.Issued)
                {
                    Reclaim(pool, coral);
                }

                pool.Issued.Clear();

                while (pool.Overflow > 0 && pool.Dormant.Count > 0)
                {
                    var extra = pool.Dormant.Dequeue();
                    if (extra.gameObject)
                    {
                        Destroy(extra.gameObject);
                    }

                    pool.Overflow--;
                    pool.Built--;
                }
            }

            // Replaces anything Reclaim had to discard. New coral goes straight into the inactive
            // root, so the destruction pass that follows cannot see it. Skipped while the initial
            // build is still running, which owns the counts and would otherwise be raced into topping
            // the pool up to full in a single frame.
            if (IsWarm)
            {
                foreach (var pool in _pools.Values)
                {
                    while (pool.Built < coralPerAlliance)
                    {
                        BuildOne(pool);
                    }
                }
            }

            Debug.Log($"[CoralPool] Reclaimed {reclaimed} coral in {watch.Elapsed.TotalMilliseconds:F1} ms.");
        }

        private void Reclaim(AlliancePool pool, PooledCoral coral)
        {
            if (!coral.gameObject)
            {
                pool.Built--;
                return;
            }

            if (!coral.controller || !coral.controller.ReturnToPool())
            {
                Destroy(coral.gameObject);
                pool.Built--;
                return;
            }

            // Switched off before it is unparented, so the move out of the game piece world is done
            // on an object that is already inert.
            coral.gameObject.SetActive(false);
            coral.transform.SetParent(null, false);
            pool.Dormant.Enqueue(coral);
        }

    }
}
