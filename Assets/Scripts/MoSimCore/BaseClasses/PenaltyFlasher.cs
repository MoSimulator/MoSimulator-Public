using System.Collections;
using MoSimCore.Enums;
using UnityEngine;

namespace MoSimCore.BaseClasses
{
    /// <summary>
    /// Flashes a marker on the side of the alliance that committed a penalty.
    ///
    /// Shared by every game rather than reimplemented per game: the markers sit at the same place in
    /// each GameGUI and behave identically, so only the trigger differs. A game's penalty code calls
    /// <see cref="Flash"/> with the offending alliance - which is the opposite of the alliance the
    /// penalty points are awarded to.
    /// </summary>
    public class PenaltyFlasher : MonoBehaviour
    {
        /// <summary>The flasher in the current scene, if there is one.</summary>
        public static PenaltyFlasher Instance { get; private set; }

        [Header("Markers")]
        [SerializeField]
        [Tooltip("Marker shown on the red side. Located by name under this object when left empty.")]
        private GameObject redMarker;

        [SerializeField]
        [Tooltip("Marker shown on the blue side. Located by name under this object when left empty.")]
        private GameObject blueMarker;

        [Header("Pattern")]
        [SerializeField]
        [Tooltip("Flashes played for a penalty.")]
        private int flashesPerPenalty = 3;

        [SerializeField]
        [Tooltip("Seconds those flashes take. Another penalty part-way through restarts the count from full.")]
        private float flashSequenceSeconds = 1f;

        private MarkerState _red;
        private MarkerState _blue;

        /// <summary>
        /// Flashes the offending alliance's marker. Safe to call when no flasher is present, so game
        /// code does not have to care whether the scene has one.
        /// </summary>
        public static void Flash(Alliance offendingAlliance)
        {
            if (Instance != null)
            {
                Instance.FlashAlliance(offendingAlliance);
            }
        }

        /// <summary>
        /// Starts the offending alliance flashing, or extends it if already going, so a burst of
        /// penalties runs on until a full sequence has played since the last one.
        /// </summary>
        public void FlashAlliance(Alliance offendingAlliance)
        {
            var state = offendingAlliance == Alliance.Blue ? _blue : _red;
            if (state?.Marker == null)
            {
                return;
            }

            state.Remaining = flashesPerPenalty;
            state.Routine ??= StartCoroutine(RunFlashes(state));
        }

        private void Awake()
        {
            Instance = this;

            _red = new MarkerState { Marker = ResolveMarker(redMarker, "RedPenaltyMarker") };
            _blue = new MarkerState { Marker = ResolveMarker(blueMarker, "BluePenaltyMarker") };

            SetMarkerActive(_red, false);
            SetMarkerActive(_blue, false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private IEnumerator RunFlashes(MarkerState state)
        {
            // Two halves per flash - one lit, one dark - so the whole run lands on flashSequenceSeconds.
            var halfCycle = Mathf.Max(0.01f, flashSequenceSeconds / Mathf.Max(1, flashesPerPenalty * 2));

            while (state.Remaining > 0)
            {
                state.Remaining--;

                SetMarkerActive(state, true);
                yield return new WaitForSeconds(halfCycle);

                SetMarkerActive(state, false);
                yield return new WaitForSeconds(halfCycle);
            }

            state.Routine = null;
        }

        private static void SetMarkerActive(MarkerState state, bool active)
        {
            if (state?.Marker != null)
            {
                state.Marker.SetActive(active);
            }
        }

        /// <summary>
        /// Falls back to finding a marker by name, since every game's GUI names these the same way.
        /// Keeps the component drop-in per scene without wiring both references each time.
        /// </summary>
        private GameObject ResolveMarker(GameObject assigned, string markerName)
        {
            if (assigned != null)
            {
                return assigned;
            }

            foreach (var child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == markerName)
                {
                    return child.gameObject;
                }
            }

            Debug.LogWarning($"[PenaltyFlasher]: No '{markerName}' found under '{name}'; that side will not flash.");
            return null;
        }

        private sealed class MarkerState
        {
            public GameObject Marker;
            public int Remaining;
            public Coroutine Routine;
        }
    }
}
