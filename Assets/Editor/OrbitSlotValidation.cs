using RobotFramework.Components;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class OrbitSlotValidation
{
    public static string Run()
    {
        if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Run outside play mode.");
        return Trial(false, false) + "\n" + Trial(true, false) + "\n" + Trial(false, true) + "\n" + PrefabTrial();
    }

    private static string Trial(bool reverse, bool obstacle)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var track = new GameObject("Track"); SceneManager.MoveGameObjectToScene(track, scene);
            var trackBody = track.AddComponent<Rigidbody>(); trackBody.isKinematic = true;
            var pin = new GameObject("Pin"); SceneManager.MoveGameObjectToScene(pin, scene);
            var rb = pin.AddComponent<Rigidbody>(); rb.useGravity = false;
            rb.solverIterations = 24; rb.solverVelocityIterations = 12;
            var sphere = pin.AddComponent<SphereCollider>(); sphere.radius = .005f;
            float initial = reverse ? 55f : 5f;
            pin.transform.position = Quaternion.AngleAxis(initial, Vector3.right) * (Vector3.up * .1f);
            if (obstacle)
            {
                var block = new GameObject("Collision stop"); SceneManager.MoveGameObjectToScene(block, scene);
                block.transform.position = Quaternion.AngleAxis(30f, Vector3.right) * (Vector3.up * .1f);
                var box = block.AddComponent<BoxCollider>(); box.size = Vector3.one * .015f;
            }
            var joint = track.AddComponent<ConfigurableJoint>(); joint.enableCollision = true;
            var slot = track.AddComponent<CircularSlotJoint>();
            slot.Configure(joint, rb, Vector3.zero, Vector3.zero, Vector3.up * .1f, Vector3.right, 60f,
                new Collider[0], new Collider[0]);
            var physics = scene.GetPhysicsScene();
            if (physics.Equals(Physics.defaultPhysicsScene)) throw new System.InvalidOperationException("Validation requires an isolated physics scene.");
            float maximumError = 0f;
            for (int i = 0; i < 600; i++)
            {
                slot.UpdateConstraint();
                var tangent = Vector3.Cross(Vector3.right, rb.position).normalized;
                rb.AddForce(tangent * (reverse ? -.3f : .3f));
                physics.Simulate(Time.fixedDeltaTime);
                float error = Vector3.Distance(rb.position, slot.ClosestPoint(rb.position));
                maximumError = Mathf.Max(maximumError, error);
            }
            float angle = Vector3.SignedAngle(Vector3.up, rb.position, Vector3.right);
            if (maximumError > .005f || float.IsNaN(angle) ||
                (obstacle ? angle < 5f || angle > 30f : Mathf.Abs(angle - (reverse ? 0f : 60f)) > 2f))
                throw new System.InvalidOperationException($"Slot trial failed: reverse={reverse}, obstacle={obstacle}, angle={angle}, max error={maximumError}");
            return $"PASS reverse={reverse}, obstacle={obstacle}, angle={angle:F3}, max error={maximumError:F6}m, dt={Time.fixedDeltaTime}";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static string PrefabTrial()
    {
        var root = UnityEditor.PrefabUtility.LoadPrefabContents("Assets/Prefabs/Games/Reefscape/Robots/1690/1690.prefab");
        try
        {
            var physics = root.scene.GetPhysicsScene();
            if (physics.Equals(Physics.defaultPhysicsScene)) throw new System.InvalidOperationException("Prefab validation requires isolated physics.");
            var slot = root.GetComponentInChildren<CircularSlotJoint>(true);
            if (!slot || !slot.Constraint || !slot.PinBody) throw new System.InvalidOperationException("Missing slot wiring.");
            root.GetComponent<Rigidbody>().isKinematic = true;
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.useGravity = false;
                rb.solverIterations = 12; rb.solverVelocityIterations = 8;
            }
            slot.ApplyCollisionFilter();
            float maxError = 0f;
            for(int i=0;i<300;i++)
            {
                slot.UpdateConstraint();
                // Exercise the actual closed linkage without running game code.
                slot.GetComponent<Rigidbody>().AddTorque(Vector3.right * (i<150 ? .5f : -.5f));
                physics.Simulate(Time.fixedDeltaTime);
                float error=Vector3.Distance(slot.PinWorld, slot.transform.TransformPoint(slot.ClosestPoint(slot.transform.InverseTransformPoint(slot.PinWorld))));
                maxError=Mathf.Max(maxError,error);
                if(float.IsNaN(error) || error > .02f) throw new System.InvalidOperationException($"Prefab slot diverged at step {i}: {error}m");
            }
            return $"PASS actual prefab closed linkage, alternating torque, max error={maxError:F6}m";
        }
        finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
    }
}
