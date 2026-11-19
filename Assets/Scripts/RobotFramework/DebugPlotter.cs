#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RobotFramework
{
    /// <summary>
    /// Utility window for plotting float values over time, intended to aid sim development.
    /// <para>THIS CLASS DOES NOT COMPILE OUTSIDE OF THE UNITY EDITOR. Make sure to #ifdef usage.</para>
    /// 
    /// <para>Example usage:</para>
    /// <code>
    /// private float foo = 0f;
    /// private float bar = 0f;
    /// 
    /// private void FixedUpdate()
    /// {
    ///   foo++;
    ///   bar *= 2;
    /// 
    ///   DebugPlotter.Add("Foo", foo);
    ///   DebugPlotter.Add("Bar", bar);
    /// }
    /// </code>
    /// </summary>
    public class DebugPlotter : EditorWindow
    {
        public static int SampleDepth = 750;
        private static readonly Dictionary<string, List<(float, float)>> _data = new();
        private static readonly Dictionary<string, bool> _disabled = new();
        private static readonly Color[] _colors =
        {
            new(0.169f, 0.400f, 0.635f),
            new(0.898f, 0.702f, 0.106f),
            new(0.686f, 0.141f, 0.216f),
            new(0.502f, 0.345f, 0.557f),
            new(0.894f, 0.545f, 0.196f),
            new(0.753f, 0.706f, 0.529f),
            new(0.522f, 0.522f, 0.518f),
            new(0.231f, 0.529f, 0.353f),
            new(0.851f, 0.576f, 0.667f),
            new(0.373f, 0.271f, 0.157f)
        };

        /// <summary>
        /// Adds a sample to the plot.
        /// </summary>
        /// <param name="name">The name of the collection the sample belongs to.</param>
        /// <param name="value">The value of the sample.</param>
        public static void Add(string name, float value)
        {
            if (!_data.TryGetValue(name, out var samples))
            {
                samples = new();
                _data.Add(name, samples);
            }

            samples.Add((value, Time.time));
            if (samples.Count > SampleDepth) samples.RemoveRange(0, Mathf.Clamp(samples.Count - SampleDepth, 0, samples.Count));
        }

        [MenuItem("Tools/Debug Plotter")]
        public static void Open()
        {
            GetWindow<DebugPlotter>("Debug Plotter");
        }

        private void OnGUI()
        {
            Rect container = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );

            const float yLabelWidth = 60f;
            const int yTickCount = 7;

            EditorGUI.DrawRect(container, new Color(0.133f, 0.133f, 0.133f));
            var graph = new Rect(container.x + yLabelWidth, container.y + 24f, container.width - yLabelWidth - 26f, container.height - 48f);

            float valueRange = 1e-4f;
            (float, float) timeRange = (float.MaxValue, float.MinValue);
            foreach (var (name, list) in _data)
            {
                if (_disabled.GetValueOrDefault(name, false)) continue;

                foreach (var sample in list)
                {
                    valueRange = Mathf.Max(valueRange, Mathf.Abs(sample.Item1));
                    timeRange.Item1 = Mathf.Min(timeRange.Item1, sample.Item2);
                    timeRange.Item2 = Mathf.Max(timeRange.Item2, sample.Item2);
                }
            }

            valueRange *= 1.05f;

            Handles.BeginGUI();

            int colorIter = 0;
            foreach (var (name, list) in _data)
            {
                Handles.color = _colors[colorIter];
                colorIter = colorIter != _colors.Length - 1 ? colorIter + 1 : 0;

                if (_disabled.GetValueOrDefault(name, false)) continue;
                for (int i = 0; i < list.Count - 1; i++)
                {
                    var sample1 = list[i];
                    var sample2 = list[i + 1];

                    Handles.DrawLine(
                        new Vector3(
                            Mathf.Lerp(graph.x, graph.xMax, Mathf.InverseLerp(timeRange.Item1, timeRange.Item2, sample1.Item2)),
                            Mathf.Lerp(graph.yMax, graph.y, Mathf.InverseLerp(-valueRange, valueRange, sample1.Item1))
                        ),
                        new Vector3(
                            Mathf.Lerp(graph.x, graph.xMax, Mathf.InverseLerp(timeRange.Item1, timeRange.Item2, sample2.Item2)),
                            Mathf.Lerp(graph.yMax, graph.y, Mathf.InverseLerp(-valueRange, valueRange, sample2.Item1))
                        )
                    );
                }
            }

            Handles.color = new Color(1f, 1f, 1f, 0.2f);
            for (int i = 0; i < yTickCount; i++)
            {
                float y = Mathf.Lerp(graph.yMax, graph.y, i / (yTickCount - 1f));
                Handles.DrawLine(new Vector3(graph.x, y), new Vector3(graph.xMax, y));
            }

            Handles.EndGUI();

            GUIStyle scaleLabel = new(EditorStyles.label) { normal = { textColor = Color.white }, alignment = TextAnchor.MiddleRight };
            for (int i = 0; i < yTickCount; i++)
            {
                float norm = i / (yTickCount - 1f);
                GUI.Label(
                    new Rect(
                        container.x,
                        Mathf.Lerp(graph.yMax, graph.y, norm) - 9f,
                        yLabelWidth - 8f,
                        18f
                    ),
                    Mathf.Lerp(-valueRange, valueRange, norm).ToString("F2"),
                    scaleLabel
                );
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space();

            colorIter = 0;
            foreach(var (name, _) in _data)
            {
                GUIContent content = new GUIContent(name);
                GUIStyle style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = _colors[colorIter];
                colorIter = colorIter != _colors.Length - 1 ? colorIter + 1 : 0;
                _disabled[name] = !EditorGUILayout.Toggle(!_disabled.GetValueOrDefault(name, false), GUILayout.Width(15f));
                EditorGUILayout.LabelField(content, style, GUILayout.Width(EditorStyles.label.CalcSize(content).x + 8f));
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Sample Depth", GUILayout.Width(85f));
            SampleDepth = EditorGUILayout.IntField(SampleDepth, GUILayout.MaxWidth(75f));

            if (GUILayout.Button("Clear Data", GUILayout.ExpandWidth(false)))
            {
                foreach (var (_, list) in _data) list.Clear();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);

            Repaint();
        }
    }
}

#endif
