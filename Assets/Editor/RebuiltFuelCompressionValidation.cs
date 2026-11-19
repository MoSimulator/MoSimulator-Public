using System;
using System.Collections.Generic;
using Games.Rebuilt.GamePieceSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Run through unitylive outside play mode; all trials use isolated, unsaved physics scenes.</summary>
public static class RebuiltFuelCompressionValidation
{
    private const float CompressionDepth = 0.05f;
    private const float FrictionMultiplier = 6f;

    public static string RunPerformance()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        return PerformanceTrial(false) + "\n" + PerformanceTrial(true);
    }

    public static string RunLifecycle()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        try
        {
            var fuel = new GameObject("Lifecycle fuel");
            SceneManager.MoveGameObjectToScene(fuel, scene);
            var sphere = fuel.AddComponent<SphereCollider>();
            sphere.radius = 0.075f;
            var fuelBody = fuel.AddComponent<Rigidbody>();
            fuelBody.isKinematic = true;
            fuelBody.solverIterations = fuelBody.solverVelocityIterations = 1;
            var robot = CreateBox(scene, Vector3.up);
            robot.constraints = RigidbodyConstraints.FreezeAll;
            robot.transform.position = Vector3.up * 0.08f;
            var box = robot.GetComponent<BoxCollider>();
            var preserved = CreateContactBox(robot.transform, Vector3.up, Vector3.one * 0.1f);
            preserved.hasModifiableContacts = true;
            modifier.RegisterRobotBody(robot, box);
            modifier.RegisterFuel(0, fuelBody);
            modifier.Enable();
            var physics = scene.GetPhysicsScene();
            void Step(float depth = CompressionDepth)
            {
                Physics.SyncTransforms();
                if (robot) robot.WakeUp();
                modifier.PrepareStep(Vector3.down * 9.81f, depth, 0.25f);
                physics.Simulate(0.01f);
            }
            void Require(bool condition, string message)
            {
                if (!condition) throw new InvalidOperationException(message);
            }
            Step();
            Step();
            Require(!sphere.hasModifiableContacts && box.hasModifiableContacts, "Contact flags must belong to robot geometry.");
            Require(fuelBody.solverIterations >= 8, "Initial load did not boost fuel.");
            for (int i = 0; i < 3; i++)
            {
                modifier.Disable();
                Require(!box.hasModifiableContacts && preserved.hasModifiableContacts, "Disable lost original contact flags.");
                Require(fuelBody.solverIterations == 1, "Disable did not restore the baseline solver.");
                modifier.Enable();
                Step();
                Step();
                Require(fuelBody.solverIterations >= 8, "Reenable lost a load notification.");
            }
            Step(0f);
            Require(!box.hasModifiableContacts && preserved.hasModifiableContacts && fuelBody.solverIterations == 1,
                "Zero compression must restore contact flags and solver settings.");
            Step();
            Step();
            Require(fuelBody.solverIterations >= 8, "Reactivating compression lost a load notification.");
            robot.transform.position = Vector3.up;
            Step();
            Step();
            Step();
            Require(fuelBody.solverIterations == 1, "Release did not expire the adaptive solver boost.");
            robot.transform.position = Vector3.up * 0.08f;
            Step(); // Leave a notification pending while replacing the registered slot.
            modifier.UnregisterFuel(0);
            modifier.RegisterFuel(0, fuelBody);
            Step();
            Step();
            Require(fuelBody.solverIterations >= 8, "Slot reuse lost the replacement fuel's load notification.");
            UnityEngine.Object.DestroyImmediate(robot.gameObject);
            Step();
            Step();
            Step();
            Require(fuelBody.solverIterations == 1, "Destroyed robot did not release the fuel solver.");
            return "PASS contact lifecycle: robot-only flags, original flags preserved, disable/reenable, zero depth, release expiry, queued slot reuse, and destroyed chassis cleanup.";
        }
        finally
        {
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static string PerformanceTrial(bool loaded)
    {
        const int fuelCount = 256;
        const int warmupSteps = 60;
        const int measuredSteps = 300;
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        Action<PhysicsScene, Unity.Collections.NativeArray<ModifiableContactPair>> observe = null;
        long pairCount = 0;
        Transform movingChassis = null;
        try
        {
            var floor = new GameObject("Performance floor");
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = Vector3.down * 0.05f;
            floor.AddComponent<BoxCollider>().size = new Vector3(10f, 0.1f, 10f);
            var bodies = new Rigidbody[fuelCount];
            for (int i = 0; i < fuelCount; i++)
            {
                var fuel = new GameObject("Performance fuel");
                SceneManager.MoveGameObjectToScene(fuel, scene);
                fuel.transform.position = new Vector3((i % 16) * 0.16f, 0.075f, (i / 16) * 0.16f);
                fuel.AddComponent<SphereCollider>().radius = 0.075f;
                var body = fuel.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.solverIterations = 1;
                body.solverVelocityIterations = 1;
                body.constraints = RigidbodyConstraints.FreezeAll;
                bodies[i] = body;
                modifier.RegisterFuel(i, body);
            }
            if (loaded)
            {
                var chassis = new GameObject("Performance chassis");
                SceneManager.MoveGameObjectToScene(chassis, scene);
                chassis.transform.position = new Vector3(0.16f, 0.145f, 0.16f);
                movingChassis = chassis.transform;
                var box = chassis.AddComponent<BoxCollider>();
                box.size = new Vector3(0.46f, 0.05f, 0.46f);
                var body = chassis.AddComponent<Rigidbody>();
                body.isKinematic = true;
                modifier.RegisterRobotBody(body, box);
                // Articulated bodies share the chassis envelope even when their own contacts are remote.
                for (int i = 0; i < 12; i++)
                {
                    var part = new GameObject("Performance robot part");
                    SceneManager.MoveGameObjectToScene(part, scene);
                    part.transform.position = new Vector3(i * 0.1f, 2f, 0f);
                    part.AddComponent<BoxCollider>().size = Vector3.one * 0.05f;
                    var partBody = part.AddComponent<Rigidbody>();
                    partBody.isKinematic = true;
                    modifier.RegisterRobotBody(partBody, box);
                }
            }
            var physics = scene.GetPhysicsScene();
            observe = (source, pairs) =>
            {
                if (source == physics) System.Threading.Interlocked.Add(ref pairCount, pairs.Length);
            };
            modifier.Enable();
            Physics.ContactModifyEvent += observe;
            Physics.SyncTransforms();
            long prepareTicks = 0;
            long simulateTicks = 0;
            long allocatedBytes = 0;
            // Some Unity runtimes expose this API but return zero even for known allocations.
            long probeBefore = GC.GetAllocatedBytesForCurrentThread();
            var allocationProbe = new byte[1024];
            bool allocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > probeBefore;
            GC.KeepAlive(allocationProbe);
            for (int i = -warmupSteps; i < measuredSteps; i++)
            {
                foreach (var body in bodies) body.WakeUp();
                if (movingChassis)
                    movingChassis.position = new Vector3(0.16f, 0.145f + Mathf.Sin(i * 0.1f) * 0.001f, 0.16f);
                Physics.SyncTransforms();
                if (i == 0) System.Threading.Interlocked.Exchange(ref pairCount, 0);
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long begin = System.Diagnostics.Stopwatch.GetTimestamp();
                modifier.PrepareStep(Vector3.down * 9.81f, CompressionDepth, 0.25f, FrictionMultiplier);
                long prepared = System.Diagnostics.Stopwatch.GetTimestamp();
                physics.Simulate(0.01f);
                long simulated = System.Diagnostics.Stopwatch.GetTimestamp();
                if (i < 0) continue;
                prepareTicks += prepared - begin;
                simulateTicks += simulated - prepared;
                allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }
            double millisecondsPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency / measuredSteps;
            string allocation = allocationCounterAvailable
                ? $"{allocatedBytes / (double)measuredSteps:F1} B/step"
                : "unavailable on this runtime";
            return $"{(loaded ? "Loaded chassis" : "Loose fuel")}: {fuelCount} awake fuel, " +
                $"{pairCount / (double)measuredSteps:F1} callback pairs/step, " +
                $"prepare={prepareTicks * millisecondsPerTick:F4} ms/step, " +
                $"simulate={simulateTicks * millisecondsPerTick:F4} ms/step, " +
                $"main-thread allocation={allocation}. " +
                "Synthetic editor fixture; timings include an identical pair-count observer.";
        }
        finally
        {
            if (observe != null) Physics.ContactModifyEvent -= observe;
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    public static string InspectSkiCandidates()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Robots/2910/ReBlitz/2910B.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var colliders = new List<Collider>(root.GetComponentsInChildren<Collider>());
            colliders.Sort((a, b) => a.bounds.min.y.CompareTo(b.bounds.min.y));
            var lines = new List<string>();
            foreach (var collider in colliders)
            {
                if (!collider.enabled || collider.isTrigger || collider.bounds.center.y > 0.16f ||
                    collider is WheelCollider || collider.GetComponentInParent<RebuiltGamePieceController>()) continue;
                string path = collider.name;
                for (var parent = collider.transform.parent; parent != root.transform && parent; parent = parent.parent)
                    path = parent.name + "/" + path;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(collider);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long id);
                lines.Add($"{path} {collider.GetType().Name} fileID={id} min={collider.bounds.min:F4} max={collider.bounds.max:F4} material={collider.sharedMaterial?.name}");
                if (lines.Count == 20) break;
            }
            return string.Join("\n", lines);
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var results = new List<string>();
        float baseline = Trial(Vector3.up, Vector3.up, 0f, true, false, false);
        float compressed = Trial(Vector3.up, Vector3.up, CompressionDepth, true, false, false);
        Check("top compression", baseline - compressed, CompressionDepth, results);
        Check("creation order", Trial(Vector3.up, Vector3.up, CompressionDepth, true, true, false), compressed, results);
        Check("field contact", Trial(Vector3.up, Vector3.up, CompressionDepth, false, false, false), baseline, results);
        Check("side contact", Trial(Vector3.right, Vector3.up, CompressionDepth, true, false, false), baseline, results);
        Check("underside contact", Trial(Vector3.down, Vector3.up, CompressionDepth, true, false, false), baseline, results);
        Check("rotated gravity", Trial(Vector3.right, Vector3.right, CompressionDepth, true, false, false), compressed, results);
        Check("zero gravity", Trial(Vector3.up, Vector3.zero, CompressionDepth, true, false, false), baseline, results);
        Check("disabled modifier", Trial(Vector3.up, Vector3.up, CompressionDepth, true, false, true), baseline, results);
        Check("reenabled modifier", Trial(Vector3.up, Vector3.up, CompressionDepth, true, false, false, true), compressed, results);
        Check("continuous collision", Trial(Vector3.up, Vector3.up, CompressionDepth, true, false, false, false, true), compressed, results);
        Check("fuel to fuel", Trial(Vector3.up, Vector3.up, CompressionDepth, false, false, false, false, false, true), baseline, results);
        float rigidHopper = HopperTrial(0f, out var rail);
        float protectedHopper = HopperTrial(CompressionDepth, out _);
        Check("2910B hopper pressure from fuel below", protectedHopper, rigidHopper, results);
        Check("hopper regression control", HopperTrial(CompressionDepth, out _, true) - rigidHopper, CompressionDepth, results);
        results.Add("Hopper collider: " + rail);
        results.Add(RunFriction());
        results.Add(RunSkis());
        results.Add(RunRollingEscape());
        results.Add(RunLoadedEscape());
        results.Add(RunTiltedSupport());
        results.Add(RunCompressedEdges());
        results.Add(RunLifecycle());
        return string.Join("\n", results);
    }

    public static string RunSkis()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        float blocked = SkiTrial(false);
        float released = SkiTrial(true);
        if (released < 0.47f || released - blocked < 0.1f)
            throw new InvalidOperationException($"Ski escape failed: original z={blocked:F4}, glide z={released:F4}.");
        return $"PASS actual 2910B ski escape: original z={blocked:F4}, glide z={released:F4} m.";
    }

    public static string RunRollingEscape()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        float position = SkiTrial(true, true, true);
        return $"PASS stationary 2910B rolling escape on carpet: final z={position:F4} m; no pull-back reversal.";
    }

    public static string RunLoadedEscape()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        float position = SkiTrial(true, true, true, true);
        return $"PASS loaded 2910B escape: baseline solver 1/1, adaptive load boost 8/4, restored afterward; z={position:F4} m, no pull-back reversal.";
    }

    public static string RunTiltedSupport()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        Action<PhysicsScene, Unity.Collections.NativeArray<ModifiableContactPair>> observe = null;
        try
        {
            var fuel = new GameObject("Tilted support fuel");
            SceneManager.MoveGameObjectToScene(fuel, scene);
            fuel.AddComponent<SphereCollider>().radius = 0.075f;
            var fuelBody = fuel.AddComponent<Rigidbody>();
            fuelBody.isKinematic = true;
            var box = CreateBox(scene, Vector3.up);
            box.transform.SetPositionAndRotation(Vector3.up * (0.115f - CompressionDepth),
                Quaternion.AngleAxis(5f, Vector3.forward));
            Physics.SyncTransforms();
            box.constraints = RigidbodyConstraints.FreezeAll;
            var expectedNormal = box.rotation * Vector3.up;
            modifier.RegisterFuel(0, fuelBody);
            modifier.RegisterRobotBody(box);
            modifier.Enable();
            int fuelId = fuel.GetComponent<SphereCollider>().GetInstanceID();
            float alignment = -1f;
            observe = (_, pairs) =>
            {
                foreach (var pair in pairs)
                {
                    bool first = pair.colliderInstanceID == fuelId;
                    if (!first && pair.otherColliderInstanceID != fuelId) continue;
                    for (int i = 0; i < pair.contactCount; i++)
                        alignment = Vector3.Dot(pair.GetNormal(i) * (first ? -1f : 1f), expectedNormal);
                }
            };
            Physics.ContactModifyEvent += observe;
            box.WakeUp();
            modifier.PrepareStep(Vector3.down * 9.81f, CompressionDepth, 0.25f, FrictionMultiplier);
            scene.GetPhysicsScene().Simulate(0.01f);
            if (alignment == -1f) throw new InvalidOperationException("Tilted fixture generated no contacts.");
            if (alignment < 0.99999f)
                throw new InvalidOperationException($"Tilted main support was changed into an edge contact: normal alignment={alignment:F6}.");
            return "PASS tilted main support: keeps its original normal and compression response instead of switching to edge glide.";
        }
        finally
        {
            if (observe != null) Physics.ContactModifyEvent -= observe;
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static float SkiTrial(bool glide, bool carpet = false, bool checkReversal = false, bool loaded = false)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        var floorMaterial = new PhysicMaterial("Ski validation floor")
        {
            staticFriction = 0f, dynamicFriction = 0f, bounciness = 0f,
            frictionCombine = PhysicMaterialCombine.Minimum, bounceCombine = PhysicMaterialCombine.Minimum
        };
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Robots/2910/ReBlitz/2910B.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var robot = root.GetComponent<Games.Rebuilt.Robots.RebuiltRobotBase>();
            var ski = root.transform.Find("Stationary/drivebase/22-03-07.001").GetComponent<BoxCollider>();
            var belly = root.transform.Find("Stationary/drivebase/22-03-01").GetComponent<BoxCollider>();
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            ski.enabled = true;
            belly.enabled = true;
            var robotBody = root.GetComponent<Rigidbody>();
            if (loaded)
            {
                foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                    if (body != robotBody) body.gameObject.SetActive(false);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    if (collider.attachedRigidbody == robotBody && collider is MeshCollider) collider.enabled = true;
                robotBody.isKinematic = false;
                robotBody.useGravity = false;
                robotBody.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ |
                    RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            }
            // The belly is at the compression limit, with 1 mm clearance to isolate the lower ski's catch.
            root.transform.position += Vector3.up * (0.15f - CompressionDepth + 0.001f - belly.bounds.min.y);
            Physics.SyncTransforms();
            var floor = new GameObject("Ski validation floor");
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.transform.position = Vector3.down * 0.05f;
            var floorCollider = floor.AddComponent<BoxCollider>();
            floorCollider.size = new Vector3(4f, 0.1f, 12f);
            floorCollider.sharedMaterial = carpet
                ? AssetDatabase.LoadAssetAtPath<PhysicMaterial>("Assets/Materials/PhysicsMaterials/Carpet.physicMaterial")
                : floorMaterial;
            var fuel = CreatePressureFuel(scene, new Vector3(0f, 0.075f, ski.bounds.min.z - 0.1f));
            fuel.constraints = RigidbodyConstraints.FreezePositionX;
            fuel.velocity = Vector3.forward * 0.5f;
            if (loaded)
            {
                fuel.solverIterations = 1;
                fuel.solverVelocityIterations = 1;
            }
            if (carpet) fuel.angularVelocity = Vector3.right * (0.5f / 0.075f);
            modifier.RegisterRobot(robot);
            modifier.RegisterFuel(0, fuel);
            modifier.Enable();
            modifier.PrepareStep(Vector3.down * 9.81f, CompressionDepth, 0.25f, FrictionMultiplier, glide);
            var physics = scene.GetPhysicsScene();
            float furthest = fuel.position.z;
            float rollback = 0f;
            bool sawLoadBoost = false;
            for (int i = 0; i < (carpet ? 600 : 200); i++)
            {
                modifier.PrepareStep(Vector3.down * 9.81f, CompressionDepth, 0.25f, FrictionMultiplier, glide);
                if (loaded && fuel.solverIterations >= 8 && fuel.solverVelocityIterations >= 4) sawLoadBoost = true;
                fuel.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration);
                if (loaded) robotBody.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration);
                physics.Simulate(0.005f);
                furthest = Mathf.Max(furthest, fuel.position.z);
                if (furthest > ski.bounds.min.z) rollback = Mathf.Max(rollback, furthest - fuel.position.z);
                if (fuel.position.y < 0.07f)
                    throw new InvalidOperationException($"Ski fixture floor penetration: glide={glide}, step={i}, y={fuel.position.y:F4}.");
            }
            if (checkReversal && (rollback > 0.015f || fuel.position.z < ski.bounds.max.z + 0.075f))
                throw new InvalidOperationException($"Rolling escape failed: furthest z={furthest:F4}, final z={fuel.position.z:F4}, pull-back={rollback:F4} m.");
            if (loaded)
            {
                if (!sawLoadBoost) throw new InvalidOperationException("Loaded fuel never received its adaptive solver boost.");
                modifier.UnregisterFuel(0);
                if (fuel.solverIterations != 1 || fuel.solverVelocityIterations != 1)
                    throw new InvalidOperationException("Loaded fuel solver settings were not restored after unregistering.");
            }
            return fuel.position.z;
        }
        finally
        {
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(floorMaterial);
        }
    }

    public static string RunCompressedEdges()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        Action<PhysicsScene, Unity.Collections.NativeArray<ModifiableContactPair>> observe = null;
        try
        {
            var fuel = new GameObject("Fuel contact probe");
            SceneManager.MoveGameObjectToScene(fuel, scene);
            var sphere = fuel.AddComponent<SphereCollider>();
            sphere.radius = 0.075f;
            fuel.AddComponent<Rigidbody>().isKinematic = true;
            var robot = new GameObject("Unlabelled robot geometry");
            SceneManager.MoveGameObjectToScene(robot, scene);
            var body = robot.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            var chassis = robot.AddComponent<BoxCollider>();
            chassis.isTrigger = true;
            chassis.center = Vector3.up * 0.09f;
            chassis.size = new Vector3(0.8f, 0.125f, 0.8f);
            var edge = CreateContactBox(robot.transform, new Vector3(0f, 0.06f, 0.07f), new Vector3(0.3f, 0.02f, 0.04f));
            var wall = CreateContactBox(robot.transform, new Vector3(0f, 0f, -0.1f), new Vector3(0.3f, 0.04f, 0.08f));
            var support = CreateContactBox(robot.transform, Vector3.up * 0.045f, new Vector3(0.3f, 0.02f, 0.3f));
            support.enabled = false;
            modifier.RegisterFuel(0, fuel.GetComponent<Rigidbody>());
            modifier.RegisterRobotBody(body, chassis);
            modifier.Enable();
            int sphereId = sphere.GetInstanceID(), edgeId = edge.GetInstanceID(), wallId = wall.GetInstanceID();
            float edgeUp = float.NaN, wallUp = float.NaN, edgeImpulse = float.NaN;
            var gate = new object();
            observe = (_, pairs) =>
            {
                foreach (var pair in pairs)
                {
                    bool fuelFirst = pair.colliderInstanceID == sphereId;
                    if (!fuelFirst && pair.otherColliderInstanceID != sphereId) continue;
                    int otherId = fuelFirst ? pair.otherColliderInstanceID : pair.colliderInstanceID;
                    if (otherId != edgeId && otherId != wallId) continue;
                    for (int i = 0; i < pair.contactCount; i++)
                    {
                        float up = Vector3.Dot(pair.GetNormal(i) * (fuelFirst ? -1f : 1f), Vector3.up);
                        lock (gate)
                        {
                            if (otherId == edgeId)
                            {
                                edgeUp = up;
                                edgeImpulse = pair.GetMaxImpulse(i);
                            }
                            else wallUp = up;
                        }
                    }
                }
            };
            Physics.ContactModifyEvent += observe;
            var physics = scene.GetPhysicsScene();
            float originalUp = 0f;
            for (int stage = 0; stage < 4; stage++)
            {
                support.enabled = stage == 1;
                edgeUp = wallUp = edgeImpulse = float.NaN;
                body.WakeUp();
                modifier.PrepareStep(Vector3.down * 9.81f, CompressionDepth, 0.25f, FrictionMultiplier);
                physics.Simulate(0.01f);
                if (float.IsNaN(edgeUp) || float.IsNaN(wallUp)) throw new InvalidOperationException("Missing edge or side-wall contact.");
                if (stage == 0) originalUp = edgeUp;
                bool clipped = stage == 1 || stage == 2;
                if (Mathf.Abs(edgeUp - originalUp) > 0.001f || Mathf.Abs(wallUp) > 0.001f || originalUp > 0.9f ||
                    float.IsNaN(edgeImpulse) || (clipped ? edgeImpulse != 0f : edgeImpulse <= 0f))
                    throw new InvalidOperationException($"Generic edge stage {stage}: edge up={edgeUp:F4}, side up={wallUp:F4}, impulse limit={edgeImpulse}, clipped={clipped}.");
            }
            return "PASS generic edge clipping: zero impulse only inside the loaded cap, native normals preserved, clipping expires after release; side wall stays rigid.";
        }
        finally
        {
            if (observe != null) Physics.ContactModifyEvent -= observe;
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static BoxCollider CreateContactBox(Transform parent, Vector3 center, Vector3 size)
    {
        var obj = new GameObject("Contact geometry");
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = center;
        var box = obj.AddComponent<BoxCollider>();
        box.size = size;
        return box;
    }

    public static string RunFriction()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        FrictionTrial(false, true, CompressionDepth, FrictionMultiplier);
        FrictionTrial(true, true, CompressionDepth, FrictionMultiplier);
        FrictionTrial(false, false, CompressionDepth, FrictionMultiplier);
        FrictionTrial(false, true, CompressionDepth, 1f);
        FrictionTrial(false, true, 0f, FrictionMultiplier);
        return "PASS static/sliding friction: 1x at zero, 3.5x at 2.5 cm, 6x at 5 cm; capped, repeatable, and restored on release. " +
            "Hopper, non-robot, disabled boost, and zero compression remain unchanged.";
    }

    private static void FrictionTrial(bool aboveChassis, bool registerRobot, float depth, float maximumMultiplier)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        var material = new PhysicMaterial("Friction validation")
        {
            staticFriction = 0.4f, dynamicFriction = 0.2f, bounciness = 0f,
            frictionCombine = PhysicMaterialCombine.Average
        };
        Action<PhysicsScene, Unity.Collections.NativeArray<ModifiableContactPair>> observe = null;
        try
        {
            var fuel = new GameObject("Friction probe fuel");
            SceneManager.MoveGameObjectToScene(fuel, scene);
            var sphere = fuel.AddComponent<SphereCollider>();
            sphere.radius = 0.075f;
            sphere.contactOffset = 0.0001f;
            sphere.sharedMaterial = material;
            var fuelBody = fuel.AddComponent<Rigidbody>();
            fuelBody.isKinematic = true;
            var box = CreateBox(scene, Vector3.up);
            box.GetComponent<BoxCollider>().sharedMaterial = material;
            // This observer also checks unregistered/disabled cases, whose contacts production
            // deliberately no longer requests. Opt the probe in independently of the modifier.
            box.GetComponent<BoxCollider>().hasModifiableContacts = true;
            box.constraints = RigidbodyConstraints.FreezeAll;
            BoxCollider ceiling = null;
            if (aboveChassis)
            {
                var marker = new GameObject("Lower chassis envelope");
                SceneManager.MoveGameObjectToScene(marker, scene);
                marker.transform.position = Vector3.down;
                ceiling = marker.AddComponent<BoxCollider>();
                ceiling.isTrigger = true;
                ceiling.size = Vector3.one * 0.1f;
            }
            modifier.RegisterFuel(0, fuelBody);
            if (registerRobot) modifier.RegisterRobotBody(box, ceiling);
            modifier.Enable();
            int robotId = box.GetInstanceID();
            var gate = new object();
            int observed = 0;
            string failure = null;
            float expectedScale = 1f;
            observe = (_, pairs) =>
            {
                foreach (var pair in pairs)
                {
                    if (pair.bodyInstanceID != robotId && pair.otherBodyInstanceID != robotId) continue;
                    lock (gate)
                    {
                        for (int i = 0; i < pair.contactCount; i++)
                        {
                            observed++;
                            float actualStatic = pair.GetStaticFriction(i);
                            float actualDynamic = pair.GetDynamicFriction(i);
                            if (Mathf.Abs(actualStatic - 0.4f * expectedScale) > 0.001f ||
                                Mathf.Abs(actualDynamic - 0.2f * expectedScale) > 0.001f)
                                failure = $"Friction scale {expectedScale:F2}: static={actualStatic:F4}, sliding={actualDynamic:F4}.";
                        }
                    }
                }
            };
            // Observe the contact buffer after the production callback has modified it.
            Physics.ContactModifyEvent += observe;
            float[] overlaps = { -0.00005f, 0.0125f, 0.025f, 0.05f, 0.05f, 0.06f, 0f };
            float[] scales = { 1f, 2.25f, 3.5f, 6f, 6f, 6f, 1f };
            var physics = scene.GetPhysicsScene();
            for (int i = 0; i < overlaps.Length; i++)
            {
                expectedScale = !aboveChassis && registerRobot && depth > 0f && maximumMultiplier > 1f ? scales[i] : 1f;
                observed = 0;
                box.position = Vector3.up * (0.115f - overlaps[i]);
                box.WakeUp();
                modifier.PrepareStep(Vector3.down * 9.81f, depth, 0.25f, maximumMultiplier);
                physics.Simulate(0.01f);
                if (observed == 0) throw new InvalidOperationException("Friction fixture generated no contacts.");
                if (failure != null) throw new InvalidOperationException(failure);
            }
        }
        finally
        {
            if (observe != null) Physics.ContactModifyEvent -= observe;
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void Check(string name, float actual, float expected, List<string> results)
    {
        if (Mathf.Abs(actual - expected) > 0.002f)
            throw new InvalidOperationException($"{name}: expected {expected:F4} m, got {actual:F4} m");
        results.Add($"PASS {name}: {actual:F4} m");
    }

    private static float Trial(Vector3 approach, Vector3 up, float depth, bool robot, bool robotFirst,
        bool disable, bool reenable = false, bool ccd = false, bool secondFuel = false)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        try
        {
            var physics = scene.GetPhysicsScene();
            if (physics.Equals(Physics.defaultPhysicsScene)) throw new InvalidOperationException("Expected isolated physics.");
            Rigidbody box = robotFirst ? CreateBox(scene, approach) : null;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
            var fuel = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            fuel.transform.position = Vector3.zero;
            var body = fuel.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            if (!box) box = CreateBox(scene, approach);

            var spheres = fuel.GetComponentsInChildren<SphereCollider>();
            var original = new bool[spheres.Length];
            for (int i = 0; i < spheres.Length; i++) original[i] = spheres[i].hasModifiableContacts;
            modifier.RegisterFuel(0, body);
            if (robot) modifier.RegisterRobotBody(box);
            if (secondFuel)
            {
                UnityEngine.Object.DestroyImmediate(box.GetComponent<BoxCollider>());
                var otherSphere = box.gameObject.AddComponent<SphereCollider>();
                otherSphere.radius = 0.04f;
                // Even a fuel body accidentally registered as a robot must keep fuel-fuel contacts rigid.
                modifier.RegisterRobotBody(box);
                modifier.RegisterFuel(1, box);
            }
            int ccdContacts = 0;
            Action<PhysicsScene, Unity.Collections.NativeArray<ModifiableContactPair>> observeCCD = (_, pairs) =>
                System.Threading.Interlocked.Add(ref ccdContacts, pairs.Length);
            if (ccd)
            {
                box.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                box.velocity = -approach * 20f;
                Physics.ContactModifyEventCCD += observeCCD;
            }
            modifier.Enable();
            modifier.PrepareStep(-up * 9.81f, depth, 0.25f);
            if (disable || reenable) modifier.Disable();
            if (reenable)
            {
                modifier.Enable();
                modifier.PrepareStep(-up * 9.81f, depth, 0.25f);
            }

            try
            {
                for (int i = 0; i < 250; i++)
                {
                    box.AddForce(-approach * 9.81f, ForceMode.Acceleration);
                    physics.Simulate(0.01f);
                }
                if (ccd && ccdContacts == 0) throw new InvalidOperationException("Continuous trial did not generate CCD contacts.");
            }
            finally
            {
                if (ccd) Physics.ContactModifyEventCCD -= observeCCD;
            }
            float distance = Vector3.Dot(box.position, approach);
            modifier.UnregisterFuel(0);
            for (int i = 0; i < spheres.Length; i++)
                if (spheres[i].hasModifiableContacts != original[i])
                    throw new InvalidOperationException("Fuel contact flags were not restored.");
            return distance;
        }
        finally
        {
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Rigidbody CreateBox(Scene scene, Vector3 approach)
    {
        var obj = new GameObject("Robot contact probe");
        SceneManager.MoveGameObjectToScene(obj, scene);
        obj.transform.position = approach * 0.18f;
        var collider = obj.AddComponent<BoxCollider>();
        collider.size = Vector3.one * 0.08f;
        collider.contactOffset = 0.0001f;
        var body = obj.AddComponent<Rigidbody>();
        body.mass = 98f;
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.solverIterations = 8;
        body.solverVelocityIterations = 2;
        return body;
    }

    private static float HopperTrial(float depth, out string railName, bool raiseCeiling = false)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var modifier = new RebuiltFuelContactModifier();
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Robots/2910/ReBlitz/2910B.prefab");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var robot = root.GetComponent<Games.Rebuilt.Robots.RebuiltRobotBase>();
            var chassis = root.GetComponent<BoxCollider>();
            float chassisTop = chassis.transform.TransformPoint(chassis.center + Vector3.up * chassis.size.y * 0.5f).y;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;

            BoxCollider rail = null;
            float widest = 0f;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider is BoxCollider box && !box.isTrigger && box.gameObject.activeInHierarchy &&
                    box.bounds.min.y > chassisTop + 0.05f && box.bounds.size.y < 0.08f &&
                    Mathf.Abs(Vector3.Dot(box.transform.up, Vector3.up)) > 0.999f)
                {
                    float width = Mathf.Max(box.bounds.size.x, box.bounds.size.z);
                    if (width > widest) { widest = width; rail = box; }
                }
                collider.enabled = false;
            }
            if (!rail) throw new InvalidOperationException("No horizontal upper rail found on 2910B.");
            rail.enabled = true;
            railName = rail.name;
            for (var parent = rail.transform.parent; parent; parent = parent.parent)
                railName = parent.name + "/" + railName;
            Vector3 target = rail.bounds.center;
            target.y = rail.bounds.min.y - 0.075f;
            // A deliberately permissive ceiling reproduces the original bug without changing the rail.
            if (raiseCeiling) chassis.size += Vector3.up * (2f * (rail.bounds.max.y - chassisTop + 0.1f));
            var upper = CreatePressureFuel(scene, target - Vector3.up * 0.01f);
            var lower = CreatePressureFuel(scene, target - Vector3.up * 0.16f);
            modifier.RegisterRobot(robot);
            modifier.RegisterFuel(0, upper);
            modifier.RegisterFuel(1, lower);
            modifier.Enable();
            modifier.PrepareStep(Vector3.down * 9.81f, depth, 0.25f);
            var physics = scene.GetPhysicsScene();
            for (int i = 0; i < 350; i++)
            {
                upper.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration);
                lower.AddForce(Vector3.up * 30f, ForceMode.Acceleration);
                physics.Simulate(0.01f);
            }
            float penetration = upper.position.y - target.y;
            if (depth == 0f && Mathf.Abs(penetration) > 0.002f)
                throw new InvalidOperationException($"Hopper fixture did not settle against the rail: {penetration:F4} m.");
            return penetration;
        }
        finally
        {
            modifier.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static Rigidbody CreatePressureFuel(Scene scene, Vector3 position)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Games/Rebuilt/Fuel.prefab");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        root.transform.position = position;
        var body = root.GetComponent<Rigidbody>();
        body.useGravity = false;
        body.solverIterations = 16;
        body.solverVelocityIterations = 8;
        body.constraints = RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ |
            RigidbodyConstraints.FreezeRotation;
        return body;
    }
}
