using UnityEngine;

namespace RobotFramework.Components
{
    /// <summary>Visual cable wrap and linear take-up. All geometry settings use the stationary frame.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Robot Framework/Turret Energy Chain")]
    [DefaultExecutionOrder(100)]
    public sealed class TurretWireWrap : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GenericTurret turret;
        [Tooltip("A frame fixed to the robot, never the rotating turret.")]
        [SerializeField] private Transform stationaryFrame;
        [SerializeField] private Transform turretAttachment;
        [SerializeField] private Transform slider;
        [SerializeField] private LineRenderer wire;

        [Header("Rectangular energy chain (stationary frame units)")]
        [Min(0.001f)] [SerializeField] private float chainWidth = 0.025f;
        [Min(0.001f)] [SerializeField] private float chainHeight = 0.018f;
        [Min(0.002f)] [SerializeField] private float chainLinkLength = 0.018f;
        [Range(0f, 0.35f)] [SerializeField] private float chainLinkGap = 0.06f;
        [Tooltip("Uses the original wire material when left empty.")]
        [SerializeField] private Material chainMaterial;

        [Header("Wrap (stationary frame units)")]
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 rotationAxis = Vector3.up;
        [SerializeField] private Vector3 departureDirection = Vector3.right;
        [Tooltip("Route from the feed side of the turret, along the base and parallel to the sliding guide.")]
        [SerializeField] private bool routeAlongGuide = true;
        [Min(0.001f)] [SerializeField] private float radius = 0.15f;
        [Tooltip("Signed wrap already present at the turret's zero angle, in degrees.")]
        [SerializeField] private float initialWrapDegrees = 180f;
        [Tooltip("Use -1 if the joint's positive angle unwinds this cable.")]
        [SerializeField] private float angleMultiplier = 1f;
        [Min(0f)] [SerializeField] private float layerSpacing = 0.008f;
        [Tooltip("Radius of the return bend at the rotating mount when the chain winds backwards.")]
        [Min(0.001f)] [SerializeField] private float reverseBendRadius = 0.035f;
        [Tooltip("Reverse rotation over which the mount return bend opens, avoiding a pop at zero wrap.")]
        [Min(1f)] [SerializeField] private float reverseTransitionDegrees = 60f;
        [Range(12, 256)] [SerializeField] private int segmentsPerTurn = 96;
        [Min(1f)] [SerializeField] private float maximumVisualTurns = 8f;

        [Header("Take-up slider (stationary frame units)")]
        [SerializeField] private Vector3 sliderRestPosition;
        [SerializeField] private Vector3 slideAxis = Vector3.forward;
        [Tooltip("A U-shaped take-up consumes two lengths of cable per unit of travel; normally 0.5.")]
        [SerializeField] private float travelPerCableLength = 0.5f;
        [SerializeField] private Vector2 travelLimits = new Vector2(-0.4f, 0.4f);
        [SerializeField] private Vector3 guideOffset;
        [SerializeField] private Vector3 fixedFeedPosition;
        [Min(0.001f)] [SerializeField] private float bendRadius = 0.025f;
        [Min(0.001f)] [SerializeField] private float leadLength = 0.12f;
        [Range(4, 64)] [SerializeField] private int bendSegments = 24;

        [Header("Lower fixed guide (stationary frame units)")]
        [Tooltip("Use an independently positioned circular guide instead of the automatic base corner. Requires Route Along Guide.")]
        [SerializeField] private bool useFixedGuide;
        [Tooltip("Center of the lower guide in Stationary Frame local coordinates.")]
        [SerializeField] private Vector3 fixedGuideCenter;
        [Tooltip("Cable centerline radius around the lower guide. Diameter is twice this value; include half the cable width for clearance.")]
        [Min(0.001f)] [SerializeField] private float fixedGuideRadius = 0.05f;

        private Vector3[] positions;
        private EnergyChainMesh chainMesh;
        private Vector3 attachmentAtZero;
        private bool capturedAttachment;
        public float SliderTravel { get; private set; }
        public bool TravelLimited { get; private set; }
        public bool FixedGuideInvalid { get; private set; }

        private void OnEnable() => capturedAttachment = false;

        private void LateUpdate()
        {
            if (turret == null) return;
            var angle = turret.CurrentAngle();
            if (angle.IsSuccess) RenderAngle(angle.Value);
        }

