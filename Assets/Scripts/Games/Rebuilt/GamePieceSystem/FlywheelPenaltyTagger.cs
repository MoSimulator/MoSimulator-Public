using Games.Rebuilt.GamePieceSystem;
using MoSimLib;
using UnityEngine;

public class FlywheelPenaltyTagger : MonoBehaviour
{
    [SerializeField] private LayerMask gamePieceLayers;

    [Header("Debug")]
    [Tooltip("Count the game pieces this tagger claims and releases. Off by default; the counters " +
             "below only move while this is on, so toggling it mid-run starts a fresh window rather " +
             "than back-filling what was already missed.")]
    [SerializeField] private bool debugCounts;

    [Tooltip("Pieces claimed since counting began. Read-only - written by the tagger.")]
    [SerializeField] private int totalTagged;

    [Tooltip("Pieces claimed and not yet released. Read-only - written by the tagger.")]
    [SerializeField] private int currentlyTagged;

    /// <summary>Pieces claimed since counting began. Zero unless the debug counters are enabled.</summary>
    public int TotalTagged => totalTagged;

    /// <summary>Pieces claimed and not yet released. Zero unless the debug counters are enabled.</summary>
    public int CurrentlyTagged => currentlyTagged;

    /// <summary>Clears both counters without disturbing what is currently tagged.</summary>
    public void ResetCounts()
    {
        totalTagged     = 0;
        currentlyTagged = 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsGamePiece(other)) return;
        var obj = Utils.FindParentObjectComponent<RebuiltGamePieceController>(other.gameObject);
        obj.parentBody = gameObject;

        if (!debugCounts) return;
        totalTagged++;
        currentlyTagged++;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsGamePiece(other)) return;
        var obj = Utils.FindParentObjectComponent<RebuiltGamePieceController>(other.gameObject);
        obj.parentBody = null;

        // Floored rather than allowed to go negative: a piece that entered while counting was off,
        // or before the last ResetCounts, still exits through here and would otherwise drive the
        // live count below zero and keep it permanently offset.
        if (!debugCounts) return;
        if (currentlyTagged > 0) currentlyTagged--;
    }

    private bool IsGamePiece(Collider other)
    {
        if (gamePieceLayers == 0) return true;
        return (gamePieceLayers.value & (1 << other.gameObject.layer)) != 0;
    }
}
