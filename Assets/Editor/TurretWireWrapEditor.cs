using RobotFramework.Components;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TurretWireWrap))]
public sealed class TurretWireWrapEditor : UnityEditor.Editor
{
    private float previewAngle;
    private Transform previewTurret;
    private Quaternion zeroLocalRotation;
    private bool previewApplied;

    private void OnEnable()
    {
        var source = serializedObject.FindProperty("turret").objectReferenceValue as GenericTurret;
        if (source == null) return;
        previewTurret = source.transform;
        zeroLocalRotation = previewTurret.localRotation;
    }

    private void OnDisable()
    {
        if (!previewApplied || previewTurret == null || Application.isPlaying) return;
        previewTurret.localRotation = zeroLocalRotation;
        if (target is TurretWireWrap wrap && wrap != null) wrap.RenderAngle(0f);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        DrawSetupControls();
        EditorGUILayout.HelpBox("Preview rotates the turret and its real chain mount together. Restore zero before saving. Take-up is a visual approximation; tune the guide and travel limits for the mechanism.", MessageType.Info);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            previewAngle = EditorGUILayout.FloatField("Preview angle (degrees)", previewAngle);
            if (GUILayout.Button("Preview wrap")) Preview(previewAngle);
            if (GUILayout.Button("Restore zero angle"))
            {
                previewAngle = 0f;
                Preview(0f);
            }
        }
        var wrap = (TurretWireWrap)target;
        if (wrap.TravelLimited)
            EditorGUILayout.HelpBox("The slider has reached its configured travel limit. Additional wrap no longer moves it.", MessageType.Warning);
        if (serializedObject.FindProperty("useFixedGuide").boolValue && !serializedObject.FindProperty("routeAlongGuide").boolValue)
            EditorGUILayout.HelpBox("Enable Route Along Guide to use the lower fixed guide.", MessageType.Warning);
        if (wrap.FixedGuideInvalid)
            EditorGUILayout.HelpBox("A cable endpoint is inside the lower guide radius. Move the guide or reduce its radius. The preview uses the automatic corner until the guide fits.", MessageType.Warning);
    }

    private void DrawSetupControls()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (serializedObject.FindProperty("wire").objectReferenceValue == null && GUILayout.Button("Create chain renderer"))
            {
                var owner = ((TurretWireWrap)target).transform;
                var chain = new GameObject("Generated Turret Energy Chain");
                Undo.RegisterCreatedObjectUndo(chain, "Create turret energy chain");
                chain.transform.SetParent(owner, false);
                var line = Undo.AddComponent<LineRenderer>(chain);
                line.enabled = false;
                line.positionCount = 0;
                line.sharedMaterial = serializedObject.FindProperty("chainMaterial").objectReferenceValue as Material;
                Undo.AddComponent<EnergyChainMesh>(chain);
                serializedObject.FindProperty("wire").objectReferenceValue = line;
                serializedObject.ApplyModifiedProperties();
            }
            var turret = serializedObject.FindProperty("turret").objectReferenceValue as GenericTurret;
            var frame = serializedObject.FindProperty("stationaryFrame").objectReferenceValue as Transform;
            var mount = serializedObject.FindProperty("turretAttachment").objectReferenceValue as Transform;
            var slider = serializedObject.FindProperty("slider").objectReferenceValue as Transform;
            using (new EditorGUI.DisabledScope(turret == null || frame == null || mount == null || slider == null || previewAngle != 0f))
            {
                if (GUILayout.Button("Capture mount and slider positions"))
                {
                    Vector3 axis = serializedObject.FindProperty("rotationAxis").vector3Value.normalized;
                    Vector3 center = frame.InverseTransformPoint(turret.transform.position);
                    Vector3 anchor = frame.InverseTransformPoint(mount.position);
                    center += axis * Vector3.Dot(anchor - center, axis);
                    serializedObject.FindProperty("center").vector3Value = center;
                    serializedObject.FindProperty("radius").floatValue = Mathf.Max(0.001f, Vector3.ProjectOnPlane(anchor - center, axis).magnitude);
                    serializedObject.FindProperty("sliderRestPosition").vector3Value = frame.InverseTransformPoint(slider.position);
                    serializedObject.ApplyModifiedProperties();
                }
            }
        }
        var wire = serializedObject.FindProperty("wire").objectReferenceValue as LineRenderer;
        if (serializedObject.FindProperty("chainMaterial").objectReferenceValue == null && (wire == null || wire.sharedMaterial == null))
            EditorGUILayout.HelpBox("Assign Chain Material to render the energy chain.", MessageType.Info);
    }

    private void Preview(float angle)
    {
        var source = serializedObject.FindProperty("turret").objectReferenceValue as GenericTurret;
        if (source != null && previewTurret != source.transform)
        {
            previewTurret = source.transform;
            zeroLocalRotation = previewTurret.localRotation;
        }
        var wrap = (TurretWireWrap)target;
        var slider = serializedObject.FindProperty("slider").objectReferenceValue as Transform;
        var wire = serializedObject.FindProperty("wire").objectReferenceValue as LineRenderer;
        if (slider != null) Undo.RecordObject(slider, "Preview turret wire wrap");
        if (wire != null) Undo.RecordObject(wire, "Preview turret wire wrap");
        var frame = serializedObject.FindProperty("stationaryFrame").objectReferenceValue as Transform;
        if (previewTurret != null && frame != null)
        {
            Undo.RecordObject(previewTurret, "Preview turret wire wrap");
            Vector3 worldAxis = frame.TransformDirection(serializedObject.FindProperty("rotationAxis").vector3Value).normalized;
            Vector3 parentAxis = previewTurret.parent != null ? previewTurret.parent.InverseTransformDirection(worldAxis) : worldAxis;
            float multiplier = serializedObject.FindProperty("angleMultiplier").floatValue;
            previewTurret.localRotation = Quaternion.AngleAxis(angle * multiplier, parentAxis) * zeroLocalRotation;
            previewApplied = true;
        }
        wrap.RenderAngle(angle);
        SceneView.RepaintAll();
    }
}