        /// <summary>Absolute angle relative to GenericTurret's startup zero; also usable for editor previews.</summary>
        public void RenderAngle(float angleDegrees, bool followAttachment = true)
        {
            if (stationaryFrame == null || wire == null || slider == null ||
                rotationAxis.sqrMagnitude < 0.000001f || slideAxis.sqrMagnitude < 0.000001f ||
                float.IsNaN(angleDegrees) || float.IsInfinity(angleDegrees)) return;

            Vector3 axis = rotationAxis.normalized;
            Vector3 radial = Vector3.ProjectOnPlane(departureDirection, axis).normalized;
            if (radial.sqrMagnitude < 0.5f) return;
            Vector3 travel = slideAxis.normalized;
            Vector3 restGuide = sliderRestPosition + guideOffset;
            // The return bend points away from the supply, independently of the signed slide axis.
            Vector3 forward = Vector3.ProjectOnPlane(travel, axis).normalized;
            if (Vector3.Dot(restGuide - fixedFeedPosition, forward) < 0f) forward = -forward;
            Vector3 side = Vector3.Cross(axis, forward).normalized;
            if (side.sqrMagnitude < 0.5f) return;
            // The turret connects to the inner run; the supply returns along the outer run.
            if (Vector3.Dot(side, center - restGuide) < 0f) side = -side;

            float initialSweep = initialWrapDegrees;
            float normalWinding = Mathf.Sign(initialWrapDegrees == 0f ? 1f : initialWrapDegrees);
            if (routeAlongGuide)
            {
                radial = -forward;
                if ((!capturedAttachment || !Application.isPlaying) && turretAttachment != null)
                {
                    attachmentAtZero = stationaryFrame.InverseTransformPoint(turretAttachment.position) - center;
                    // Also supports enabling the visual after the turret has already moved.
                    if (Application.isPlaying || followAttachment)
                        attachmentAtZero = Quaternion.AngleAxis(-angleDegrees * angleMultiplier, axis) * attachmentAtZero;
                    capturedAttachment = true;
                }
                if (capturedAttachment)
                {
                    float phase = Vector3.SignedAngle(radial, Vector3.ProjectOnPlane(attachmentAtZero, axis), axis);
                    initialSweep += Mathf.DeltaAngle(initialSweep, phase);
                }
                // Select the equivalent winding that leaves the circle toward the guide lane.
                float winding = Mathf.Sign(Vector3.Dot(-Vector3.Cross(axis, radial), restGuide - center));
                normalWinding = winding;
                if (winding > 0f && initialSweep < 0f) initialSweep += 360f;
                if (winding < 0f && initialSweep > 0f) initialSweep -= 360f;
            }

            float limit = Mathf.Clamp(maximumVisualTurns, 1f, 64f) * 360f;
            float sweep = Mathf.Clamp(initialSweep + angleDegrees * angleMultiplier, -limit, limit);
            Vector3 mount = center + Quaternion.AngleAxis(sweep, axis) * radial * Mathf.Max(0.001f, radius);
            if (followAttachment && turretAttachment != null)
                mount = stationaryFrame.InverseTransformPoint(turretAttachment.position);
            Vector3 mountRadial = Vector3.ProjectOnPlane(mount - center, axis).normalized;
            float mountRadius = Vector3.ProjectOnPlane(mount - center, axis).magnitude;
            if (mountRadial.sqrMagnitude < 0.5f) return;
            float reverseRadius = ReverseRadius(sweep, normalWinding);
            bool reversing = sweep * normalWinding < 0f;
            float coilSweep = sweep;
            if (reversing)
            {
                // The opposite winding has a different common tangent to the lower
                // guide. Until the mount passes that contact, there is no turret coil:
                // connect the mount's return bend directly to the lower guide.
                Vector3 target = useFixedGuide ? fixedGuideCenter : fixedFeedPosition;
                float targetRadius = useFixedGuide ? Mathf.Max(0.001f, fixedGuideRadius) : 0f;
                float guideWinding = Mathf.Sign(Vector3.Dot(Vector3.Cross(axis, -forward), -side));
                float reverseDegrees = -sweep * normalWinding;
                float outerRadius = Mathf.Max(0.001f, radius) + 2f * reverseRadius;
                coilSweep = 0f;
                for (int iteration = 0; iteration < 3; iteration++)
                {
                    if (!TryDepartureRadial(center, outerRadius, target, targetRadius, axis,
                            normalWinding, guideWinding, out Vector3 contactRadial)) break;
                    float approachDegrees = Mathf.Repeat(-normalWinding * Vector3.SignedAngle(radial, contactRadial, axis), 360f);
                    coilSweep = -normalWinding * Mathf.Max(0f, reverseDegrees - approachDegrees);
                    outerRadius = Mathf.Max(0.001f, radius) + 2f * reverseRadius
                        + Mathf.Max(0f, layerSpacing) * Mathf.Abs(coilSweep) / 360f;
                }
            }
            float radians = coilSweep * Mathf.Deg2Rad;
            float reverseCurlDegrees = normalWinding * 180f;
            Vector3 reverseBendCenter = mount + mountRadial * reverseRadius;
            Vector3 reverseExit = mount + mountRadial * (2f * reverseRadius);
            bool freeReverseLead = reversing && Mathf.Abs(coilSweep) < 0.001f;
            if (freeReverseLead && useFixedGuide && reverseRadius > 0f)
            {
                float guideWinding = Mathf.Sign(Vector3.Dot(Vector3.Cross(axis, -forward), -side));
                if (TryDepartureRadial(reverseBendCenter, reverseRadius, fixedGuideCenter,
                        Mathf.Max(0.001f, fixedGuideRadius), axis, normalWinding, guideWinding, out Vector3 bendExitRadial))
                {
                    reverseCurlDegrees = Vector3.SignedAngle(-mountRadial, bendExitRadial, axis);
                    if (normalWinding > 0f && reverseCurlDegrees < 0f) reverseCurlDegrees += 360f;
                    if (normalWinding < 0f && reverseCurlDegrees > 0f) reverseCurlDegrees -= 360f;
                    reverseExit = reverseBendCenter + bendExitRadial * reverseRadius;
                }
            }
            float wrappedLength = WrappedLength(coilSweep, normalWinding, reverseRadius)
                + reverseRadius * (Mathf.Abs(reverseCurlDegrees) * Mathf.Deg2Rad - Mathf.PI);
            float requestedTravel = -(wrappedLength - WrappedLength(initialSweep, normalWinding)) * travelPerCableLength * Mathf.Sign(Vector3.Dot(travel, forward));
            SliderTravel = Mathf.Clamp(requestedTravel, Mathf.Min(travelLimits.x, travelLimits.y), Mathf.Max(travelLimits.x, travelLimits.y));
            TravelLimited = !Mathf.Approximately(requestedTravel, SliderTravel);
            Vector3 guide = sliderRestPosition + travel * SliderTravel + guideOffset;
            slider.position = stationaryFrame.TransformPoint(sliderRestPosition + travel * SliderTravel);

            int arcSegments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(coilSweep) / 360f * Mathf.Clamp(segmentsPerTurn, 12, 256)));
            int bends = Mathf.Clamp(bendSegments, 4, 64);
            int reverseSegments = reverseRadius > 0f ? Mathf.Max(bends, Mathf.Clamp(segmentsPerTurn, 12, 256) / 2) : 0;
            int count = arcSegments + 1 + reverseSegments + bends * (routeAlongGuide ? 5 : 3);
            if (positions == null || positions.Length != count) positions = new Vector3[count];
            int index = 0;
            if (reverseSegments > 0)
            {
                // The incoming chain keeps its mounting direction. Turn it back around
                // the rotating mount before laying the reverse coil on an outer track.
                Add(ref index, mount);
                for (int i = 1; i < reverseSegments; i++)
                    Add(ref index, reverseBendCenter + Quaternion.AngleAxis(reverseCurlDegrees * i / reverseSegments, axis)
                        * (-mountRadial * reverseRadius));
            }
            for (int i = 0; i <= arcSegments; i++)
            {
                if (freeReverseLead)
                {
                    Add(ref index, reverseExit);
                    continue;
                }
                float t = (float)i / arcSegments;
                Vector3 direction = Quaternion.AngleAxis(-coilSweep * t, axis) * mountRadial;
                float contactRadius = reversing && Mathf.Abs(coilSweep) < 0.001f ? mountRadius : Mathf.Max(0.001f, radius);
                float r = Mathf.Lerp(mountRadius, contactRadius, t) + 2f * reverseRadius
                    + Mathf.Max(0f, layerSpacing) * Mathf.Abs(radians) * t / (2f * Mathf.PI);
                Vector3 point = center + direction * r;
                float heightBlend = reversing && Mathf.Abs(coilSweep) < 0.001f ? 1f : 1f - t;
                point += axis * (Vector3.Dot(mount - center, axis) * heightBlend);
                Add(ref index, point);
            }

            Vector3 departure = stationaryFrame.InverseTransformPoint(positions[index - 1]);
            Vector3 exitRadial = Vector3.ProjectOnPlane(departure - center, axis).normalized;
            Vector3 tangent = (-Mathf.Sign(sweep == 0f ? initialSweep : sweep) * Vector3.Cross(axis, exitRadial)).normalized;
            float bend = Mathf.Max(0.001f, bendRadius);
            float lead = Mathf.Max(0.001f, leadLength);
            Vector3 loopStart = guide + side * bend;
            Vector3 loopEnd = guide - side * bend;
            FixedGuideInvalid = false;
            if (routeAlongGuide)
            {
                float guideRadius = Mathf.Max(0.001f, fixedGuideRadius);
                float winding = Mathf.Sign(Vector3.Dot(Vector3.Cross(axis, -forward), -side));
                if (useFixedGuide && TryGuideArc(departure, loopStart, fixedGuideCenter, axis,
                        guideRadius, winding, out Vector3 entry, out Vector3 exit, out float guideSweep))
                {
                    for (int i = 1; i <= bends; i++)
                        Add(ref index, Vector3.Lerp(departure, entry, (float)i / bends));
                    Vector3 entryRadial = entry - fixedGuideCenter;
                    for (int i = 1; i <= bends; i++)
                        Add(ref index, fixedGuideCenter + Quaternion.AngleAxis(guideSweep * i / bends, axis) * entryRadial);
                    for (int i = 1; i <= bends; i++)
                        Add(ref index, Vector3.Lerp(exit, loopStart, (float)i / bends));
                }
                else
                {
                    FixedGuideInvalid = useFixedGuide;
                    if (reversing)
                    {
                        // No fixed circular guide: use the reverse departure's own
                        // approach, rather than forcing it back through the positive corner.
                        for (int i = 1; i <= bends * 3; i++)
                            Add(ref index, Bezier(departure, departure + tangent * lead,
                                loopStart - forward * lead, loopStart, (float)i / (bends * 3)));
                    }
                    else
                    {
                        // Positive wrapping retains the straight base and rounded corner.
                        Vector3 corner = loopStart + forward * Vector3.Dot(departure - loopStart, forward);
                        Vector3 baseDirection = Vector3.ProjectOnPlane(corner - departure, axis).normalized;
                        float cornerRadius = Mathf.Min(bend, Vector3.Distance(departure, corner) * 0.45f,
                            Mathf.Abs(Vector3.Dot(loopStart - corner, forward)) * 0.45f);
                        Vector3 cornerIn = corner - baseDirection * cornerRadius;
                        Vector3 cornerOut = corner + forward * cornerRadius;
                        for (int i = 1; i <= bends; i++)
                            Add(ref index, Vector3.Lerp(departure, cornerIn, (float)i / bends));
                        const float quarterCircleHandle = 0.55228475f;
                        for (int i = 1; i <= bends; i++)
                            Add(ref index, Bezier(cornerIn, cornerIn + baseDirection * cornerRadius * quarterCircleHandle,
                                cornerOut - forward * cornerRadius * quarterCircleHandle, cornerOut, (float)i / bends));
                        for (int i = 1; i <= bends; i++)
                            Add(ref index, Vector3.Lerp(cornerOut, loopStart, (float)i / bends));
                    }
                }
            }
            else
                for (int i = 1; i <= bends; i++)
                    Add(ref index, Bezier(departure, departure + tangent * lead, loopStart - forward * lead, loopStart, (float)i / bends));
            for (int i = 1; i <= bends; i++)
            {
                float a = Mathf.PI * i / bends;
                Add(ref index, guide + bend * (side * Mathf.Cos(a) + forward * Mathf.Sin(a)));
            }
            for (int i = 1; i <= bends; i++)
                Add(ref index, Bezier(loopEnd, loopEnd - forward * lead, fixedFeedPosition + forward * lead, fixedFeedPosition, (float)i / bends));

            wire.useWorldSpace = true;
            wire.loop = false;
            wire.positionCount = count;
            wire.SetPositions(positions);
            if (chainMesh == null)
            {
                chainMesh = wire.GetComponent<EnergyChainMesh>();
                if (chainMesh == null) chainMesh = wire.gameObject.AddComponent<EnergyChainMesh>();
            }
            chainMesh.Rebuild(positions, stationaryFrame, axis, chainWidth, chainHeight,
                chainLinkLength, chainLinkGap, chainMaterial != null ? chainMaterial : wire.sharedMaterial);
            wire.enabled = false;
        }

        private void Add(ref int index, Vector3 point) => positions[index++] = stationaryFrame.TransformPoint(point);

        // Common tangent between the turret coil and lower guide. Matching or
        // opposing winding chooses an external or internal tangent respectively.
        private static bool TryDepartureRadial(Vector3 coilCenter, float coilRadius, Vector3 guideCenter,
            float guideRadius, Vector3 axis, float coilWinding, float guideWinding, out Vector3 radial)
        {
            radial = Vector3.zero;
            Vector3 between = Vector3.ProjectOnPlane(guideCenter - coilCenter, axis);
            float distance = between.magnitude;
            float sameSide = coilWinding * guideWinding;
            float offset = coilRadius - sameSide * guideRadius;
            if (distance < 0.00001f || Mathf.Abs(offset) >= distance) return false;
            float angle = Mathf.Acos(offset / distance) * Mathf.Rad2Deg;
            Vector3 a = Quaternion.AngleAxis(angle, axis) * (between / distance);
            Vector3 b = Quaternion.AngleAxis(-angle, axis) * (between / distance);
            Vector3 run = between + a * (sameSide * guideRadius - coilRadius);
            radial = Vector3.Dot(coilWinding * Vector3.Cross(axis, a), run) > 0f ? a : b;
            return true;
        }

        // Tangents keep both straight runs outside the selected circular guide, even
        // when its center is moved away from the automatically calculated corner.
        private static bool TryGuideArc(Vector3 from, Vector3 to, Vector3 guideCenter, Vector3 axis,
            float guideRadius, float winding, out Vector3 entry, out Vector3 exit, out float sweep)
        {
            entry = exit = guideCenter;
            sweep = 0f;
            Vector3 fromRadial = Vector3.ProjectOnPlane(from - guideCenter, axis);
            Vector3 toRadial = Vector3.ProjectOnPlane(to - guideCenter, axis);
            if (fromRadial.magnitude <= guideRadius || toRadial.magnitude <= guideRadius) return false;
            Vector3 a = GuideTangent(fromRadial, axis, guideRadius, winding, true);
            Vector3 b = GuideTangent(toRadial, axis, guideRadius, winding, false);
            entry += a;
            exit += b;
            sweep = Vector3.SignedAngle(a, b, axis);
            if (winding > 0f && sweep < 0f) sweep += 360f;
            if (winding < 0f && sweep > 0f) sweep -= 360f;
            return true;
        }

        private static Vector3 GuideTangent(Vector3 point, Vector3 axis, float guideRadius, float winding, bool incoming)
        {
            float angle = Mathf.Acos(Mathf.Clamp01(guideRadius / point.magnitude)) * Mathf.Rad2Deg;
            Vector3 a = Quaternion.AngleAxis(angle, axis) * point.normalized * guideRadius;
            Vector3 b = Quaternion.AngleAxis(-angle, axis) * point.normalized * guideRadius;
            Vector3 run = incoming ? a - point : point - a;
            return Vector3.Dot(winding * Vector3.Cross(axis, a), run) >= 0f ? a : b;
        }

        // Arc length of the planar Archimedean spiral. Integrate in bounded steps to also handle zero pitch.
        private float ReverseRadius(float degrees, float normalWinding)
        {
            float reverseDegrees = Mathf.Max(0f, -degrees * normalWinding);
            float opening = Mathf.SmoothStep(0f, 1f, reverseDegrees / Mathf.Max(1f, reverseTransitionDegrees));
            // Once formed, the return bend leaves room for two widths of chain.
            return Mathf.Max(reverseBendRadius, chainWidth * 0.5f + layerSpacing * 0.5f) * opening;
        }

        private float WrappedLength(float degrees, float normalWinding, float reverseRadiusOverride = -1f)
        {
            float theta = Mathf.Min(Mathf.Abs(degrees), Mathf.Clamp(maximumVisualTurns, 1f, 64f) * 360f) * Mathf.Deg2Rad;
            float pitch = Mathf.Max(0f, layerSpacing) / (2f * Mathf.PI);
            const int steps = 64;
            float reverseRadius = reverseRadiusOverride >= 0f ? reverseRadiusOverride : ReverseRadius(degrees, normalWinding);
            float length = Mathf.PI * reverseRadius;
            for (int i = 0; i < steps; i++)
            {
                float r = Mathf.Max(0.001f, radius) + 2f * reverseRadius + pitch * theta * (i + 0.5f) / steps;
                length += Mathf.Sqrt(r * r + pitch * pitch) * theta / steps;
            }
            return length;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float s = 1f - t;
            return s * s * s * a + 3f * s * s * t * b + 3f * s * t * t * c + t * t * t * d;
        }
    }
}
