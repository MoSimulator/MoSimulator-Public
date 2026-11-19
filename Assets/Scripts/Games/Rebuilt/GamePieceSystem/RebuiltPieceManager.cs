using System.Collections.Generic;
using UnityEngine;
using MoSimCore.Enums;

namespace Games.Rebuilt.GamePieceSystem
{
    public class RebuiltGamePieceManager : MonoBehaviour
    {
        public static RebuiltGamePieceManager Instance { get; private set; }

        [SerializeField] private bool VelocityDirectionDebug = false;

        [Header("Fuel Compression")]
        [SerializeField, Range(0f, 0.05f)]
        [Tooltip("Maximum overlap in meters beneath the robot's chassis/bumper height. Hopper contacts stay rigid. Set to zero to disable. Does not change wheel raycasts.")]
        private float fuelCompressionDepth = 0.05f;
        [SerializeField, Range(0f, 0.95f)]
        [Tooltip("Upward contact-normal alignment where compression starts. Fades smoothly to full compression at the top.")]
        private float fuelCompressionStartDot = 0.25f;
        [SerializeField, Range(1f, 12f)]
        [Tooltip("Contact friction multiplier at full fuel compression. Ramps from normal friction at zero overlap. Set to 1 to disable the friction increase.")]
        private float fuelCompressionFrictionMultiplier = 6f;
        [SerializeField]
        [Tooltip("Let lower-chassis edges glide over fuel already compressed from above by the same robot. Adds up to 1 cm of edge-only clearance; ordinary side and hopper contacts stay rigid.")]
        private bool fuelCompressedEdgeGlide = true;

        private readonly RebuiltFuelContactModifier _fuelContacts = new();
        // --- Index tracking ---
        private readonly List<RebuiltGamePieceController> _pieces    = new(512);
        private readonly Stack<int>                       _freeSlots = new(64);

        // --- Struct-of-arrays: hot data read/written every FixedUpdate ---
        // Sized generously up front; grown on demand via GrowArrays().
        private const int InitialCapacity = 512;
        private int _capacity = InitialCapacity;

        // Nullable structs are expensive in tight loops; use a parallel bool instead.
        internal Vector3[] ReturnPoints       = new Vector3[InitialCapacity];
        internal bool[]    HasReturnPoint     = new bool   [InitialCapacity];
        internal float[]   ReturnCounters     = new float  [InitialCapacity];
        internal bool[]    WatchingFor404     = new bool   [InitialCapacity];
        internal bool[]    Scored             = new bool   [InitialCapacity];
        internal Alliance[]OwnedBy            = new Alliance[InitialCapacity];
        internal bool[]    HasOwner           = new bool   [InitialCapacity];

        // ---------------------------------------------------------------

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnEnable() => _fuelContacts.Enable();

        private void OnDisable() => _fuelContacts.Disable();

        private void OnDestroy()
        {
            _fuelContacts.Dispose();
            if (Instance == this) Instance = null;
        }

        public void RegisterRobot(Games.Rebuilt.Robots.RebuiltRobotBase robot) => _fuelContacts.RegisterRobot(robot);

        public static RebuiltGamePieceManager GetInstance()
        {
            if (Instance == null)
                Instance = FindFirstObjectByType<RebuiltGamePieceManager>();
            return Instance;
        }

        // Returns the slot index assigned to this controller.
        public int Register(RebuiltGamePieceController piece)
        {
            int slot;
            if (_freeSlots.Count > 0)
            {
                slot = _freeSlots.Pop();
            }
            else
            {
                slot = _pieces.Count;
                if (slot >= _capacity)
                    GrowArrays();
                _pieces.Add(null); // placeholder; replaced below
            }

            _pieces[slot]       = piece;
            _fuelContacts.RegisterFuel(slot, piece.GamePiece.rigidbody);
            ReturnCounters[slot] = 3f;
            HasReturnPoint[slot] = false;
            WatchingFor404[slot] = false;
            Scored[slot]         = false;
            HasOwner[slot]       = false;
            return slot;
        }

        public void Unregister(int slot)
        {
            if (slot < 0 || slot >= _pieces.Count) return;
            _fuelContacts.UnregisterFuel(slot);
            _pieces[slot] = null;
            _freeSlots.Push(slot);
        }

        private void GrowArrays()
        {
            int newCap = _capacity * 2;

            System.Array.Resize(ref ReturnPoints,   newCap);
            System.Array.Resize(ref HasReturnPoint, newCap);
            System.Array.Resize(ref ReturnCounters, newCap);
            System.Array.Resize(ref WatchingFor404, newCap);
            System.Array.Resize(ref Scored,         newCap);
            System.Array.Resize(ref OwnedBy,        newCap);
            System.Array.Resize(ref HasOwner,       newCap);

            _capacity = newCap;
        }

        private void FixedUpdate()
        {
            _fuelContacts.PrepareStep(Physics.gravity, fuelCompressionDepth, fuelCompressionStartDot,
                fuelCompressionFrictionMultiplier, fuelCompressedEdgeGlide);
            float deltaTime = Time.fixedDeltaTime;
            int   count     = _pieces.Count;

            for (int i = 0; i < count; i++)
            {
                RebuiltGamePieceController piece = _pieces[i];
                if (piece != null)
                {
                    piece.ManagedFixedUpdate(deltaTime, i);
                }
            }
        }
    }
}
