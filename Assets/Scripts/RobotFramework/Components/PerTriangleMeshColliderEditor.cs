#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace RobotFramework.Components
{
    // Default Inspector plus the three actions. Multi-object aware so a whole selection of parts
    // can be generated in one press.
    [CustomEditor(typeof(PerTriangleMeshCollider))]
    [CanEditMultipleObjects]
    public class PerTriangleMeshColliderEditor : Editor
    {
        // --- Collider triangle budget ---------------------------------------------------------
        // Generated triangle counts above these turn yellow, then red, in the readout. Provisional
        // numbers - retune them here as parts are actually profiled; they only affect the colour,
        // never generation itself.
        private const int WarnTriangleCount = 300;
        private const int ErrorTriangleCount = 800;

        private static readonly Color WarnColor = new(1f, 0.8f, 0.2f);
        private static readonly Color ErrorColor = new(1f, 0.4f, 0.35f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            DrawPreviewRefresh();
            DrawTriangleReadout();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Colliders are generated here in the Editor and saved into the scene/prefab. " +
                "The script does nothing at runtime, so re-press Generate after changing a setting or the source mesh.",
                MessageType.Info);

            if (GUILayout.Button("Generate Colliders")) ForEachTarget(c => c.GenerateColliders());
            if (GUILayout.Button("Clear Colliders")) ForEachTarget(c => c.ClearColliders());

            EditorGUILayout.Space();

            // Destructive to the MeshFilter's mesh reference, so it sits apart from the two
            // buttons above rather than being the third one in a row.
            if (GUILayout.Button("Remesh From Colliders")) ForEachTarget(c => c.RemeshFromColliders());
        }

        // On a part where building the preview is slow, it stops following edits by itself - see
        // PreviewRefreshIsManual. Say so and offer the button, rather than leaving stale numbers
        // and stale gizmos looking like the settings did nothing.
        private void DrawPreviewRefresh()
        {
            bool waiting = false;
            ForEachTarget(c => waiting |= c.PreviewOutOfDate && c.PreviewRefreshIsManual);
            if (!waiting) return;

            EditorGUILayout.HelpBox(
                "The preview below is out of date. It doesn't rebuild automatically on this part because doing so takes long enough to make editing settings unresponsive.",
                MessageType.Info);

            if (GUILayout.Button("Refresh Preview")) ForEachTarget(c => c.RefreshPreview());
        }

        // Source mesh vs. what the generated colliders actually cost, summed over the selection.
        // Reflects what's currently generated, so it goes stale (and reads 0) until the next
        // Generate press.
        private void DrawTriangleReadout()
        {
            int partTriangles = 0;
            int previewTriangles = 0;
            int previewColliders = 0;
            int previewBoxes = 0;
            int colliderTriangles = 0;
            int colliderCount = 0;
            int boxCount = 0;
            ForEachTarget(c =>
            {
                partTriangles += c.SourceTriangleCount;
                previewTriangles += c.PreviewTriangleCount;
                previewColliders += c.PreviewColliderCount;
                previewBoxes += c.PreviewBoxCount;
                colliderTriangles += c.GeneratedTriangleCount;
                colliderCount += c.GeneratedColliderCount;
                boxCount += c.GeneratedBoxCount;
            });

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Part triangles", partTriangles.ToString("N0"));

                // Preview is what the current settings would build - the same shapes drawn as
                // gizmos. Generated is what is actually saved. They differ whenever a setting has
                // been touched since the last Generate, which is precisely when the difference is
                // worth seeing.
                EditorGUILayout.LabelField("Preview triangles", Describe(previewTriangles, previewColliders, previewBoxes, "nothing to build"), Budget(previewTriangles));
                EditorGUILayout.LabelField("Collider triangles", Describe(colliderTriangles, colliderCount, boxCount, "none generated"), Budget(colliderTriangles));
            }
        }

        // Triangles are counted from mesh colliders only - a box costs PhysX far less than its
        // twelve corners suggest, so boxes are reported alongside rather than added in.
        private static string Describe(int triangles, int colliders, int boxes, string empty)
        {
            return colliders > 0 ? $"{triangles:N0}   ({colliders:N0} colliders, {boxes:N0} boxes)" : empty;
        }

        private static GUIStyle Budget(int triangles)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label);
            if (triangles > ErrorTriangleCount) style.normal.textColor = ErrorColor;
            else if (triangles > WarnTriangleCount) style.normal.textColor = WarnColor;
            return style;
        }

        private void ForEachTarget(System.Action<PerTriangleMeshCollider> action)
        {
            foreach (Object target in targets)
            {
                action((PerTriangleMeshCollider)target);
            }
        }
    }
}
#endif
