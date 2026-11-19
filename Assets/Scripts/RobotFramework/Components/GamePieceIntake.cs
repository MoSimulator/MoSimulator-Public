using System.Collections.Generic;
using MoSimLib;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEngine;
using UnityEngine.Serialization;

namespace RobotFramework.Components
{
    /// <summary>
    /// Abstract generic intake mechanism that handles game piece collection.
    /// Manages collision detection, force application, and smooth handoff to the robot controller.
    /// </summary>
    /// <typeparam name="TPiece">The type of game piece this intake collects.</typeparam>
    /// <typeparam name="TData">The data structure for this game piece type.</typeparam>
    public abstract class GamePieceIntake<TPiece, TData> : MonoBehaviour
        where TPiece : GamePiece<TData> where TData : IGamePieceData
    {
        private BoxCollider _intake;

        [SerializeField]
        [Tooltip("Array of BoxColliders defining intake regions.")]
        private BoxCollider[] intakeCollider;

        [FormerlySerializedAs("_gamePieces")]
        [SerializeField]
        [Tooltip("Parent GameObject containing game piece instances.")]
        private GameObject gamePieces;

        [FormerlySerializedAs("_hasGamePiece")]
        [Tooltip("Whether this intake currently holds a game piece.")]
        public bool hasGamePiece;

        /// <summary>Gets or sets the current game piece held by this intake.</summary>
        public GameObject GamePiece { get; set; }
        
        /// <summary>Gets or sets the game piece controller managing this intake.</summary>
        public GamePieceController<TPiece, TData> GamePieceController { get; set; }

        [Tooltip("Whether to request intake during the next update.")]
        public bool requestIntake;

        [HideInInspector]
        public string pieceName;

        [Header("Linear Force Settings")]
        [SerializeField]
        [Tooltip("Intake speed in inches/sec.")]
        private float intakeSpeed;

        [SerializeField]
        [Tooltip("Intake force in inch-lb.")]
        private float intakeForce;

        [Header("Tolerance Settings")]
        [SerializeField]
        [Tooltip("If true, use planar tolerance mode instead of distance-based.")]
        private bool usePlanarTolerancing = false;

        [SerializeField]
        [Tooltip("Box extents defining the intake boundary in inches.")]
        private Vector3 boxExtents;

        [SerializeField]
        [Tooltip("Maximum distance from center (inches in distance mode, multiplier in planar mode).")]
        private float maxDistance;

        [SerializeField]
        [Tooltip("Required accuracy for smooth handoff in inches.")]
        private float accuracy;

        [Header("AxisLockingSettings")]
        [SerializeField]
        [Tooltip("Lock motion along the X axis.")]
        private bool lockXAxis = false;

        [SerializeField]
        [Tooltip("Lock motion along the Y axis.")]
        private bool lockYAxis = false;
        
        [SerializeField]
        [Tooltip("Lock motion along the Z axis.")]
        private bool lockZAxis = false;

        [Header("RotationSettings")]
        [SerializeField]
        [Tooltip("Rotation force in degrees.")]
        private float roationForce;

        [SerializeField]
        [Tooltip("Maximum rotation speed in deg/sec.")]
        private float maxRotationSpeed;
        
        [SerializeField]
        [Tooltip("Whether to apply rotation forces.")]
        private bool useRotaion = true;

        [Header("TargetSettings")]
        [SerializeField]
        [Tooltip("If true, smoothly hand off piece instead of immediately zeroing velocity.")]
        public bool smoothHandoff = false;

        [SerializeField]
        [Tooltip("Transform target for piece delivery.")]
        private Transform target;
        
        private int _results;
        
        [Tooltip("Whether the game piece is secured (no longer needs containment).")]
        public bool securedGamePiece;
        
        private Vector3[] _halfExtents;

        private Color _gizmoColor = Color.magenta;
        
        private List<Collider> colliders = new List<Collider>();
        
        private void Start()
        {
            requestIntake = false;
            gamePieces = null;
            hasGamePiece = false;
            
            _halfExtents = new Vector3[intakeCollider.Length];
            for (int i = 0; i < intakeCollider.Length; i++)
            {
                _halfExtents[i] =
                    Utils.MultiplyVectors(intakeCollider[i].size, intakeCollider[i].transform.lossyScale) / 2;
            }
        }

        public MoSimResult<MoSimLib.Void> RemovePiece()
        {
            var version = MoSim.ActiveVersion;
            RemovePieceV1();
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void RemovePieceV1()
        {
            requestIntake = false;
            hasGamePiece = false;
            GamePiece = null;
            GamePieceController = null;
            gamePieces = null;
            securedGamePiece = false;
        }

        public MoSimResult<MoSimLib.Void> ChangeTarget(Transform newTarget)
        {
            var version = MoSim.ActiveVersion;
            ChangeTargetV1(newTarget);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void ChangeTargetV1(Transform newTarget)
        {
            target = newTarget;
        }

        public MoSimResult<MoSimLib.Void> setForce(float Force)
        {
            var version = MoSim.ActiveVersion;
            SetForceV1(Force);
            return MoSimResult<MoSimLib.Void>.Success(new MoSimLib.Void());
        }

        private void SetForceV1(float Force)
        {
            intakeForce = Force;
        }

        private void FixedUpdate()
        {
            if (requestIntake && gamePieces != null && !hasGamePiece)
            {
                GamePiece = gamePieces;
                GamePieceController = gamePieces.GetComponent<GamePieceController<TPiece, TData>>();
                if (GamePieceController != null && GamePieceController.PieceName == pieceName)
                {
                    _results = 0;
                    var result = GamePieceController.MoveBreakable(target, intakeSpeed, intakeForce, maxDistance,
                        accuracy, roationForce, maxRotationSpeed, useRotaion, smoothHandoff, usePlanarTolerancing,
                        boxExtents, lockXAxis, lockYAxis, lockZAxis);
                    hasGamePiece = true;
                    GamePiece = gamePieces;
                    if (result == 1)
                    {
                        securedGamePiece = true;
                    }
                }
            }
            else
            {
                if (hasGamePiece && !requestIntake)
                {
                    hasGamePiece = false;
                    GamePiece = null;
                    GamePieceController = null;
                    gamePieces = null;
                }

                if (hasGamePiece && GamePieceController != null && _results != 1)
                {
                    _results = GamePieceController.MoveBreakable(target, intakeSpeed, intakeForce, maxDistance, accuracy,
                        roationForce, maxRotationSpeed, useRotaion, smoothHandoff, usePlanarTolerancing, boxExtents,
                        lockXAxis, lockYAxis, lockZAxis);
                    if (Mathf.Approximately(_results, -1))
                    {
                        hasGamePiece = false;
                        GamePiece = null;
                        GamePieceController = null;
                        gamePieces = null;
                    }

                    if (_results == 1)
                    {
                        securedGamePiece = true;
                    }
                }
            }

            if (gamePieces || !requestIntake) return;
            var mask = LayerMask.GetMask(pieceName);
            
            colliders.Clear();

            for (int i = 0; i < intakeCollider.Length; i++)
            {
                var coll = Physics.OverlapBox(intakeCollider[i].transform.position, _halfExtents[i],
                    transform.rotation, mask);
                foreach (var col in coll)
                {
                    if (!colliders.Contains(col))
                    {
                        colliders.Add(col);
                    }
                }
            }

            foreach (Collider coll in colliders)
            {
                if (coll.CompareTag("Untagged")) continue;
                var objectThing = coll.gameObject;
                if (!objectThing.TryGetComponent(out GamePieceController<TPiece, TData> controller)) continue;
                // if (controller.gamePieceType != intakeType) continue; TODO: Fix this
                if (gamePieces) continue;
                gamePieces = objectThing;
                break;
            }
        }

        //gizmo stuff
        private void OnDrawGizmos()
        {
            DrawAABBGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            // Draw a different color when selected
            Color originalColor = _gizmoColor;
            _gizmoColor = Color.yellow;
            DrawAABBGizmo();
            _gizmoColor = originalColor;
        }

        private void DrawAABBGizmo()
        {
            if (target == null)
                return;

            if (usePlanarTolerancing)
            {
                // Store the original gizmo matrix
                Matrix4x4 originalMatrix = Gizmos.matrix;

                // Set the gizmo matrix to match the center transform's position, rotation, and scale
                Gizmos.matrix = Matrix4x4.TRS(target.position, target.rotation, Vector3.one);

                // Set gizmo color
                Gizmos.color = _gizmoColor;

                // Reset color for wireframe
                Gizmos.color = _gizmoColor;
                DrawWireCubeWithDepth(Vector3.zero, boxExtents);


                // Restore the original gizmo matrix
                Gizmos.matrix = originalMatrix;
            }
            else
            {
                // Set gizmo color
                Gizmos.color = _gizmoColor;

                // Reset color for wireframe
                Gizmos.color = _gizmoColor;
                Gizmos.DrawWireSphere(target.position, accuracy * 0.0254f);
            }
        }

        private void DrawWireCubeWithDepth(Vector3 center, Vector3 size)
        {
            size *= 0.0254f;
            Vector3 halfSize = size * 0.5f;

            // Define the 8 corners of the cube in local space
            Vector3[] corners = new Vector3[8]
            {
                center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z), // 0: left-bottom-back
                center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z), // 1: right-bottom-back
                center + new Vector3(halfSize.x, halfSize.y, -halfSize.z), // 2: right-top-back
                center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z), // 3: left-top-back
                center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z), // 4: left-bottom-front
                center + new Vector3(halfSize.x, -halfSize.y, halfSize.z), // 5: right-bottom-front
                center + new Vector3(halfSize.x, halfSize.y, halfSize.z), // 6: right-top-front
                center + new Vector3(-halfSize.x, halfSize.y, halfSize.z) // 7: left-top-front
            };

            // Draw the 12 edges of the cube
            // Back face
            Gizmos.DrawLine(corners[0], corners[1]);
            Gizmos.DrawLine(corners[1], corners[2]);
            Gizmos.DrawLine(corners[2], corners[3]);
            Gizmos.DrawLine(corners[3], corners[0]);

            // Front face
            Gizmos.DrawLine(corners[4], corners[5]);
            Gizmos.DrawLine(corners[5], corners[6]);
            Gizmos.DrawLine(corners[6], corners[7]);
            Gizmos.DrawLine(corners[7], corners[4]);

            // Connecting edges
            Gizmos.DrawLine(corners[0], corners[4]);
            Gizmos.DrawLine(corners[1], corners[5]);
            Gizmos.DrawLine(corners[2], corners[6]);
            Gizmos.DrawLine(corners[3], corners[7]);
        }
    }
}
