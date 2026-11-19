using RobotFramework.Components;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnergyChainRun))]
public sealed class EnergyChainRunEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Route markers are ordered from one chain end to the other. Parent moving markers to an arm, elevator or slider. Corners round automatically; the chain updates when markers move. This is a visual route and does not preserve total chain length.", MessageType.Info);
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(Application.isPlaying || serializedObject.FindProperty("waypoints").arraySize != 0))
        {
            if (GUILayout.Button("Create straight run")) CreateRoute(false);
            if (GUILayout.Button("Create U-loop run")) CreateRoute(true);
        }
        if (GUILayout.Button("Refresh chain")) ((EnergyChainRun)target).RebuildNow();
        if (serializedObject.FindProperty("chainMaterial").objectReferenceValue == null && ((EnergyChainRun)target).GetComponent<MeshRenderer>().sharedMaterial == null)
            EditorGUILayout.HelpBox("Assign Chain Material to render the links.", MessageType.Info);
    }

    private void CreateRoute(bool loop)
    {
        var run = (EnergyChainRun)target;
        Vector3[] positions = loop
            ? new[] { Vector3.zero, new Vector3(0f, 0f, 0.3f), new Vector3(0.15f, 0f, 0.3f), new Vector3(0.15f, 0f, 0f) }
            : new[] { Vector3.zero, new Vector3(0f, 0f, 0.3f) };
        string[] names = loop
            ? new[] { "Fixed End", "Return Bend In", "Return Bend Out", "Moving End" }
            : new[] { "Fixed End", "Moving End" };
        var points = serializedObject.FindProperty("waypoints");
        points.arraySize = positions.Length;
        for (int i = 0; i < positions.Length; i++)
        {
            var marker = new GameObject(names[i]);
            Undo.RegisterCreatedObjectUndo(marker, "Create energy chain route");
            marker.transform.SetParent(run.transform, false);
            marker.transform.localPosition = positions[i];
            points.GetArrayElementAtIndex(i).objectReferenceValue = marker.transform;
        }
        serializedObject.FindProperty("followMovingEnd").boolValue = loop;
        var frame = serializedObject.FindProperty("referenceFrame").objectReferenceValue as Transform;
        if (frame == null) frame = run.transform;
        serializedObject.FindProperty("movingEndAtRest").vector3Value = frame.InverseTransformPoint(run.transform.TransformPoint(positions[positions.Length - 1]));
        serializedObject.ApplyModifiedProperties();
        run.RebuildNow();
        SceneView.RepaintAll();
    }
}
