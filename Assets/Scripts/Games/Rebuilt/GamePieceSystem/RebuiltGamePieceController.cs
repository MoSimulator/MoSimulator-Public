using UnityEngine;
using MoSimCore.BaseClasses;
using MoSimCore.Enums;
using MoSimLib;
using Games.Rebuilt.Robots;
using Games.Rebuilt.Scoring;
using RobotFramework.Controllers.GamePieceSystem;

namespace Games.Rebuilt.GamePieceSystem
{
    public class RebuiltGamePieceController : GamePieceController<RebuiltGamePiece, RebuiltGamePieceData>
    {
        [HideInInspector] public int   SlotIndex = -1;
        public                  GameObject parentBody;

        private LayerMask  _layerMask;
        private Transform  _cachedTransform;
        
        [Tooltip("Seconds during which if 3 or more fuel are scored it counts zone penalties")]
        private static float zonePenaltyLeniencySeconds = 5f;

        private static float lastZonePenaltyTime = float.NegativeInfinity;
        private static int  consecutivePenalties = 0;

        [SerializeField]
        [Tooltip("Seconds after being scored during which this piece cannot score again.")]
        private float scoreDebounceSeconds = 1.5f;

        private float _lastScoredTime = float.NegativeInfinity;

        public bool scored
        {
            get => RebuiltGamePieceManager.Instance.Scored[SlotIndex];
            set => RebuiltGamePieceManager.Instance.Scored[SlotIndex] = value;
        }

        /// <summary>
        /// Claims a scoring event for this piece, returning false if it scored too recently to count
        /// again.
        ///
        /// Scorers track pieces by presence in their volume, so a piece that bounces out and straight
        /// back in reads as a brand new arrival and scores twice. This holds the piece off for a moment
        /// after it counts, which is long enough for a bounce to settle without blocking a genuine
        /// second cycle through the hub.
        /// </summary>
        public bool TryClaimScore()
        {
            if (Time.time - _lastScoredTime < scoreDebounceSeconds)
            {
                return false;
            }

            _lastScoredTime = Time.time;
            return true;
        }

        private new void Start()
        {
            base.Start();
            gamePiece.rigidbody.solverIterations         = 1;
            gamePiece.rigidbody.solverVelocityIterations = 1;

            _layerMask       = LayerMask.NameToLayer("Robot");
            _cachedTransform = transform;

            var mgr = RebuiltGamePieceManager.GetInstance();
            if (mgr == null)
            {
                SlotIndex = -1;
                enabled = false;
                return;
            }
            SlotIndex = mgr.Register(this);
        }

        private void OnDestroy()
        {
            if (RebuiltGamePieceManager.Instance != null)
                RebuiltGamePieceManager.Instance.Unregister(SlotIndex);
        }

        // Called by the manager each FixedUpdate; `slot` is passed in so
        // the JIT can keep it in a register rather than re-reading SlotIndex.
        public void ManagedFixedUpdate(float deltaTime, int slot)
        {
            if (gamePiece == null || gamePiece.rigidbody == null)
            {
                RebuiltGamePieceManager mgr0 = RebuiltGamePieceManager.Instance;
                mgr0.WatchingFor404[slot] = false;
                mgr0.HasOwner[slot]       = false;
                return;
            }

            RebuiltGamePieceManager mgr = RebuiltGamePieceManager.Instance;

            var     rb             = gamePiece.rigidbody;
            Vector3 currentPos     = _cachedTransform.position;
            Vector3 currentLocalPos = _cachedTransform.localPosition;

            // ---- Boundary check ----------------------------------------
            if (currentPos.z is < -4.1f or > 4.1f ||
                currentPos.x is < -9.4f or > 9.4f ||
                currentPos.y < -1f)
            {
                if (!mgr.HasReturnPoint[slot])
                {
                    Vector3 point = currentPos;
                    point.y = 1f;

                    if      (currentPos.x < -9.4f) { point.x = -7.5f; point.z =  4.15f; }
                    else if (currentPos.x >  9.4f) { point.x =  7.5f; point.z = -4.15f; }
                    else if (currentPos.y <  -1f)  { point.x = -7.5f; point.z =  4.15f; }

                    mgr.ReturnPoints[slot]    = point;
                    mgr.HasReturnPoint[slot]  = true;
                }
            }

            // ---- Return logic ------------------------------------------
            if (mgr.HasReturnPoint[slot])
            {
                mgr.ReturnCounters[slot] -= deltaTime;
                if (mgr.ReturnCounters[slot] > 0f) return;

                rb.MovePosition(mgr.ReturnPoints[slot]);
                Vector3 returnForce = Vector3.zero;
                if (currentLocalPos.z < -4.1f) returnForce.z = -80f;
                if (currentLocalPos.x < -8.4f) returnForce.x = -80f;
                if (currentLocalPos.z >  4.1f) returnForce.z =  80f;
                if (currentLocalPos.x >  8.4f) returnForce.x =  80f;

                rb.velocity = Vector3.zero;
                rb.AddForce(returnForce);

                mgr.HasReturnPoint[slot]  = false;
            }
            else
            {
                mgr.ReturnCounters[slot] = 3f;
            }

            // ---- Alliance & penalty logic ------------------------------
            if (parentBody && parentBody.layer == _layerMask)
            {
                var bot = Utils.FindParentObjectComponent<RebuiltRobotBase>(parentBody);
                if (bot == null)
                {
                    mgr.WatchingFor404[slot] = false;
                    mgr.HasOwner[slot]       = false;
                    return;
                }

                mgr.OwnedBy[slot]       = bot.Alliance;
                mgr.HasOwner[slot]      = true;
                mgr.WatchingFor404[slot] = !bot.InAllianceZone();
            }
            else if (rb.velocity.magnitude < 0.025f || currentPos.y < 0.04f)
            {
                mgr.WatchingFor404[slot] = false;
            }

            if (mgr.WatchingFor404[slot] && mgr.Scored[slot] && mgr.HasOwner[slot])
            {
                // Worked out first so the flash can follow what was actually awarded. The leniency
                // window forgives the first two shots, and those must not light the marker.
                var penaltyInstances = 0;

                if(Time.time - lastZonePenaltyTime < zonePenaltyLeniencySeconds){
                    consecutivePenalties++;
                    if(consecutivePenalties==3) {
                        penaltyInstances = 3;
                    }
                    else if (consecutivePenalties > 3){
                        penaltyInstances = 1;
                    }
                }
                else
                {
                    consecutivePenalties=1;
                }

                lastZonePenaltyTime= Time.time;

                if (penaltyInstances > 0)
                {
                    if   (mgr.OwnedBy[slot] == Alliance.Red)  ZoneShotsPenalty.RedInstances  += penaltyInstances;
                    else if (mgr.OwnedBy[slot] == Alliance.Blue) ZoneShotsPenalty.BlueInstances += penaltyInstances;

                    // Flashed here rather than in ZoneShotsPenalty: that recomputes from the counters on
                    // every scoring tick, so it has no notion of a penalty happening. The owning alliance
                    // is the one that committed it - its own PenaltyEarned is credited to the opponent.
                    PenaltyFlasher.Flash(mgr.OwnedBy[slot]);
                }

                mgr.WatchingFor404[slot] = false;
                mgr.HasOwner[slot]       = false;
            }

            mgr.Scored[slot] = false;
            
            
        }
    }
}
