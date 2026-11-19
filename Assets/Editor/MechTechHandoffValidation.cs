using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run with: python Tools/unitylive.py --json run MechTechHandoffValidation.Run
public static class MechTechHandoffValidation
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var robotType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Prefabs.Reefscape.Robots.Mods.OfficialTest._422.MechTechDragons"))
            .First(type => type != null);
        var carry = robotType.GetMethod("ApplyGroundCoralHandoffMotion", BindingFlags.Static | BindingFlags.NonPublic);
        if (carry == null) throw new InvalidOperationException("Handoff motion method not found.");

        var results = new System.Collections.Generic.List<string>();
        foreach (float speed in new[] { -4f, 0f, 4f })
        foreach (float acceleration in new[] { -6f, 0f, 6f })
        {
            float drift = Trial(carry, speed, acceleration, true);
            if (drift > 0.04f)
                throw new InvalidOperationException($"Excess handoff drift: speed={speed}, acceleration={acceleration}, drift={drift}");
            results.Add($"v={speed}, a={acceleration}: {drift:F4}m");
        }
        float baseline = Trial(carry, 4f, 6f, false);
        float assisted = Trial(carry, 4f, 6f, true);
        if (assisted >= baseline * 0.2f)
            throw new InvalidOperationException($"Insufficient improvement: baseline={baseline}, assisted={assisted}");
        return $"PASS: 9 physics trials; unassisted drift={baseline:F4}m, assisted={assisted:F4}m. " + string.Join("; ", results);
    }

    private static float Trial(MethodInfo carry, float speed, float acceleration, bool assisted)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var physics = scene.GetPhysicsScene();
            if (physics.Equals(Physics.defaultPhysicsScene))
                throw new InvalidOperationException("Validation requires isolated physics.");
            var robot = NewBody("Handoff chassis", scene);
            robot.useGravity = false;
            robot.rotation = Quaternion.Euler(0f, 63f, 0f);
            var forward = robot.rotation * Vector3.forward;
            robot.velocity = forward * speed;
            var coral = NewBody("Dropped coral", scene);
            coral.position = new Vector3(0f, 2f, 0f);
            coral.drag = 0.5f;
            coral.velocity = robot.velocity + Vector3.down * 2f;
            var fallingControl = NewBody("Vertical motion control", scene);
            fallingControl.position = coral.position;
            fallingControl.drag = coral.drag;
            fallingControl.velocity = Vector3.down * 2f;

            const float step = 0.01f;
            float maximumDrift = 0f;
            for (int i = 0; i < 40; i++)
            {
                robot.velocity = forward * (speed + acceleration * i * step);
                if (assisted) carry.Invoke(null, new object[] { coral, robot });
                physics.Simulate(step);
                maximumDrift = Mathf.Max(maximumDrift,
                    Vector3.ProjectOnPlane(coral.position - robot.position, Vector3.up).magnitude);
                if (Mathf.Abs(coral.position.y - fallingControl.position.y) > 0.0001f)
                    throw new InvalidOperationException("Handoff assistance changed vertical falling motion.");
            }
            return maximumDrift;
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Rigidbody NewBody(string name, Scene scene)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        var body = obj.AddComponent<Rigidbody>();
        body.drag = 0f;
        return body;
    }
}
