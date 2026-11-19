using System;
using System.Collections.Generic;
using Games.Rebuilt.FieldScripts;
using Games.Rebuilt.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using MoSimCore.Interfaces;
using MoSimLib;
using UnityEngine;

namespace Games.Rebuilt.Scoring
{
    public class HubScoring : MonoBehaviour, IScorer
    {
        [field: SerializeField] public Alliance Alliance { get; private set; }

        private HashSet<GameObject> scoredPieces;
        private HashSet<GameObject> previousOccupyObjects;
        private MoSimLib.SecureFloat autoScore;
        private MoSimLib.SecureFloat totalScore;
        private List<RebuiltGamePieceController> occupyObjects;
        [SerializeField] private Collider[] occupyColliders;
        private HashSet<RebuiltGamePieceController> uniquePieces;
        private Vector3[] halfExtents;
        private List<RebuiltGamePieceController> pieces;
        private LayerMask peiceMask;
        private float disableDelay;

        private KeyCode[] konamiSequence = new KeyCode[]
    {
        KeyCode.UpArrow, KeyCode.UpArrow,
        KeyCode.DownArrow, KeyCode.DownArrow,
        KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.LeftArrow, KeyCode.RightArrow,
        KeyCode.B, KeyCode.A
    };
        public static bool godMode = false;

        private int currentIndex = 0;

        private void Awake()
        {
            scoredPieces = new HashSet<GameObject>();
            previousOccupyObjects = new HashSet<GameObject>();
            halfExtents = new Vector3[occupyColliders.Length];
            occupyObjects = new List<RebuiltGamePieceController>();
            uniquePieces = new HashSet<RebuiltGamePieceController>();
            pieces = new List<RebuiltGamePieceController>();
            godMode = false;
            totalScore = 0;
            autoScore = 0;
            disableDelay = 3;
            peiceMask = LayerMask.GetMask("GamePiece1");

            foreach (var coll in occupyColliders)
            {
                Vector3 localHalfExtents = Vector3.zero;
                int index = Array.IndexOf(occupyColliders, coll);

                if (coll is BoxCollider boxCollider)
                {
                    localHalfExtents = boxCollider.size / 2f;
                }
                else if (coll is CapsuleCollider capsuleCollider)
                {
                    switch (capsuleCollider.direction)
                    {
                        case 0:
                            localHalfExtents = new Vector3(capsuleCollider.height / 2f, capsuleCollider.radius, capsuleCollider.radius);
                            break;
                        case 1:
                            localHalfExtents = new Vector3(capsuleCollider.radius, capsuleCollider.height / 2f, capsuleCollider.radius);
                            break;
                        case 2:
                            localHalfExtents = new Vector3(capsuleCollider.radius, capsuleCollider.radius, capsuleCollider.height / 2f);
                            break;
                    }
                }

                if (index >= 0 && index < halfExtents.Length)
                    halfExtents[index] = localHalfExtents;
            }
        }

        protected void Update()
        {
            // At game end, keep scoring for 3 seconds then stop
            if (BaseGameManager.Instance.GameState == GameState.End)
            {
                if (disableDelay > 0)
                {
                    disableDelay -= Time.deltaTime;
                    PoolOccupyObjects();
                    CompareObjects();
                }
                return;
            }

            if (BaseGameManager.Instance.GameState == GameState.Auto)
            {

                PoolOccupyObjects();

                disableDelay = 3;
            }
            else if ((RebuiltShifts.ActiveHub == ActiveHub.Red && Alliance == Alliance.Red) ||
                (RebuiltShifts.ActiveHub == ActiveHub.Blue && Alliance == Alliance.Blue) ||
                RebuiltShifts.ActiveHub == ActiveHub.Both)
            {
                PoolOccupyObjects();

                disableDelay = 3;
            }
            else
            {
                if (disableDelay > 0)
                {
                    disableDelay -= Time.deltaTime;
                    PoolOccupyObjects();
                }
                else
                {
                    scoredPieces.Clear();
                    occupyObjects.Clear();
                }
            }

            CompareObjects();

            //konami code stuff
            if (Input.GetKeyDown(konamiSequence[currentIndex]))
            {
                currentIndex++;

                if (currentIndex >= konamiSequence.Length)
                {
                    // Konami code entered successfully
                    godMode = true; // Activate god mode
                    BaseGameManager.Instance.wasCheated = true;
                    currentIndex = 0; // Reset the index for future inputs
                }
            }
            else if (Input.anyKeyDown)
            {
                currentIndex = 0; // Reset if any other key is pressed
            }
        }

        private void PoolOccupyObjects()
        {
            occupyObjects = OccupyPieces();
        }

        private void CompareObjects(bool shouldScore = true)
        {
            HashSet<GameObject> currentObjects = new HashSet<GameObject>();
            foreach (var piece in occupyObjects)
            {
                currentObjects.Add(piece.gameObject);

                if (!scoredPieces.Contains(piece.gameObject))
                {
                    // Tracked either way. A piece still inside its debounce window has to be recorded as
                    // present, or it would be treated as a fresh arrival the moment the window expires
                    // while it is sitting in the hub.
                    scoredPieces.Add(piece.gameObject);

                    if (!piece.TryClaimScore())
                    {
                        continue;
                    }

                    piece.scored = true;

                    if (shouldScore)
                    {
                        if (BaseGameManager.Instance.GameState == GameState.Auto)
                            autoScore++;
                        else
                            totalScore++;
                    }
                }
            }

            foreach (var obj in scoredPieces)
            {
                if (!currentObjects.Contains(obj))
                {
                    if (obj.TryGetComponent(out RebuiltGamePieceController script))
                        script.scored = false;
                }
            }

            scoredPieces.RemoveWhere(obj => !currentObjects.Contains(obj));
        }

        private List<RebuiltGamePieceController> OccupyPieces()
        {
            uniquePieces.Clear();
            pieces.Clear();

            foreach (var coll in occupyColliders)
            {
                var overlapBox = Physics.OverlapBox(
                    coll.gameObject.transform.position,
                    halfExtents[Array.IndexOf(occupyColliders, coll)],
                    coll.gameObject.transform.rotation,
                    peiceMask);

                foreach (var box in overlapBox)
                {
                    var piece = Utils.FindParentObjectComponent<RebuiltGamePieceController>(box.gameObject);
                    if (!piece) continue;
                    if (uniquePieces.Contains(piece)) continue;
                    uniquePieces.Add(piece);
                }
            }

            pieces.AddRange(uniquePieces);
            return pieces;
        }

        public void AddScore(IScoreData scoreData, GameState gameState)
        {
            if (scoreData is RebuiltScoreData rebuiltScoreData)
            {
                rebuiltScoreData.FuelTele += godMode ? totalScore * 10 : totalScore;
                rebuiltScoreData.FuelAuto += godMode ? autoScore * 10 : autoScore;
            }
            else
            {
                Debug.LogError("Invalid score data type passed to BargeScorer.");
            }
        }
    }
}
