using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// One searchable list of every authoring tool, what it is bound to, and whether the current
    /// selection gives it anything to do. The tools are spread over two menus and a keyboard binding
    /// or two; this is the place to go looking when you do not already know the name of the thing.
    /// </summary>
    public sealed class ToolPalette : EditorWindow
    {
        /// <summary>What a row needs to draw itself, none of which is worth working out per repaint.</summary>
        struct Row
        {
            public AuthoringTool Tool;
            public string MenuPath;
            public string Shortcut;
            public bool Available;
        }

        Vector2 _scroll;
        string _search = string.Empty;

        /// <summary>
        /// Availability walks the selection's hierarchy once per tool, which is far too much to spend
        /// on every repaint, so the rows are rebuilt only when what they depend on changes.
        /// </summary>
        List<Row> _rows;

        [MenuItem("Tools/MoSim Tool Palette", priority = 10)]
        [Shortcut("MoSim/Tool Palette", KeyCode.T, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        static void Open()
        {
            GetWindow<ToolPalette>("Tools").Show();
        }

        void OnEnable()
        {
            Selection.selectionChanged += Invalidate;
            EditorApplication.hierarchyChanged += Invalidate;
            Invalidate();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= Invalidate;
            EditorApplication.hierarchyChanged -= Invalidate;
        }

        void Invalidate()
        {
            _rows = null;
            Repaint();
        }

        List<Row> Rows()
        {
            return _rows ??= ToolRegistry.All.Select(tool => new Row
            {
                Tool = tool,
                MenuPath = ToolRegistry.Menu(tool)?.Path,
                Shortcut = ToolRegistry.Shortcut(tool),
                Available = tool.IsAvailable()
            }).ToList();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);
            }

            Row[] rows = Rows().Where(Matches).ToArray();

            if (rows.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_search) ? "No tools are registered." : "No tool matches that.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                Selection.gameObjects.Length == 0
                    ? "Nothing selected — tools that need a selection are greyed."
                    : $"{Selection.gameObjects.Length} object(s) selected.",
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Grouped rather than watched for a change of category: the registry orders by the tools'
            // own Order, and nothing stops two categories from interleaving there.
            foreach (IGrouping<string, Row> category in rows.GroupBy(r => r.Tool.Info.Category))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(category.Key, EditorStyles.boldLabel);

                foreach (Row row in category) Draw(row);
            }

            EditorGUILayout.EndScrollView();
        }

        static void Draw(Row row)
        {
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!row.Available))
            {
                var label = new GUIContent(row.Tool.Info.Title, row.MenuPath);
                if (GUILayout.Button(label, EditorStyles.miniButton)) ToolRegistry.Run(row.Tool);

                if (!string.IsNullOrEmpty(row.Shortcut))
                    GUILayout.Label(row.Shortcut, EditorStyles.miniLabel, GUILayout.Width(90f));
            }
        }

        bool Matches(Row row)
        {
            return string.IsNullOrEmpty(_search) ||
                   row.Tool.Info.ScenePath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
