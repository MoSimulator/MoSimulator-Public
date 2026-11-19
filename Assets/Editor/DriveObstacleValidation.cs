using System;
using System.Reflection;
using RobotFramework.Controllers.Drivetrain;
using RobotFramework.Motors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run through unitylive.py run DriveObstacleValidation.Run. Creates and destroys
// isolated preview scenes; never edits or saves the user's open scene.
public static class DriveObstacleValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string RunContactPerformance()
    {
        using (var fixture = new Fixture(new Vector3(0, 0.1f, -0.7f), true))
        {
            fixture.UseActualDrivetrain();
            fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
            for (int i = 0; i < 200; i++) fixture.Step(0f);
            return fixture.ProfileContacts(1000, false) + "\n" + fixture.ProfileContacts(1000, true);
        }
    }

    public static string RunSimboticsRebuiltBump()
    {
        Scene scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var robot = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Robots/Simbotics/Simbotics.prefab"), scene);
            var bump = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Field/Red/RedBump.prefab"), scene);
            bump.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var rigid in bump.GetComponentsInChildren<Rigidbody>()) rigid.isKinematic = true;
            robot.transform.SetPositionAndRotation(new Vector3(1.4f, 0.01f, 1.52f), Quaternion.Euler(0, 90, 0));
            var preloads = robot.transform.Find("Preloads");
            if (preloads != null) preloads.gameObject.SetActive(false);
            foreach (Collider collider in robot.GetComponentsInChildren<Collider>()) collider.gameObject.layer = 0;
            foreach (Collider collider in bump.GetComponentsInChildren<Collider>()) collider.gameObject.layer = 0;
            var floor = new GameObject("Validation carpet");
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = new Vector3(3, -0.05f, 0);
            floor.AddComponent<BoxCollider>().size = new Vector3(12, 0.1f, 12);
            var body = robot.GetComponent<Rigidbody>();
            var controller = robot.GetComponent<DriveController>();
            typeof(DriveController).GetField("_rb", Private).SetValue(controller, body);
            var baseField = typeof(DriveController).GetField("_robotBase", Private);
            baseField.SetValue(controller, robot.GetComponent(baseField.FieldType));
            typeof(DriveController).GetField("_fieldLayerMask", Private).SetValue(controller, 1 << LayerMask.NameToLayer("Robot"));
            DriveWheel[] wheels = robot.GetComponentsInChildren<DriveWheel>();
            Type patchType = typeof(DriveController).GetNestedType("WheelContactPatch", BindingFlags.NonPublic);
            Array patches = Array.CreateInstance(patchType, wheels.Length);
            for (int i = 0; i < wheels.Length; i++)
            {
                patches.SetValue(Activator.CreateInstance(patchType, true), i);
                wheels[i].Collider.wheelDampingRate = 0;
                wheels[i].Collider.motorTorque = 1e-30f;
            }
            typeof(DriveController).GetField("_wheels", Private).SetValue(controller, wheels);
            typeof(DriveController).GetField("_contactPatches", Private).SetValue(controller, patches);
            controller.RefreshRobotRigidbodies();
            body.maxLinearVelocity = controller.GetMaxVelocity();
            var drive = typeof(DriveController).GetMethod("DriveSwerve", Private);
            float maxPitch = 0, maxPitchRate = 0, peakHeight = 0, minSpeed = float.PositiveInfinity;
            float eventX = 0;
            for (int i = 0; i < 700; i++)
            {
                Physics.SyncTransforms();
                drive.Invoke(controller, new object[] { i < 200 ? 0f : controller.GetMaxVelocity(), 0f, 0f });
                scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
                if (i < 200) continue;
                peakHeight = Mathf.Max(peakHeight, body.position.y);
                float pitch = Mathf.Atan2(-robot.transform.forward.y,
                    new Vector2(robot.transform.forward.x, robot.transform.forward.z).magnitude) * Mathf.Rad2Deg;
                float pitchRate = Vector3.Dot(body.angularVelocity, robot.transform.right) * Mathf.Rad2Deg;
                if (pitch > maxPitch) { maxPitch = pitch; eventX = body.position.x; }
                maxPitchRate = Mathf.Max(maxPitchRate, pitchRate);
                if (body.position.x > 3.4f && body.position.x < 4.3f) minSpeed = Mathf.Min(minSpeed, body.velocity.x);
            }
            Require(maxPitch < 18f, "1114 avoids the excessive nose-down impulse over the Rebuilt bump", maxPitch);
            Require(maxPitchRate < 140f, "1114 crest transition has bounded pitch rate", maxPitchRate);
            Require(minSpeed > 1.8f && body.position.x > 5f, "1114 clears the bump without the bumper catch stalling it", minSpeed);
            return $"PASS: 1114 at full speed over Rebuilt bump; nose-down={maxPitch:F2} deg at x={eventX:F3}, pitch rate={maxPitchRate:F2} deg/s, crest speed minimum={minSpeed:F3} m/s";
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    public static string RunPhysicalWheelSupport()
    {
        using (var fixture = new Fixture(new Vector3(0, 0.065f, -0.7f), true))
        {
            fixture.AddPhysicalSupport();
            fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
            for (int i = 0; i < 150; i++) fixture.Step(0f);
            fixture.CheckFallbackSupport();
            float start = fixture.Body.position.z;
            for (int i = 0; i < 200; i++) fixture.Step(1f);
            float travel = fixture.Body.position.z - start;
            Require(travel > 0.4f, "Physical wheel support retains drive force", travel);
            fixture.Body.velocity = Vector3.right;
            for (int i = 0; i < 100; i++) fixture.Step(0f);
            Require(Mathf.Abs(fixture.Body.velocity.x) < 0.1f, "Physical wheel support retains sideways damper", fixture.Body.velocity.x);
            return $"PASS: physical wheel support; drive travel={travel:F4}, residual sideways speed={fixture.Body.velocity.x:F4}";
        }
    }

    public static string RunPrefabTraction()
    {
        using (var fixture = new Fixture(new Vector3(0, 0.1f, -0.7f), true))
        {
            fixture.UseActualDrivetrain();
            fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
            for (int i = 0; i < 200; i++) fixture.Step(0f);
            float start = fixture.Body.position.z;
            for (int i = 0; i < 200; i++) fixture.Step(1f);
            float travel = fixture.Body.position.z - start;
            Require(travel > 0.4f, "Actual drivetrain prefab retains floor drive force", travel);
            return $"PASS: actual Rebuilt drivetrain prefab; drive travel={travel:F4}";
        }
    }

    public static string Run()
    {
        float flatHeight;
        using (var fixture = new Fixture(new Vector3(0, 0.054f, -0.7f), true))
        {
            fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
            for (int i = 0; i < 200; i++) fixture.Step(0f);
            flatHeight = fixture.Body.position.y;
            Require(flatHeight > 0.045f && flatHeight < 0.065f, "Flat-floor support height", flatHeight);
            fixture.CheckNativeSupport();
        }
        float wallPosition;
        using (var fixture = new Fixture(new Vector3(0, 0.4f, -0.5f), false))
        {
            fixture.Box(new Vector3(0, 0.5f, 0.1f), new Vector3(2, 2, 0.2f));
            fixture.Body.velocity = new Vector3(0, 0, 2);
            for (int i = 0; i < 100; i++) fixture.Step(null);
            wallPosition = fixture.Body.position.z;
            Require(wallPosition < -0.29f, "Wheel cannot pass through a tall wall", wallPosition);
            Require(fixture.Body.velocity.z < 0.1f, "Wall stops closing velocity", fixture.Body.velocity.z);
            Require(fixture.Body.position.y < 0.41f, "Unpowered wall impact does not launch chassis", fixture.Body.position.y);
        }
        float peakHeight = 0f;
        float minStepTravel = float.PositiveInfinity;
        foreach (Vector3 direction in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            using (var fixture = new Fixture(new Vector3(0, 0.054f, 0) - direction * 0.7f, true))
            {
                fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
                Vector3 size = Mathf.Abs(direction.z) > 0.5f ? new Vector3(2, 0.025f, 1) : new Vector3(1, 0.025f, 2);
                fixture.Box(direction * 0.5f + Vector3.up * 0.0125f, size, direction == Vector3.left);
                for (int i = 0; i < 100; i++) fixture.Step(0f);
                float trialPeak = 0f;
                for (int i = 0; i < 450; i++)
                {
                    fixture.Step(direction.z, direction.x);
                    trialPeak = Mathf.Max(trialPeak, fixture.Body.position.y);
                }
                float travel = Vector3.Dot(fixture.Body.position, direction);
                Require(travel > 0.4f, $"Powered wheels climb a half-radius step toward {direction}", travel);
                Require(trialPeak > flatHeight + 0.018f && trialPeak < 0.15f, "Step raises chassis without launching it", trialPeak);
                peakHeight = Mathf.Max(peakHeight, trialPeak);
                minStepTravel = Mathf.Min(minStepTravel, travel);
            }
        }
        using (var fixture = new Fixture(new Vector3(0, 0.4f, 0), false))
        {
            fixture.RotateWheels(90f);
            Collider wall = fixture.Box(new Vector3(0, 0.4f, 0.38f), new Vector3(2, 0.2f, 0.2f));
            fixture.Step(null);
            Require(fixture.ContactCount() == 0, "Wheel query respects tread width after steering", fixture.ContactCount());
            wall.transform.position -= Vector3.forward * 0.015f;
            fixture.Body.velocity = Vector3.forward;
            fixture.Step(null);
            Require(fixture.ContactCount() > 0, "Wheel sidewall blocks actual width overlap", fixture.ContactCount());
            Require(fixture.Body.velocity.z < 1f, "Sidewall reaction opposes motion", fixture.Body.velocity.z);
        }
        using (var fixture = new Fixture(new Vector3(0, 0.054f, -0.29f), false))
        {
            fixture.Box(new Vector3(0, 0.0125f, 0.5f), new Vector3(2, 0.025f, 1));
            fixture.Step(null);
            Require(fixture.HasCornerNormal(), "Step corner supplies a diagonal wheel normal", fixture.ContactCount());
            Require(fixture.Body.velocity.y > 0f, "Corner support lifts wheel before central ray reaches step", fixture.Body.velocity.y);
        }
        using (var fixture = new Fixture(new Vector3(0, 0.054f, -0.7f), true))
        {
            fixture.Body.constraints = RigidbodyConstraints.None;
            fixture.Body.inertiaTensor = new Vector3(1.3f, 2.5f, 1.3f);
            fixture.Box(new Vector3(0, -0.05f, 0), new Vector3(5, 0.1f, 5));
            fixture.Box(new Vector3(0, 0.0125f, 0.5f), new Vector3(2, 0.025f, 1));
            for (int i = 0; i < 100; i++) fixture.Step(0f);
            float maxTilt = 0f;
            for (int i = 0; i < 450; i++)
            {
                fixture.Step(1f);
                maxTilt = Mathf.Max(maxTilt, Quaternion.Angle(Quaternion.identity, fixture.Body.rotation));
            }
            Require(fixture.Body.position.z > 0.4f, "Freely pitching chassis climbs the step", fixture.Body.position.z);
            Require(maxTilt < 35f, "Obstacle support does not destabilize chassis pitch", maxTilt);
        }
        return $"PASS: obstacle physics; flat height={flatHeight:F4}, wall stop z={wallPosition:F4}, minimum step travel={minStepTravel:F4}, peak height={peakHeight:F4}; four drive directions, mesh step, steered wheel width and sidewall";
    }

    private static void Require(bool condition, string name, float value)
    {
        if (!condition || float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidOperationException($"{name}: {value}");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Scene scene = EditorSceneManager.NewPreviewScene();
        private readonly DriveController controller;
        private readonly DriveWheel[] wheels = new DriveWheel[4];
        private readonly Array patches;
        private readonly MethodInfo drive = typeof(DriveController).GetMethod("DriveSwerve", Private);
        private readonly MethodInfo contacts = typeof(DriveController).GetMethod("UpdateWheelContacts", Private);
        private readonly MethodInfo torque = typeof(DriveController).GetMethod("ApplyDriveTorque", Private);
        public readonly Rigidbody Body;
        private PhysicMaterial supportMaterial;

        public Fixture(Vector3 position, bool gravity)
        {
            GameObject root = Create("Wheel obstacle validation");
            root.transform.position = position;
            Body = root.AddComponent<Rigidbody>();
            Body.mass = 60f;
            // RunDrivetrain normally sets this before dispatching DriveSwerve.
            Body.maxLinearVelocity = 4f;
            Body.useGravity = gravity;
            Body.constraints = RigidbodyConstraints.FreezeRotation;
            Body.inertiaTensor = new Vector3(10, 10, 10);
            controller = root.AddComponent<DriveController>();
            Set("_rb", Body);
            Set("_fieldLayerMask", 1 << LayerMask.NameToLayer("Robot"));
            Type patchType = typeof(DriveController).GetNestedType("WheelContactPatch", BindingFlags.NonPublic);
            patches = Array.CreateInstance(patchType, wheels.Length);
            for (int i = 0; i < wheels.Length; i++)
            {
                GameObject wheel = Create("Wheel " + i);
                wheel.transform.SetParent(root.transform, false);
                wheel.transform.localPosition = new Vector3((i & 1) == 0 ? -0.25f : 0.25f, 0, i < 2 ? -0.25f : 0.25f);
                var collider = wheel.AddComponent<WheelCollider>();
                collider.radius = 0.0508f;
                collider.mass = 1f;
                // DriveWheel.Update normally keeps the passive native wheel rolling.
                collider.wheelDampingRate = 0f;
                collider.motorTorque = 1e-30f;
                collider.suspensionDistance = 0.004f;
                collider.suspensionSpring = new JointSpring { spring = 200000f, damper = 1000f, targetPosition = 0.5f };
                wheels[i] = wheel.AddComponent<DriveWheel>();
                patches.SetValue(Activator.CreateInstance(patchType, true), i);
            }
            Set("_wheels", wheels);
            Set("_contactPatches", patches);
            var motor = new Motor();
            var config = (MotorConfig)typeof(Motor).GetField("config", Private).GetValue(motor);
            config.K_p = 0.5f;
            config.K_v = 0.138f;
            Set("motor", motor);
            Physics.SyncTransforms();
        }

        public void Step(float? target, float strafe = 0f)
        {
            Physics.SyncTransforms();
            if (target.HasValue) drive.Invoke(controller, new object[] { target.Value, strafe, 0f });
            else
            {
                contacts.Invoke(controller, null);
                for (int i = 0; i < wheels.Length; i++)
                    torque.Invoke(controller, new object[] { wheels[i], patches.GetValue(i), 0f, 0f, null });
            }
            scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        }

        public string ProfileContacts(int iterations, bool varyingPose)
        {
            var builds = typeof(DriveController).GetField("_wheelContactShapeBuilds", Private);
            var driveCall = (Action<float, float, float>)Delegate.CreateDelegate(typeof(Action<float, float, float>), controller, drive);
            var allocationMethod = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", BindingFlags.Public | BindingFlags.Static);
            var allocations = allocationMethod == null ? null : (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), allocationMethod);
            for (int i = 0; i < 20; i++) Step(0f);
            int before = (int)builds.GetValue(controller);
            var timer = new System.Diagnostics.Stopwatch();
            long allocatedBefore = allocations != null ? allocations() : 0;
            timer.Start();
            for (int i = 0; i < iterations; i++)
            {
                if (varyingPose) Body.rotation = Quaternion.Euler(Mathf.Sin(i * 0.01f), i * 0.03f, Mathf.Cos(i * 0.01f));
                Physics.SyncTransforms();
                driveCall(1f, varyingPose ? Mathf.Sin(i * 0.03f) : 0f, varyingPose ? 30f : 0f);
            }
            timer.Stop();
            long allocated = allocations != null ? allocations() - allocatedBefore : -1;
            int cooked = (int)builds.GetValue(controller) - before;
            Require(cooked == 0, "Steering does not rebuild contact meshes", cooked);
            return $"{(varyingPose ? "Turning/tilting" : "Straight")}: {iterations} drivetrain updates in {timer.Elapsed.TotalMilliseconds:F1} ms; convex mesh rebuilds={cooked}; managed allocation={allocated} bytes";
        }

        public void CheckNativeSupport()
        {
            int native = 0;
            foreach (object patch in patches)
            {
                Type type = patch.GetType();
                int floorCount = (int)type.GetField("FloorCount").GetValue(patch);
                if (floorCount > 0)
                {
                    native++;
                    int count = (int)type.GetField("Count").GetValue(patch);
                    Require(count == floorCount, "Flat carpet does not duplicate native support", count);
                }
            }
            Require(native == wheels.Length, "All wheels retain native floor support", native);
        }

        public void AddPhysicalSupport()
        {
            var support = Body.gameObject.AddComponent<BoxCollider>();
            support.center = new Vector3(0, -0.053f, 0);
            support.size = new Vector3(0.55f, 0.01f, 0.55f);
            supportMaterial = new PhysicMaterial("Validation support")
            {
                dynamicFriction = 0, staticFriction = 0, frictionCombine = PhysicMaterialCombine.Minimum,
                hideFlags = HideFlags.HideAndDontSave
            };
            support.sharedMaterial = supportMaterial;
        }

        public void UseActualDrivetrain()
        {
            foreach (var wheel in wheels) UnityEngine.Object.DestroyImmediate(wheel.gameObject);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Robots/Drivetrain Variant.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            instance.transform.SetParent(Body.transform, false);
            var actual = instance.GetComponentsInChildren<DriveWheel>();
            Require(actual.Length == wheels.Length, "Actual prefab has four drive wheels", actual.Length);
            for (int i = 0; i < wheels.Length; i++)
            {
                wheels[i] = actual[i];
                wheels[i].Collider.wheelDampingRate = 0f;
                wheels[i].Collider.motorTorque = 1e-30f;
            }
            float bottom = float.PositiveInfinity;
            foreach (var collider in instance.GetComponentsInChildren<Collider>())
            {
                // The test floor uses Default; leave all asset geometry and materials intact.
                collider.gameObject.layer = 0;
                if (!(collider is WheelCollider) && collider.enabled && !collider.isTrigger)
                    bottom = Mathf.Min(bottom, collider.bounds.min.y);
            }
            Require(!float.IsInfinity(bottom), "Actual prefab includes physical wheel colliders", bottom);
            Body.position += Vector3.up * (0.01f - bottom);
            Physics.SyncTransforms();
        }

        public void CheckFallbackSupport()
        {
            int native = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].Collider.GetGroundHit(out WheelHit hit) && hit.force > 0f) native++;
                object patch = patches.GetValue(i);
                int floor = (int)patch.GetType().GetField("FloorCount").GetValue(patch);
                float load = (float)patch.GetType().GetField("NormalLoad").GetValue(patch);
                Require(floor > 0 && load > 100f, "Physical support retains carpet contacts and load", load);
            }
            Require(native == 0, "Fixture reproduces physical support without native suspension load", native);
        }

        private GameObject Create(string name)
        {
            var result = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
        }

        public Collider Box(Vector3 position, Vector3 size, bool mesh = false)
        {
            GameObject box = Create("Obstacle");
            box.transform.position = position;
            if (!mesh)
            {
                var collider = box.AddComponent<BoxCollider>();
                collider.size = size;
                return collider;
            }
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh cube = primitive.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(primitive);
            box.transform.localScale = size;
            var terrain = box.AddComponent<MeshCollider>();
            terrain.sharedMesh = cube;
            return terrain;
        }

        public void RotateWheels(float angle)
        {
            foreach (var wheel in wheels) wheel.transform.localRotation = Quaternion.Euler(0, angle, 0);
        }

        public int ContactCount()
        {
            int count = 0;
            foreach (object patch in patches) count += (int)patch.GetType().GetField("Count").GetValue(patch);
            return count;
        }

        public bool HasCornerNormal()
        {
            foreach (object patch in patches)
            {
                Type type = patch.GetType();
                int count = (int)type.GetField("Count").GetValue(patch);
                Array hits = (Array)type.GetField("Hits").GetValue(patch);
                for (int i = 0; i < count; i++)
                {
                    object hit = hits.GetValue(i);
                    Vector3 normal = (Vector3)hit.GetType().GetField("normal").GetValue(hit);
                    if (normal.y > 0.2f && normal.z < -0.2f) return true;
                }
            }
            return false;
        }

        private void Set(string field, object value) => typeof(DriveController).GetField(field, Private).SetValue(controller, value);

        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            if (supportMaterial != null) UnityEngine.Object.DestroyImmediate(supportMaterial);
        }
    }
}
