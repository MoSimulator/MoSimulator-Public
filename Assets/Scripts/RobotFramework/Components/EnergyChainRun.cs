using System.Collections.Generic;
using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>A reusable visual energy-chain route driven by ordered, optionally moving markers.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnergyChainMesh))]
    [AddComponentMenu("Robot Framework/Energy Chain Run")]
    public sealed class EnergyChainRun : MonoBehaviour
    {
        [Tooltip("Ordered route markers. Parent moving markers to the mechanism they follow.")]
        [SerializeField] private Transform[] waypoints = new Transform[0];
        [Tooltip("Dimensions and the up axis use this frame's local units. Defaults to this object.")]
        [SerializeField] private Transform referenceFrame;
        [SerializeField] private Vector3 upAxis = Vector3.up;
        [Min(0f)] [SerializeField] private float cornerRadius = 0.05f;
        [Range(3, 32)] [SerializeField] private int cornerSegments = 12;
        [Min(0.001f)] [SerializeField] private float chainWidth = 0.025f;
        [Min(0.001f)] [SerializeField] private float chainHeight = 0.018f;
        [Min(0.002f)] [SerializeField] private float linkLength = 0.018f;
        [Range(0f, 0.35f)] [SerializeField] private float linkGap = 0.06f;
        [SerializeField] private Material chainMaterial;

        [Header("Linear U-loop take-up (four-marker routes)")]
        [Tooltip("Offset the two return-bend markers by half the moving end's travel along Slide Axis. Keep those markers under the stationary run.")]
        [SerializeField] private bool followMovingEnd;
        [SerializeField] private Vector3 slideAxis = Vector3.forward;
        [Tooltip("Moving end position at setup, in Reference Frame local coordinates.")]
        [SerializeField] private Vector3 movingEndAtRest;

        private readonly List<Vector3> samples = new List<Vector3>();
        private Vector3[] waypointPositions;
        private Vector3[] sampledPath;
        private Matrix4x4 previousFrame;
        private Matrix4x4 previousTransform;
        private EnergyChainMesh chain;
        private bool dirty = true;
        private bool pathValid;

        private void OnEnable()
        {
            chain = GetComponent<EnergyChainMesh>();
            GetComponent<MeshRenderer>().enabled = true;
            dirty = true;
        }

        private void OnDisable()
        {
            var renderer = GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private void OnValidate() => dirty = true;

        private void LateUpdate()
        {
            var frame = referenceFrame != null ? referenceFrame : transform;
            if (NeedsRebuild(frame)) RebuildNow();
        }

        public void RebuildNow()
        {
            if (chain == null) chain = GetComponent<EnergyChainMesh>();
            if (chain == null) return;
            var frame = referenceFrame != null ? referenceFrame : transform;
            dirty = false;
            previousFrame = frame.localToWorldMatrix;
            previousTransform = transform.localToWorldMatrix;
            int count = waypoints != null ? waypoints.Length : 0;
            pathValid = false;
            if (waypointPositions == null || waypointPositions.Length != count) waypointPositions = new Vector3[count];
            if (count < 2)
            {
                chain.Clear();
                return;
            }
            for (int i = 0; i < count; i++)
            {
                if (waypoints[i] == null)
                {
                    chain.Clear();
                    return;
                }
                waypointPositions[i] = waypoints[i].position;
            }

            samples.Clear();
            Vector3 bendOffset = Vector3.zero;
            if (followMovingEnd && count == 4 && slideAxis.sqrMagnitude > 0.000001f)
            {
                Vector3 axis = slideAxis.normalized;
                Vector3 end = frame.InverseTransformPoint(waypointPositions[count - 1]);
                bendOffset = axis * (Vector3.Dot(end - movingEndAtRest, axis) * 0.5f);
            }
            samples.Add(frame.InverseTransformPoint(waypointPositions[0]));
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 before = RoutePoint(i - 1, frame, bendOffset);
                Vector3 corner = RoutePoint(i, frame, bendOffset);
                Vector3 after = RoutePoint(i + 1, frame, bendOffset);
                AddCorner(before, corner, after);
            }
            samples.Add(frame.InverseTransformPoint(waypointPositions[count - 1]));
            if (sampledPath == null || sampledPath.Length != samples.Count) sampledPath = new Vector3[samples.Count];
            for (int i = 0; i < samples.Count; i++) sampledPath[i] = frame.TransformPoint(samples[i]);
            Material material = chainMaterial != null ? chainMaterial : GetComponent<MeshRenderer>().sharedMaterial;
            chain.Rebuild(sampledPath, frame, upAxis.sqrMagnitude > 0.000001f ? upAxis.normalized : Vector3.up,
                chainWidth, chainHeight, linkLength, linkGap, material);
            pathValid = true;
        }

        private Vector3 RoutePoint(int index, Transform frame, Vector3 bendOffset)
        {
            Vector3 point = frame.InverseTransformPoint(waypointPositions[index]);
            return index > 0 && index < waypointPositions.Length - 1 ? point + bendOffset : point;
        }

        private bool NeedsRebuild(Transform frame)
        {
            if (dirty || previousFrame != frame.localToWorldMatrix || previousTransform != transform.localToWorldMatrix) return true;
            int count = waypoints != null ? waypoints.Length : 0;
            if (waypointPositions == null || waypointPositions.Length != count) return true;
            if (count < 2) return pathValid;
            for (int i = 0; i < count; i++)
            {
                if (waypoints[i] == null) return pathValid;
                if (waypointPositions[i] != waypoints[i].position) return true;
            }
            return !pathValid;
        }

        private void AddCorner(Vector3 before, Vector3 corner, Vector3 after)
        {
            Vector3 incoming = corner - before, outgoing = after - corner;
            float beforeLength = incoming.magnitude, afterLength = outgoing.magnitude;
            if (beforeLength < 0.00001f || afterLength < 0.00001f || cornerRadius <= 0f)
            {
                samples.Add(corner);
                return;
            }
            incoming /= beforeLength;
            outgoing /= afterLength;
            Vector3 normal = Vector3.Cross(incoming, outgoing);
            if (normal.sqrMagnitude < 0.000001f)
            {
                samples.Add(corner);
                return;
            }
            normal.Normalize();
            float turn = Vector3.Angle(incoming, outgoing);
            float slope = Mathf.Tan(turn * 0.5f * Mathf.Deg2Rad);
            float trim = Mathf.Min(cornerRadius * slope, Mathf.Min(beforeLength, afterLength) * 0.49f);
            float radius = trim / slope;
            Vector3 entry = corner - incoming * trim;
            Vector3 center = entry + Vector3.Cross(normal, incoming) * radius;
            Vector3 radial = entry - center;
            samples.Add(entry);
            int segments = Mathf.Clamp(cornerSegments, 3, 32);
            for (int i = 1; i <= segments; i++)
                samples.Add(center + Quaternion.AngleAxis(turn * i / segments, normal) * radial);
        }

        private void OnDrawGizmosSelected()
        {
            if (waypoints == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, 0.008f);
                if (i > 0 && waypoints[i - 1] != null) Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
            }
        }
    }
}
