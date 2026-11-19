using System.Collections;
using System.Collections.Generic;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Games.Reefscape.Scoring.Scorers
{
    public class ProcessorScorer : MonoBehaviour, IScorer
    {
        [Header("Alliance Color")]
        [field: SerializeField]
        public Alliance Alliance { get; private set; }

        [Header("Triggers")] 
        [SerializeField] private BoxCollider scoreTrigger;
        [SerializeField] private BoxCollider launchTrigger;
        [SerializeField] private BoxCollider G406Trigger;
        [SerializeField] private BoxCollider Shot1Trigger;
        [SerializeField] private BoxCollider Shot2Trigger;
        [SerializeField] private BoxCollider Shot3Trigger;

        private MoSimLib.SecureFloat _algaeScored;
        private readonly HashSet<int> _piecesInProcessor = new();
        private readonly Queue<Collider> _launchQueue = new();
        private bool _processingQueue = false;

        [Header("Launch Settings")] 
        [Tooltip("Force applied at the very end. Keep low for a gentle drop.")]
        [SerializeField] private float launchForce = 0.2f; 
        [FormerlySerializedAs("launchTarget")] [SerializeField] private Transform launchTarget1;
        [SerializeField] private Transform launchTarget2;
        [SerializeField] private Transform launchTarget3;
        [SerializeField] private float arcHeight = 3.0f; // Slightly higher arc makes the drop more vertical
        
        [Tooltip("How long to wait inside the processor before the toss starts")]
        [SerializeField] private float processingDelay = 2f; 
        
        [SerializeField] private float pullDuration = 1.2f; // Increased duration for a slower, more graceful toss
        [SerializeField] private bool launchDuringAuto = true;

        [Header("Runtime Overrides")] 
        [SerializeField] private bool enforceMinPostExitDelay = true;
        [SerializeField] private float minPostExitDelay = 0.1f;

        [Header("Launch Audio")] 
        [SerializeField] private AudioSource launchAudio;
        [SerializeField] private AudioClip launchClip;

        private Transform _gamePieceParent;
        private bool _warnedParentNull = false;

        private Vector3 bounds;
        private Vector3 center;
        private Quaternion rot;
        private OverlapBoxBounds g406Trigger;
        private LayerMask coralMask;
        private LayerMask algaeMask;
        private float g406Timer;
        private int g406Counter;
        
        private OverlapBoxBounds shot1Trigger;
        private OverlapBoxBounds shot2Trigger;
        private OverlapBoxBounds shot3Trigger;

        private void Awake()
        {
            if (SceneManager.GetActiveScene().name == "MainMenuReefscape")
            {
                enabled = false;
                return;
            }

            _gamePieceParent = GameObject.FindGameObjectWithTag("GamePieceWorld")?.transform;

            if (scoreTrigger != null)
            {
                bounds = Utils.MultiplyVectors(scoreTrigger.transform.lossyScale, scoreTrigger.size) / 2;
                center = scoreTrigger.bounds.center;
                rot = scoreTrigger.transform.rotation;
            }

            g406Trigger = new OverlapBoxBounds(G406Trigger, false);
            coralMask = LayerMask.GetMask("Coral");
            g406Timer = 0;
            g406Counter = 0;
            
            shot1Trigger = new OverlapBoxBounds(Shot1Trigger, false);
            shot2Trigger = new OverlapBoxBounds(Shot2Trigger, false);
            shot3Trigger = new OverlapBoxBounds(Shot3Trigger, false);
            algaeMask = LayerMask.GetMask("Algae");
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is not ReefscapeScoreData reefscapeScoreData)
                return;

            reefscapeScoreData.NetPoints += _algaeScored * 6;
            reefscapeScoreData.AlgaeScored += _algaeScored;
            
            // reefscapeScoreData.PenaltyEarned += g406Counter * 6;
        }

        private void Update()
        {
            if (BaseGameManager.Instance.GameState == GameState.End)
                return;

            if (g406Trigger.OverlapBox(coralMask).Length > 0)
            {
                if (g406Counter == 0)
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 10)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                } else if (g406Timer >= 3)
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 5)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                }
                else
                {
                    g406Timer += Time.deltaTime;
                    if (g406Timer >= 1.5f)
                    {
                        g406Timer = 0;
                        g406Counter++;
                    }
                }
            }

            if (_gamePieceParent == null)
            {
                var gp = GameObject.FindGameObjectWithTag("GamePieceWorld");
                if (gp != null) _gamePieceParent = gp.transform;
            }

            var results = Physics.OverlapBox(center, bounds, rot);
            var currentIds = new HashSet<int>();

            foreach (var result in results)
            {
                if (!result.CompareTag("Algae") || _gamePieceParent == null || !result.transform.IsChildOf(_gamePieceParent) || LayerMask.LayerToName(result.gameObject.layer) != "Algae") 
                    continue;

                var id = result.gameObject.GetInstanceID();
                currentIds.Add(id);

                if (!_piecesInProcessor.Contains(id) && BaseGameManager.Instance.GameState != GameState.End)
                {
                    _piecesInProcessor.Add(id);
                    _algaeScored++;
                    _launchQueue.Enqueue(result);
                    result.gameObject.layer = LayerMask.NameToLayer("Default");
                    if (!_processingQueue)
                        StartCoroutine(ProcessLaunchQueue());
                }
            }

            var toRemove = new List<int>();
            foreach (var id in _piecesInProcessor)
            {
                if (!currentIds.Contains(id)) toRemove.Add(id);
            }
            foreach (var id in toRemove) _piecesInProcessor.Remove(id);
        }

        private IEnumerator ProcessLaunchQueue()
        {
            _processingQueue = true;
            while (_launchQueue.Count > 0)
            {
                var algaeCollider = _launchQueue.Dequeue();
                if (algaeCollider != null)
                    yield return StartCoroutine(LaunchAlgae(algaeCollider));
            }
            _processingQueue = false;
        }

        private IEnumerator LaunchAlgae(Collider algaeCollider)
        {
            // 1. GameState Logic
            bool detectedDuringAuto = BaseGameManager.Instance != null && BaseGameManager.Instance.GameState == GameState.Auto;
            if (detectedDuringAuto)
                yield return new WaitUntil(() => BaseGameManager.Instance.GameState == GameState.Teleop);
            else if (!launchDuringAuto)
                yield return new WaitUntil(() => BaseGameManager.Instance.GameState != GameState.Auto);

            // 2. The Processing Delay
            yield return new WaitForSeconds(processingDelay);

            GameObject algae = algaeCollider?.gameObject;
            if (algae == null) yield break;

            var rb = algae.GetComponent<Rigidbody>();
            if (rb == null) yield break;

            algaeCollider.enabled = false;
            rb.isKinematic = true;
            rb.useGravity = false;

            if (launchAudio != null && launchClip != null)
                launchAudio.PlayOneShot(launchClip);
            
            Vector3 startPos = algae.transform.position;
            Transform endPos = launchTarget1;

            int shot1Count = shot1Trigger.OverlapBox(algaeMask).Length;
            int shot2Count = shot2Trigger.OverlapBox(algaeMask).Length;
            int shot3Count = shot3Trigger.OverlapBox(algaeMask).Length;
            if (shot2Count < shot1Count || shot2Count < shot3Count)
            {
                endPos = launchTarget2;
            } else if (shot3Count < shot1Count || shot3Count < shot2Count)
            {
                endPos = launchTarget3;
            }
            
            Vector3 finalEndPos = endPos.position + new Vector3(0,0,Random.Range(-0.0254f*2, 0.0254f*2));
            
            // Create the curve apex
            Vector3 controlPoint = Vector3.Lerp(startPos, finalEndPos, 0.18f) + Vector3.up * arcHeight;

            float timeElapsed = 0f;
            while (timeElapsed < pullDuration)
            {
                float progress = timeElapsed / pullDuration;
                
                // Use SmoothStep for Ease-In/Ease-Out (Slows down at the end)
                float t = progress * progress * (3f - 2f * progress);

                Vector3 m1 = Vector3.Lerp(startPos, controlPoint, t);
                Vector3 m2 = Vector3.Lerp(controlPoint, finalEndPos, t);
                rb.MovePosition(Vector3.Lerp(m1, m2, t));
                
                timeElapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // 3. Cleanup and Gentle Release
            rb.MovePosition(finalEndPos);
            algaeCollider.enabled = true;
            rb.isKinematic = false;
            rb.useGravity = true;

            // Kill existing momentum from the animation
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero; 
            rb.gameObject.layer = LayerMask.NameToLayer("Algae");

            // Apply a very light "nudge" downward/forward to clear the rim
            Vector3 finalDirection = (-endPos.up).normalized;
            rb.AddForce(finalDirection * launchForce, ForceMode.Impulse);
        }

        public void ResetProcessor(Transform gamePieceParent)
        {
            StopAllCoroutines();
            _gamePieceParent = gamePieceParent;
            _algaeScored = 0;
            _piecesInProcessor.Clear();
            _launchQueue.Clear();
            _processingQueue = false;
        }
    }
}
