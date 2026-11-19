using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    public sealed class GuessComplexCollider : AuthoringTool
    {
        public override ToolInfo Info => new ToolInfo("Collider", "Guess Complex Collider", 22,
            "Select a mesh, or a parent containing meshes, to fit compound box colliders.");

        [MenuItem("GameObject/Guess Complex Collider", false, 55)]
        static void Menu() => Run<GuessComplexCollider>();

        [MenuItem("GameObject/Guess Complex Collider", true)]
        static bool Validate() => CanRun<GuessComplexCollider>();

        public override bool IsAvailable() => Has<MeshFilter>(TargetScope.Children, target => target.sharedMesh != null);

        protected internal override void Execute(ToolRun run)
        {
            MeshFilter[] targets = SelectionSet.Targets<MeshFilter>(TargetScope.Children)
                .Where(target => target != null && target.sharedMesh != null).ToArray();
            if (targets.Length == 0) { run.Fail(Info.EmptyMessage); return; }
            if (!ComplexColliderCountWindow.Ask(targets.Length, out int count)) return;
            var fits = new CompoundColliderFit.Box[targets.Length][];
            try
            {
                // Finish every fit before changing the hierarchy, so cancelling leaves no partial output.
                for (int i = 0; i < targets.Length; i++)
                {
                    MeshFilter target = targets[i];
                    int meshIndex = i;
                    fits[i] = CompoundColliderFit.Fit(target.sharedMesh, target.transform.localToWorldMatrix, count,
                        progress => EditorUtility.DisplayCancelableProgressBar(Info.Title,
                            $"Fitting {count} colliders to {target.name} ({meshIndex + 1}/{targets.Length})",
                            (meshIndex + progress) / targets.Length));
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception)
            {
                run.Fail($"Could not fit the selected meshes: {exception.Message}");
                return;
            }
            finally { EditorUtility.ClearProgressBar(); }

            run.Considered = targets.Length;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null) continue;
                CreateColliders(targets[i], fits[i], run.Name);
                run.Applied++;
            }
            run.Report($"created {run.Applied * count} box colliders ({count} per mesh) for {run.Applied} mesh(es).");
        }

        internal static GameObject CreateColliders(MeshFilter target, CompoundColliderFit.Box[] boxes, string undoName)
        {
            GameObject source = target.gameObject;
            HierarchyOps.Unpack(source);
            // Colliders must remain descendants of a Rigidbody carried by the mesh object itself.
            Transform container = source.GetComponent<Rigidbody>() != null ? source.transform : source.transform.parent;
            Transform parent = ModelLayout.ColliderParent(container, undoName);
            var group = new GameObject(HierarchyOps.UniqueChildName(parent, $"collider-complex-{source.name}"));
            Undo.RegisterCreatedObjectUndo(group, undoName);
            Undo.SetTransformParent(group.transform, parent, undoName);
            Undo.RecordObject(group, undoName);
            group.layer = source.layer;
            group.isStatic = source.isStatic;
            foreach (CompoundColliderFit.Box box in boxes)
            {
                var holder = new GameObject(HierarchyOps.UniqueChildName(group.transform, "collider-box"));
                Undo.RegisterCreatedObjectUndo(holder, undoName);
                Undo.SetTransformParent(holder.transform, group.transform, undoName);
                Undo.RecordObject(holder, undoName);
                holder.layer = source.layer;
                holder.isStatic = source.isStatic;
                Undo.RecordObject(holder.transform, undoName);
                holder.transform.SetPositionAndRotation(box.Center, box.Rotation);
                HierarchyOps.SetWorldScale(holder.transform, Vector3.one);
                var collider = Undo.AddComponent<BoxCollider>(holder);
                Undo.RecordObject(collider, undoName);
                collider.size = box.Size;
            }
            return group;
        }
    }

    sealed class ComplexColliderCountWindow : EditorWindow
    {
        const string Preference = "MoSim.GuessComplexCollider.Count";
        int _count, _meshes;
        bool _accepted;

        internal static bool Ask(int meshes, out int count)
        {
            var window = CreateInstance<ComplexColliderCountWindow>();
            window.titleContent = new GUIContent("Guess Complex Collider");
            window._meshes = meshes;
            window._count = Mathf.Clamp(EditorPrefs.GetInt(Preference, 4), 1, CompoundColliderFit.MaxColliders);
            window.minSize = window.maxSize = new Vector2(380, 210);
            window.ShowModalUtility();
            count = window._count;
            return window._accepted;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Fit {_meshes} selected mesh(es)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Colliders per mesh");
            GUI.SetNextControlName("ColliderCount");
            _count = EditorGUILayout.IntField(_count);
            EditorGUILayout.HelpBox("Fits a group of box colliders to each mesh. Higher counts can follow bends and openings more closely.", MessageType.Info);
            bool valid = _count >= 1 && _count <= CompoundColliderFit.MaxColliders;
            if (!valid) EditorGUILayout.LabelField($"Enter a number from 1 to {CompoundColliderFit.MaxColliders}.");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Cancel")) Close();
                using (new EditorGUI.DisabledScope(!valid))
                    if (GUILayout.Button($"Create {(_count * (long)_meshes)} colliders")) Accept();
            }
            if (Event.current.type == EventType.KeyDown)
            {
                if (Event.current.keyCode == KeyCode.Escape) { Event.current.Use(); Close(); }
                else if (valid && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                { Event.current.Use(); Accept(); }
            }
        }

        void Accept()
        {
            _accepted = true;
            EditorPrefs.SetInt(Preference, _count);
            Close();
        }
    }
}
