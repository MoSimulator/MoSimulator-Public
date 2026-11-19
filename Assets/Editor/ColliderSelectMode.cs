using System.Collections.Generic;
using System.Linq;
using RobotFramework.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Editor
{
    /// <summary>
    /// A Scene view mode that picks objects by their colliders instead of their rendered meshes, for
    /// working on a model's collision shell without fighting the visual geometry sitting on top of it.
    /// </summary>
    [InitializeOnLoad]
    public static class ColliderSelectMode
    {
        const string MenuPath = "GameObject/Collider Select Mode";
        const string PrefKey = "MoSim.ColliderSelectMode";

        /// <summary>Squared pixel distance a click may travel before it counts as a drag, not a pick.</summary>
        const float DragToleranceSqr = 16f;

        /// <summary>Ceiling on outlines drawn per repaint, so a dense scene stays responsive.</summary>
        const int MaxOutlines = 600;

        /// <summary>
        /// Hits considered per pick. A ray has to be able to pass through hidden colliders to reach a
        /// visible one behind them, so the whole line of hits is fetched rather than just the nearest.
        /// </summary>
        static readonly RaycastHit[] HitBuffer = new RaycastHit[64];

        static readonly Color OutlineColor = new Color(0.35f, 1f, 0.6f, 0.85f);
        static readonly Color SelectedOutlineColor = new Color(1f, 0.85f, 0.3f, 1f);

        static Vector2 _pressPosition;
        static bool _pressed;

        static Collider[] _colliders = new Collider[0];
        static InteractionRoller[] _rollers = new InteractionRoller[0];
        static bool _collidersStale = true;

        static ColliderSelectMode()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.hierarchyChanged += () => _collidersStale = true;
            PrefabStage.prefabStageOpened += _ => _collidersStale = true;
            PrefabStage.prefabStageClosing += _ => _collidersStale = true;

            SceneContextMenu.Add("Select/Collider Select Mode", Toggle,
                priority: 42, isChecked: () => Enabled);
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            private set => EditorPrefs.SetBool(PrefKey, value);
        }

        // Registered with the shortcut manager rather than as a "%#c"-style suffix on the menu path,
        // so it shows up in Edit > Shortcuts, can be rebound, and reports a conflict instead of one
        // binding silently winning. Deliberately not scoped to a context: the mode is a persistent
        // preference, and being unable to flip it from the Hierarchy would be surprising.
        //
        // The default binding differs by platform because Alt is not a plain modifier on macOS: it is
        // Option, the key the system uses to compose characters, so Option+Shift+C is eaten as text
        // input and never reaches the shortcut manager. Control is free there - Unity's own defaults
        // are all Command - while on Windows Ctrl+Shift+C is already the Console window.
#if UNITY_EDITOR_OSX
        [Shortcut("MoSim/Collider Select Mode", KeyCode.C, ShortcutModifiers.Control | ShortcutModifiers.Shift)]
#else
        [Shortcut("MoSim/Collider Select Mode", KeyCode.C, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
#endif
        [MenuItem(MenuPath, false, 44)]
        static void Toggle()
        {
            SetEnabled(!Enabled);
        }

        internal static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            Menu.SetChecked(MenuPath, Enabled);
            _collidersStale = true;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>
        /// Picking runs on mouse up rather than mouse down, so a drag on a transform handle still
        /// belongs to the handle: by the time the button is released a handle drag has claimed
        /// hotControl, and the click is left alone.
        /// </summary>
        static void OnSceneGui(SceneView sceneView)
        {
            if (!Enabled || RollerMeasureTool.Active) return;

            Event e = Event.current;

            if (e.type == EventType.Repaint)
            {
                DrawOutlines(sceneView.camera);
                return;
            }

            // Alt is the Scene view's camera navigation and shift is left to Unity's own picking, so
            // both are none of this mode's business. Ctrl (Cmd on a Mac) is, and is handled below.
            if (e.alt || e.shift) return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                _pressPosition = e.mousePosition;
                _pressed = true;
                return;
            }

            if (e.type != EventType.MouseUp || e.button != 0 || !_pressed) return;

            _pressed = false;
            if ((e.mousePosition - _pressPosition).sqrMagnitude > DragToleranceSqr) return;
            if (GUIUtility.hotControl != 0) return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            GameObject picked = Pick(ray);
            if (picked == null) return;

            Select(picked, additive: e.control || e.command);
            e.Use();
        }

        /// <summary>
        /// Replaces the selection, or - held with ctrl/cmd - toggles this one object in and out of it,
        /// matching what the modifier does in the Hierarchy and in Unity's own Scene view picking.
        /// </summary>
        /// <remarks>
        /// The whole selection is rewritten through <see cref="Selection.objects"/>. Assigning
        /// activeGameObject instead is not "also select this one" - its setter REPLACES the selection
        /// with that single object, so using it to mark the newcomer active would undo the add it was
        /// meant to complete. The newcomer goes in at index 0 instead, which is the entry Unity makes
        /// active, so the Inspector follows the click.
        ///
        /// Pinging is left to the cases that selected something: a ping on removal draws the eye to
        /// the object just dropped.
        /// </remarks>
        static void Select(GameObject picked, bool additive)
        {
            if (!additive)
            {
                Selection.activeGameObject = picked;
                EditorGUIUtility.PingObject(picked);
                return;
            }

            var objects = new List<Object>(Selection.objects);
            int index = objects.IndexOf(picked);

            if (index >= 0)
            {
                objects.RemoveAt(index);
                Selection.objects = objects.ToArray();
                return;
            }

            objects.Insert(0, picked);
            Selection.objects = objects.ToArray();
            EditorGUIUtility.PingObject(picked);
        }

        /// <summary>
        /// The colliders belonging to whatever the Scene view is editing: the prefab's own contents in
        /// isolation mode, otherwise the loaded scenes. A prefab stage lives in a preview scene of its
        /// own, and the scene behind it is hidden from view, so its colliders must not be drawn or picked.
        /// </summary>
        internal static Collider[] Colliders()
        {
            Refresh();
            return _colliders;
        }

        /// <summary>
        /// InteractionRollers, which are pickable here despite having no collider to pick.
        /// </summary>
        /// <remarks>
        /// A roller builds its trigger and core colliders in Start, so in the editor there is nothing
        /// on the object for a raycast to hit - which made the one component whose whole job is a
        /// collision volume the one thing this mode could not select. The volume is rebuilt from the
        /// roller's own dimensions instead; see RollerHit.
        ///
        /// Picking only. Drawing is the roller's own OnDrawGizmos, which already shows the shape.
        /// </remarks>
        internal static InteractionRoller[] Rollers()
        {
            Refresh();
            return _rollers;
        }

        static void Refresh()
        {
            if (!_collidersStale) return;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                _colliders = stage.prefabContentsRoot.GetComponentsInChildren<Collider>(true);
                _rollers = stage.prefabContentsRoot.GetComponentsInChildren<InteractionRoller>(true);
            }
            else
            {
                _colliders = LoadedSceneComponents<Collider>();
                _rollers = LoadedSceneComponents<InteractionRoller>();
            }

            _collidersStale = false;
        }

        static T[] LoadedSceneComponents<T>() where T : Component
        {
            var found = new List<T>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                    found.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return found.ToArray();
        }

        /// <summary>
        /// Is this object one the Scene view is currently showing? Objects hidden through the Hierarchy's
        /// eye toggle are neither outlined nor pickable, so the mode works on the same subset of the model
        /// the eye leaves on screen - which is how anything gets out from under a collision shell that
        /// wraps it.
        /// </summary>
        static bool Visible(GameObject candidate)
        {
            return candidate.activeInHierarchy && !SceneVisibilityManager.instance.IsHidden(candidate);
        }

        /// <summary>
        /// Finds the nearest collider along the ray. In a prefab stage the query has to run against that
        /// stage's own physics scene: the default one still holds the scene behind it, which would let a
        /// click land on something that is not even visible.
        /// </summary>
        internal static GameObject Pick(Ray ray)
        {
            GameObject picked = PickCollider(ray, out float distance);

            // Rollers compete on distance rather than being a fallback, so one sitting in front of a
            // collider wins the click the way any other nearer shape would.
            foreach (InteractionRoller roller in Rollers())
            {
                if (roller == null || !roller.isActiveAndEnabled) continue;
                if (!Visible(roller.gameObject)) continue;
                if (!RollerHit(ray, roller, out float rollerDistance)) continue;
                if (picked != null && rollerDistance >= distance) continue;

                picked = roller.gameObject;
                distance = rollerDistance;
            }

            return picked;
        }

        static GameObject PickCollider(Ray ray, out float distance)
        {
            distance = float.MaxValue;

            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();

            int count = stage == null
                ? Physics.RaycastNonAlloc(ray, HitBuffer, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore)
                : stage.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, HitBuffer, Mathf.Infinity, ~0,
                    QueryTriggerInteraction.Ignore);

            // A non-alloc raycast returns its hits unsorted, and the hidden ones are dropped rather than
            // being allowed to block the ray, so the nearest survivor is found by scanning them all.
            GameObject nearestVisible = null;
            for (int i = 0; i < Mathf.Min(count, HitBuffer.Length); i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.distance >= distance) continue;
                if (!Visible(hit.collider.gameObject)) continue;

                distance = hit.distance;
                nearestVisible = hit.collider.gameObject;
            }

            if (nearestVisible != null) return nearestVisible;
            if (stage == null) return null;

            // Preview scenes do not always keep their physics bodies in sync with the editor, so the
            // colliders are tested one by one as a fallback.
            var nearest = Colliders()
                .Where(c => c != null && c.enabled && !c.isTrigger && Visible(c.gameObject))
                .Select(c => c.Raycast(ray, out RaycastHit direct, Mathf.Infinity)
                    ? (Collider: c, Distance: direct.distance)
                    : (Collider: null, Distance: float.MaxValue))
                .Where(candidate => candidate.Collider != null)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();

            if (nearest.Collider == null) return null;

            distance = nearest.Distance;
            return nearest.Collider.gameObject;
        }

        /// <summary>
        /// Does the ray meet the volume this roller will sweep, and how far along?
        /// </summary>
        /// <remarks>
        /// Worked in the roller's own frame, built from its position and rotation ALONE. The sizes
        /// below are unscaled world metres - that is the volume the roller's overlap queries actually
        /// use - so folding lossyScale in would test a different shape than the one that interacts.
        /// Leaving scale out also makes the transform rigid, which is why the local hit distance can
        /// be handed straight back as a world distance.
        /// </remarks>
        internal static bool RollerHit(Ray ray, InteractionRoller roller, out float distance)
        {
            Transform transform = roller.transform;
            Quaternion inverse = Quaternion.Inverse(transform.rotation);

            Vector3 origin = inverse * (ray.origin - transform.position);
            Vector3 direction = inverse * ray.direction;

            if (roller.EditorUsesBeltMode)
            {
                // Matches _halfExtents: X the belt's width, Y its reach either side, Z its travel.
                var half = new Vector3(
                    roller.EditorLength * 0.5f,
                    roller.EditorRadius,
                    roller.EditorBeltLength * 0.5f);
                return BoxHit(origin, direction, half, out distance);
            }

            // The wheel's trigger is a capsule down local X, its spine shortened by the radius at
            // each end exactly as the overlap query's segment is.
            return CapsuleHit(origin, direction, roller.EditorRadius,
                Mathf.Max(0f, roller.EditorLength * 0.5f - roller.EditorRadius), out distance);
        }

        /// <summary>Slab test against a box centred on the origin. Distance is 0 from inside.</summary>
        static bool BoxHit(Vector3 origin, Vector3 direction, Vector3 halfExtents, out float distance)
        {
            distance = 0f;

            float near = 0f;
            float far = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < 1e-8f)
                {
                    // Parallel to this pair of faces: either always between them, or never.
                    if (Mathf.Abs(origin[axis]) > halfExtents[axis]) return false;
                    continue;
                }

                float inverse = 1f / direction[axis];
                float entry = (-halfExtents[axis] - origin[axis]) * inverse;
                float exit = (halfExtents[axis] - origin[axis]) * inverse;
                if (entry > exit) (entry, exit) = (exit, entry);

                near = Mathf.Max(near, entry);
                far = Mathf.Min(far, exit);
                if (near > far) return false;
            }

            distance = near;
            return far >= 0f;
        }

        /// <summary>
        /// Capsule down the local X axis: the side wall is the infinite cylinder clipped to the spine,
        /// the ends are spheres on the spine's endpoints, and the nearest of the three wins.
        /// </summary>
        static bool CapsuleHit(Vector3 origin, Vector3 direction, float radius, float halfSpine, out float distance)
        {
            float nearest = float.MaxValue;

            float a = direction.y * direction.y + direction.z * direction.z;
            if (a > 1e-12f)
            {
                float b = 2f * (origin.y * direction.y + origin.z * direction.z);
                float c = origin.y * origin.y + origin.z * origin.z - radius * radius;
                float discriminant = b * b - 4f * a * c;

                if (discriminant >= 0f)
                {
                    float root = Mathf.Sqrt(discriminant);

                    // Near root first, then the far one: if the near hit lands past an end cap the far
                    // one may still be on the wall, which is what looking in from off the end gives.
                    float entry = (-b - root) / (2f * a);
                    float exit = (-b + root) / (2f * a);

                    if (entry >= 0f && Mathf.Abs(origin.x + direction.x * entry) <= halfSpine) nearest = entry;
                    else if (exit >= 0f && Mathf.Abs(origin.x + direction.x * exit) <= halfSpine) nearest = exit;
                }
            }

            // Each cap is tested by moving the ray so that cap's centre sits on the origin.
            if (SphereHit(origin + new Vector3(halfSpine, 0f, 0f), direction, radius, out float start)) nearest = Mathf.Min(nearest, start);
            if (SphereHit(origin - new Vector3(halfSpine, 0f, 0f), direction, radius, out float end)) nearest = Mathf.Min(nearest, end);

            distance = nearest == float.MaxValue ? 0f : nearest;
            return nearest != float.MaxValue;
        }

        /// <summary>Sphere centred on the origin. Direction must be unit length, which a Ray's is.</summary>
        static bool SphereHit(Vector3 origin, Vector3 direction, float radius, out float distance)
        {
            distance = 0f;

            float b = 2f * Vector3.Dot(origin, direction);
            float c = origin.sqrMagnitude - radius * radius;
            float discriminant = b * b - 4f * c;
            if (discriminant < 0f) return false;

            float root = Mathf.Sqrt(discriminant);
            float near = (-b - root) * 0.5f;
            float far = (-b + root) * 0.5f;

            if (near >= 0f) { distance = near; return true; }
            if (far >= 0f) { distance = far; return true; } // origin is inside

            return false;
        }

        /// <summary>
        /// Outlines the colliders the mode can pick. Only enabled, non-trigger ones on objects the Scene view is
        /// showing are drawn, matching what a pick can land on, and only those in view, capped so a
        /// dense scene does not stall the view.
        ///
        /// InteractionRollers are pickable too but are deliberately NOT drawn here - they already
        /// draw themselves, and a second outline on top only fights the one they own.
        /// </summary>
        static void DrawOutlines(Camera camera)
        {
            if (camera == null) return;

            Collider[] colliders = Colliders();

            Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(camera);
            CompareFunction zTest = Handles.zTest;
            Matrix4x4 matrix = Handles.matrix;
            Color color = Handles.color;

            Handles.zTest = CompareFunction.LessEqual;

            int drawn = 0;
            foreach (Collider collider in colliders)
            {
                if (drawn >= MaxOutlines) break;
                if (collider == null || !collider.enabled || collider.isTrigger || !Visible(collider.gameObject)) continue;
                if (!GeometryUtility.TestPlanesAABB(frustum, collider.bounds)) continue;

                Handles.color = Selection.Contains(collider.gameObject) ? SelectedOutlineColor : OutlineColor;
                Draw(collider);
                drawn++;
            }

            Handles.color = color;
            Handles.matrix = matrix;
            Handles.zTest = zTest;
        }

        static void Draw(Collider collider)
        {
            Transform transform = collider.transform;

            switch (collider)
            {
                case BoxCollider box:
                    Handles.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Handles.DrawWireCube(box.center, box.size);
                    break;

                case SphereCollider sphere:
                    Handles.matrix = Matrix4x4.identity;
                    DrawWireSphere(transform.TransformPoint(sphere.center),
                        sphere.radius * MaxAbs(transform.lossyScale));
                    break;

                case CapsuleCollider capsule:
                    Handles.matrix = Matrix4x4.identity;
                    DrawWireCapsule(capsule);
                    break;

                default:
                    // Mesh and terrain colliders are outlined by their bounds: drawing the real surface
                    // would cost more than the mode is worth, and the box still shows what is pickable.
                    Handles.matrix = Matrix4x4.identity;
                    Handles.DrawWireCube(collider.bounds.center, collider.bounds.size);
                    break;
            }
        }

        static void DrawWireSphere(Vector3 center, float radius)
        {
            Handles.DrawWireDisc(center, Vector3.right, radius);
            Handles.DrawWireDisc(center, Vector3.up, radius);
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        /// <summary>
        /// Capsules scale the way the physics engine reads them: the height follows the axis the capsule
        /// runs along, the radius follows the larger of the other two.
        /// </summary>
        static void DrawWireCapsule(CapsuleCollider capsule)
        {
            Transform transform = capsule.transform;
            Vector3 scale = transform.lossyScale;

            int axis = Mathf.Clamp(capsule.direction, 0, 2);
            int a = (axis + 1) % 3;
            int b = (axis + 2) % 3;

            float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[a]), Mathf.Abs(scale[b]));
            float height = Mathf.Max(capsule.height * Mathf.Abs(scale[axis]), radius * 2f);

            Vector3 up = transform.rotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
            Vector3 forward = transform.rotation * (axis == 1 ? Vector3.forward : Vector3.up);
            Vector3 right = Vector3.Cross(up, forward).normalized;

            Vector3 center = transform.TransformPoint(capsule.center);
            Vector3 offset = up * (height * 0.5f - radius);
            Vector3 top = center + offset;
            Vector3 bottom = center - offset;

            Handles.DrawWireDisc(top, up, radius);
            Handles.DrawWireDisc(bottom, up, radius);

            Handles.DrawWireArc(top, right, forward, -180f, radius);
            Handles.DrawWireArc(top, forward, right, 180f, radius);
            Handles.DrawWireArc(bottom, right, forward, 180f, radius);
            Handles.DrawWireArc(bottom, forward, right, -180f, radius);

            foreach (Vector3 side in new[] { right, -right, forward, -forward })
                Handles.DrawLine(top + side * radius, bottom + side * radius);
        }

        static float MaxAbs(Vector3 value)
        {
            return Mathf.Max(Mathf.Abs(value.x), Mathf.Max(Mathf.Abs(value.y), Mathf.Abs(value.z)));
        }
    }
}
