using System.Linq;
using UnityEditor;
using UnityEngine;
using RobotFramework.Components;

public static class OrbitSlotSetup
{
    private const string Path = "Assets/Prefabs/Games/Reefscape/Robots/1690/1690.prefab";
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit play mode before installing.");
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        bool useStage = stage != null && stage.assetPath == Path;
        var root = useStage ? stage.prefabContentsRoot : PrefabUtility.LoadPrefabContents(Path);
        try
        {
            var bodies=root.GetComponentsInChildren<Rigidbody>(true);
            var track=bodies.Single(r=>r.name=="LowerFourBar");
            var pin=bodies.Single(r=>r.name=="FrontFlap");
            var mesh=track.GetComponentsInChildren<MeshFilter>(true).Single(m=>m.sharedMesh && m.sharedMesh.name=="<1690-25-2000 Basepart>-<Cut-Extrude24>");
            var pinMeshes=pin.GetComponentsInChildren<MeshFilter>(true).Where(m=>m.name=="1690-25-2574" || m.name=="1690-25-2574.001").ToArray();
            if(pinMeshes.Length!=2) throw new System.InvalidOperationException("Expected the two 2574 slot pins.");
            var pinPoints=pinMeshes.Select(m=>m.transform.TransformPoint(m.sharedMesh.bounds.center)).ToArray();
            var anchor=(pinPoints[0]+pinPoints[1])*.5f;
            // Imported mesh vertices are centimetres of FBX source coordinates;
            // Unity flips X on import. The center-plane constraint represents both
            // symmetric slots without introducing redundant side-to-side joints.
            System.Func<float,float,Vector3> world=(y,z)=> {
                var p=mesh.transform.TransformPoint(new Vector3(.002243f,y*.01f,z*.01f));
                p.x=anchor.x;
                return p;
            };
            var center=track.transform.InverseTransformPoint(world(.25999941f,.51350100f));
            var start=track.transform.InverseTransformPoint(world(.18554525f,.59386675f))-center;
            var end=track.transform.InverseTransformPoint(world(.22648555f,.61780295f))-center;
            var normal=Vector3.Cross(start,end).normalized;
            float sweep=Vector3.Angle(start,end);
            float radius=track.transform.TransformVector(start).magnitude;
            if(Mathf.Abs(radius-.109554f)>.0001f || sweep<20f || sweep>30f)
                throw new System.InvalidOperationException($"Unexpected imported slot dimensions {radius} / {sweep}");
            // These two long axial proxies are the existing slot-contact pins.
            // Keep their external contacts; replace only contacts with the track.
            var pinContacts=pin.GetComponentsInChildren<Collider>(true).Where(c=>c.attachedRigidbody==pin && c.name=="NoBlowUpColliders").ToArray();
            if(pinContacts.Length==0) throw new System.InvalidOperationException("Slot pin colliders could not be identified.");
            var trackContacts=track.GetComponentsInChildren<Collider>(true).Where(c=>c.attachedRigidbody==track).ToArray();
            var slot=track.GetComponent<CircularSlotJoint>();
            if(!slot) slot=Undo.AddComponent<CircularSlotJoint>(track.gameObject);
            var joint=slot.Constraint;
            if(!joint) joint=Undo.AddComponent<ConfigurableJoint>(track.gameObject);
            Undo.RecordObject(slot,"Configure Orbit curved slot");
            Undo.RecordObject(joint,"Configure Orbit curved slot");
            joint.enableCollision=true;
            slot.Configure(joint,pin,pin.transform.InverseTransformPoint(anchor),center,start,normal,sweep,pinContacts,trackContacts);
            EditorUtility.SetDirty(slot); EditorUtility.SetDirty(joint);
            PrefabUtility.SaveAsPrefabAsset(root,Path);
            return $"Installed slot: radius={radius:F6}m, sweep={sweep:F3}deg, pin colliders={pinContacts.Length}, track colliders={trackContacts.Length}, initial error={Vector3.Distance(track.transform.TransformPoint(slot.ClosestPoint(track.transform.InverseTransformPoint(anchor))),anchor):F6}m";
        }
        finally { if(!useStage) PrefabUtility.UnloadPrefabContents(root); }
    }
}
