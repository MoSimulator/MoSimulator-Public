using UnityEditor.ShortcutManagement;
using RobotFramework.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Editor
{
    /// <summary>Read-only Scene view measurement of the runtime solid core shapes.</summary>
    [InitializeOnLoad]
    public static partial class RollerMeasureTool
    {
        const string MenuPath = "GameObject/Measure/Roller or Collider Gap";
        public static bool Active { get; private set; }
        static Component first, second;
        static int control;
        static Vector2 pressPosition;
        static readonly Color Accent = new Color(1f, 0.82f, 0.18f);
        static readonly Color PanelColor = new Color(0.055f, 0.065f, 0.08f, 1f);
        static GUIStyle textStyle, titleStyle, distanceStyle, badgeStyle;
        static Component measuredA, measuredB;
        static int measuredHash;
        static float measuredGap;
        static Vector3 measuredPointA, measuredPointB;
        static string measurementError;

        static RollerMeasureTool()
        {
            SceneContextMenu.Add("Measure/Roller or Collider Gap", Toggle,
                isChecked: () => Active, priority: 50);
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += _ => Stop();
            Undo.undoRedoPerformed += InvalidateMeasurement;
            EditorApplication.hierarchyChanged += InvalidateMeasurement;
            EditorApplication.projectChanged += InvalidateMeasurement;
        }

#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Roller or Collider Gap", KeyCode.D, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Roller or Collider Gap", KeyCode.D, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        static void RunShortcut() { Toggle(); }

        [MenuItem(MenuPath, false, 50)]
        static void Toggle()
        {
            if (Active) Stop();
            else
            {
                Active = true;
                first = second = null;
                SceneView.RepaintAll();
            }
        }

        [MenuItem(MenuPath, true)]
        static bool Validate() { Menu.SetChecked(MenuPath, Active); return true; }

        internal static void Stop()
        {
            if (control != 0 && GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            control = 0;
            Active = false;
            first = second = null;
            SceneView.RepaintAll();
        }

        static void OnSceneGUI(SceneView view)
        {
            if (!Active) return;
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Stop();
                e.Use();
                return;
            }

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout && !e.alt) HandleUtility.AddDefaultControl(id);
            if (!e.alt && e.button == 0 && e.type == EventType.MouseDown && HandleUtility.nearestControl == id)
            {
                control = id;
                pressPosition = e.mousePosition;
                GUIUtility.hotControl = id;
                e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0 && control != 0 && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                control = 0;
                if (!e.alt && (e.mousePosition - pressPosition).sqrMagnitude <= 16f)
                {
                    Component picked = Pick(HandleUtility.GUIPointToWorldRay(e.mousePosition));
                    if (picked != null)
                    {
                        if (first == null || second != null) { first = picked; second = null; }
                        else if (picked != first) second = picked;
                    }
                }
                e.Use();
                SceneView.RepaintAll();
            }

            EnsureStyles();
            string message = first == null ? "Click a roller or collider." :
                second == null ? "Click a different roller or collider." : "Click an object to measure another pair.";
            string measurement = "— in";
            Vector3 midpoint = default;
            if (first != null && second != null)
            {
                float gap = MeasuredGap(first, second, out Vector3 a, out Vector3 b);
                measurement = measurementError == null ? $"{gap / 0.0254f:F3} in" : "Unavailable";
                if (measurementError != null) message = measurementError;
                midpoint = (a + b) * 0.5f;
                if (measurementError == null && gap <= 0.000001f) message = "Surfaces touching or overlapping.";
                if (measurementError == null) using (new Handles.DrawingScope(Accent))
                {
                    var oldDepth = Handles.zTest;
                    Handles.zTest = CompareFunction.Always;
                    Handles.color = Color.black;
                    Handles.DrawAAPolyLine(9f, a, b);
                    Handles.color = Accent;
                    Handles.DrawAAPolyLine(4f, a, b);
                    if (e.type == EventType.Repaint)
                    {
                        DrawEndpoint(a);
                        DrawEndpoint(b);
                    }
                    Handles.zTest = oldDepth;
                }
            }
            Handles.BeginGUI();
            if (first != null) DrawBadge(view, first.transform.position, "A", textStyle);
            if (second != null)
            {
                DrawBadge(view, second.transform.position, "B", textStyle);
                if (measurementError == null) DrawBadge(view, midpoint, measurement, badgeStyle);
            }
            var panel = new Rect(12, 12, Mathf.Min(420, view.position.width - 24), 176);
            DrawPanel(panel);
            float x = panel.x + 14, width = panel.width - 28;
            GUI.Label(new Rect(x, panel.y + 10, width, 22), "ROLLER / COLLIDER GAP", titleStyle);
            GUI.Label(new Rect(x, panel.y + 35, width, 38), measurement, distanceStyle);
            GUI.Label(new Rect(x, panel.y + 80, width, 20),
                new GUIContent(first == null ? "A: waiting for first object" : $"A: {TargetName(first)}", first == null ? "" : TargetName(first)), textStyle);
            GUI.Label(new Rect(x, panel.y + 102, width, 20),
                new GUIContent(second == null ? "B: waiting for second object" : $"B: {TargetName(second)}", second == null ? "" : TargetName(second)), textStyle);
            GUI.Label(new Rect(x, panel.y + 128, width, 20), message, textStyle);
            GUI.Label(new Rect(x, panel.y + 150, width, 20), "Esc to exit", textStyle);
            Handles.EndGUI();
        }

        static void EnsureStyles()
        {
            if (textStyle != null) return;
            textStyle = new GUIStyle(EditorStyles.label) { fontSize = 13, clipping = TextClipping.Clip };
            textStyle.normal.textColor = Color.white;
            titleStyle = new GUIStyle(textStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = Accent;
            distanceStyle = new GUIStyle(textStyle) { fontSize = 28, fontStyle = FontStyle.Bold };
            distanceStyle.normal.textColor = Accent;
            badgeStyle = new GUIStyle(textStyle) { fontSize = 18, fontStyle = FontStyle.Bold };
            badgeStyle.normal.textColor = Accent;
        }

        static void DrawEndpoint(Vector3 endpoint)
        {
            float size = HandleUtility.GetHandleSize(endpoint) * 0.07f;
            Handles.color = Color.black;
            Handles.DotHandleCap(0, endpoint, Quaternion.identity, size * 1.5f, EventType.Repaint);
            Handles.color = Accent;
            Handles.DotHandleCap(0, endpoint, Quaternion.identity, size, EventType.Repaint);
        }

        static void DrawPanel(Rect rect)
        {
            EditorGUI.DrawRect(rect, Accent);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), PanelColor);
        }

        static void DrawBadge(SceneView view, Vector3 world, string text, GUIStyle style)
        {
            if (view.camera == null || view.camera.WorldToViewportPoint(world).z <= 0) return;
            Vector2 position = HandleUtility.WorldToGUIPoint(world);
            Vector2 size = style.CalcSize(new GUIContent(text)) + new Vector2(20, 12);
            var rect = new Rect(position.x - size.x * 0.5f, position.y - size.y - 12, size.x, size.y);
            if (rect.xMax < 0 || rect.xMin > view.position.width || rect.yMax < 0 || rect.yMin > view.position.height) return;
            rect.x = Mathf.Clamp(rect.x, 4, Mathf.Max(4, view.position.width - rect.width - 4));
            rect.y = Mathf.Clamp(rect.y, 4, Mathf.Max(4, view.position.height - rect.height - 4));
            DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, rect.width - 20, rect.height - 12), text, style);
        }

        static string TargetName(Component target) => $"{target.name} ({(target is InteractionRoller ? "core" : target.GetType().Name)})";

        static Component Pick(Ray ray)
        {
            Component best = null;
            float distance = float.PositiveInfinity;
            foreach (var roller in ColliderSelectMode.Rollers())
            {
                if (roller == null || !roller.isActiveAndEnabled ||
                    SceneVisibilityManager.instance.IsHidden(roller.gameObject)) continue;
                if (!ColliderSelectMode.RollerHit(ray, roller, out float hit) || hit >= distance) continue;
                best = roller;
                distance = hit;
            }
            foreach (var collider in ColliderSelectMode.Colliders())
            {
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    SceneVisibilityManager.instance.IsHidden(collider.gameObject)) continue;
                // A roller's trigger must never replace the core measurement when playing.
                if (collider.GetComponentInParent<InteractionRoller>() != null) continue;
                if (!Supported(collider)) continue;
                if (!collider.Raycast(ray, out var hit, Mathf.Infinity) || hit.distance >= distance) continue;
                best = collider;
                distance = hit.distance;
            }
            return best;
        }

        // A capsule is its finite spine swept by a sphere. Capsule/capsule distances
        // therefore reduce to segment distance minus the two radii, including end caps.
        internal static float CoreGap(InteractionRoller a, InteractionRoller b, out Vector3 pa, out Vector3 pb)
        {
            return ShapeGap(new Core(a), new Core(b), out pa, out pb);
        }

        static float ShapeGap(Core sa, Core sb, out Vector3 pa, out Vector3 pb)
        {
            if (!sa.Box && !sb.Box)
                Segments(sa.Start, sa.End, sb.Start, sb.End, out pa, out pb);
            else if (!sa.Box)
                SegmentBox(sa.Start, sa.End, sb, out pa, out pb);
            else if (!sb.Box)
                SegmentBox(sb.Start, sb.End, sa, out pb, out pa);
            else
            {
                pa = sa.Center; pb = sb.Center;
                float best = float.PositiveInfinity;
                for (int side = 0; side < 2; side++)
                {
                    Core source = side == 0 ? sa : sb, target = side == 0 ? sb : sa;
                    for (int edge = 0; edge < source.EdgeCount; edge++)
                    {
                        source.Edge(edge, out var start, out var end);
                        SegmentBox(start, end, target, out var p, out var q);
                        float d = (p - q).sqrMagnitude;
                        if (d >= best) continue;
                        best = d;
                        pa = side == 0 ? p : q; pb = side == 0 ? q : p;
                    }
                }
            }
            Vector3 delta = pb - pa;
            float distance = delta.magnitude;
            float gap = Mathf.Max(0, distance - sa.Radius - sb.Radius);
            if (gap == 0) { pa = pb = distance > 0 ? pa + delta * Mathf.Clamp01(sa.Radius / distance) : pa; }
            else { pa += delta / distance * sa.Radius; pb -= delta / distance * sb.Radius; }
            return gap;
        }

        readonly struct Core
        {
            public readonly bool Box;
            readonly bool triangle;
            readonly Vector3 ta, tb, tc;
            public readonly Vector3 Center, Half, Start, End;
            public readonly Quaternion Rotation;
            public readonly float Radius;
            public Core(InteractionRoller roller)
            {
                triangle = false;
                ta = tb = tc = default;
                Box = roller.EditorUsesBeltMode;
                Center = roller.transform.position;
                Rotation = roller.transform.rotation;
                Vector3 scale = roller.transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Half = Vector3.Scale(new Vector3(roller.EditorLength * 0.5f,
                    roller.EditorCoreRadius, Mathf.Abs(roller.EditorCoreBeltLength) * 0.5f), scale);
                Radius = Box ? 0 : roller.EditorCoreRadius * Mathf.Max(scale.y, scale.z);
                float spine = Mathf.Max(0, roller.EditorLength * scale.x * 0.5f - Radius);
                Start = Center - Rotation * Vector3.right * spine;
                End = Center + Rotation * Vector3.right * spine;
            }
            public Core(Vector3 a, Vector3 b, Vector3 c)
            {
                Box = triangle = true;
                ta = a; tb = b; tc = c;
                Center = (a + b + c) / 3;
                Half = Start = End = default;
                Rotation = Quaternion.identity;
                Radius = 0;
            }
            public Core(Collider collider)
            {
                this = default;
                Transform transform = collider.transform;
                Vector3 scale = transform.lossyScale;
                scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                Rotation = transform.rotation;
                if (collider is BoxCollider box)
                {
                    Box = true;
                    Center = transform.TransformPoint(box.center);
                    Half = Vector3.Scale(box.size, scale) * .5f;
                }
                else if (collider is SphereCollider sphere)
                {
                    Center = transform.TransformPoint(sphere.center);
                    Radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                    Start = End = Center;
                }
                else if (collider is CapsuleCollider capsule)
                {
                    Center = transform.TransformPoint(capsule.center);
                    int axis = capsule.direction;
                    Radius = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    float spine = Mathf.Max(0, capsule.height * scale[axis] * .5f - Radius);
                    Vector3 direction = Vector3.zero; direction[axis] = 1;
                    Start = Center - Rotation * direction * spine;
                    End = Center + Rotation * direction * spine;
                }
            }
            public int EdgeCount => triangle ? 3 : 12;
            public void Edge(int index, out Vector3 a, out Vector3 b)
            {
                if (triangle)
                {
                    a = index == 0 ? ta : index == 1 ? tb : tc;
                    b = index == 0 ? tb : index == 1 ? tc : ta;
                    return;
                }
                int axis = index / 4, signs = index % 4;
                Vector3 v = Half;
                v[(axis + 1) % 3] *= (signs & 1) == 0 ? -1 : 1;
                v[(axis + 2) % 3] *= (signs & 2) == 0 ? -1 : 1;
                Vector3 w = v; w[axis] = -w[axis];
                a = World(v); b = World(w);
            }
            public Vector3 World(Vector3 p) => Center + Rotation * p;
            public Vector3 Closest(Vector3 p)
            {
                if (triangle) return TriangleClosest(p, ta, tb, tc);
                p = Quaternion.Inverse(Rotation) * (p - Center);
                for (int i = 0; i < 3; i++) p[i] = Mathf.Clamp(p[i], -Half[i], Half[i]);
                return World(p);
            }
        }

        static void SegmentBox(Vector3 start, Vector3 end, Core box, out Vector3 p, out Vector3 q)
        {
            // Squared distance to a convex box is convex along a segment. Golden-section
            // search also handles segments entering the box (distance zero).
            float lo = 0, hi = 1;
            for (int i = 0; i < 64; i++)
            {
                float l = Mathf.Lerp(lo, hi, 0.381966f), r = Mathf.Lerp(lo, hi, 0.618034f);
                Vector3 a = Vector3.Lerp(start, end, l), b = Vector3.Lerp(start, end, r);
                if ((a - box.Closest(a)).sqrMagnitude <= (b - box.Closest(b)).sqrMagnitude) hi = r;
                else lo = l;
            }
            p = Vector3.Lerp(start, end, (lo + hi) * 0.5f); q = box.Closest(p);
        }

        internal static void Segments(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 p, out Vector3 q)
        {
            Vector3 u = b - a, v = d - c, w = a - c;
            float uu = Vector3.Dot(u, u), vv = Vector3.Dot(v, v), uv = Vector3.Dot(u, v);
            float uw = Vector3.Dot(u, w), vw = Vector3.Dot(v, w), s, t;
            const float epsilon = 1e-12f;
            if (uu <= epsilon) { s = 0; t = vv <= epsilon ? 0 : Mathf.Clamp01(vw / vv); }
            else if (vv <= epsilon) { t = 0; s = Mathf.Clamp01(-uw / uu); }
            else
            {
                float denominator = uu * vv - uv * uv;
                s = denominator > epsilon * uu * vv ? Mathf.Clamp01((uv * vw - uw * vv) / denominator) : 0;
                t = (uv * s + vw) / vv;
                if (t < 0) { t = 0; s = Mathf.Clamp01(-uw / uu); }
                else if (t > 1) { t = 1; s = Mathf.Clamp01((uv - uw) / uu); }
            }
            p = a + u * s; q = c + v * t;
        }
    }
}
