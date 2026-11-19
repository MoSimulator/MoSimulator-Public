using System;
using System.Reflection;
using Games.Reefscape.GameManagement;
using Games.Reefscape.GamePieceSystem;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Interfaces;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run with: python Tools/unitylive.py --json run GamePieceSymmetryValidation.Run
public static class GamePieceSymmetryValidation
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type ControllerType = typeof(GamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>);

    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previousManager = BaseGameManager.Instance;
        var instance = typeof(BaseGameManager).GetProperty("Instance");
        var data = ScriptableObject.CreateInstance<ReefscapeGamePieceData>();
        try
        {
            var root = NewObject("Symmetry validation", scene);
            root.layer = LayerMask.NameToLayer("Robot");
            root.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(23, 41, 17));
            var managerObject = NewObject("Validation manager", scene);
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent<ReefscapeGameManager>();
            var timer = managerObject.AddComponent<ReefscapeTimerManager>();
            typeof(BaseTimerManager).GetProperty("CurrentRobotState").SetValue(timer, RobotState.Enabled);
            typeof(BaseGameManager).GetField("TimerManager", PrivateInstance).SetValue(manager, timer);
            instance.SetValue(null, manager);

            var pieceObject = NewObject("Validation piece", scene);
            pieceObject.transform.SetParent(root.transform, false);
            var body = pieceObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.angularDrag = 0;
            pieceObject.AddComponent<BoxCollider>();
            var controller = pieceObject.AddComponent<ReefscapeGamePieceController>();
            var piece = new ReefscapeGamePiece();
            piece.Initialize(data, pieceObject);
            piece.transform = pieceObject.transform;
            piece.rigidbody = body;
            piece.owner = root;
            var colliders = NewObject("Validation colliders", scene).transform;
            colliders.SetParent(root.transform, false);
            var offset = new Vector3(.03f, .04f, .05f);
            var colliderRotation = Quaternion.Euler(11, 22, 33);
            Set(controller, "gamePieceData", data);
            Set(controller, "gamePiece", piece);
            Set(controller, "_gamePieceWorld", root.transform);
            Set(controller, "_colliderParent", colliders);
            Set(controller, "_startPosition", offset);
            Set(controller, "_startRotation", colliderRotation);
            Set(controller, "_isControlled", true);
            int cases = 0;
            foreach (SymmetryType symmetry in Enum.GetValues(typeof(SymmetryType)))
            {
                data.hasSymmetry = true;
                data.symmetryType = symmetry;
                var axis = symmetry == SymmetryType.ZAxis || symmetry == SymmetryType.XYZAxis ? Vector3.forward
                    : symmetry == SymmetryType.YAxis || symmetry == SymmetryType.YZAxis || symmetry == SymmetryType.None
                        ? Vector3.up : Vector3.right;
                CheckCase(scene, root.transform, piece, controller, colliders, data, axis,
                    symmetry != SymmetryType.None, offset, colliderRotation);
                cases++;
            }
            data.hasSymmetry = false;
            data.symmetryType = SymmetryType.YAxis;
            CheckCase(scene, root.transform, piece, controller, colliders, data, Vector3.up, false, offset, colliderRotation);
            CheckReleases(scene, root.transform, piece, controller, colliders);
            return $"PASS: {cases + 1} symmetry configurations; animation, force direction, handoff alignment; 16 target-relative releases with recoil; 16 continued releases across starting flips; compensated local rotation tracking, speed cap, zero duration and missing world.";
        }
        finally
        {
            instance.SetValue(null, previousManager);
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(data);
        }
    }

    private static void CheckCase(Scene scene, Transform target, ReefscapeGamePiece piece,
        ReefscapeGamePieceController controller, Transform colliders, ReefscapeGamePieceData data,
        Vector3 axis, bool equivalent, Vector3 offset, Quaternion colliderRotation)
    {
        var initial = target.rotation * Quaternion.AngleAxis(170, axis);
        var expected = equivalent ? target.rotation * Quaternion.AngleAxis(180, axis) : target.rotation;
        var label = $"{data.HasSymmetry}/{data.SymmetryType}";
        foreach (var speeds in new[] { new Vector2(100, 90), new Vector2(100, 0), new Vector2(0, 90) })
        {
            Reset(piece, target, initial);
            PieceController.MoveTo(piece, data, target, speeds.x, speeds.y, out _, out _);
            if (speeds.x == 0 || speeds.y == 0)
                Rotation(piece.transform.rotation, expected, label + " instant animation");
            else if (Quaternion.Angle(piece.transform.rotation, expected) >= Quaternion.Angle(initial, expected))
                throw new Exception(label + " animation moved away from selected orientation");
            Rotation(piece.rigidbody.rotation, piece.transform.rotation, label + " animation rigidbody");
        }

        Reset(piece, target, initial);
        PieceController.MoveToBreakable(piece, target, 100, 1, 1000, .01f, 1, 10, true, false,
            Vector3.one, false, false, false, out _, out _);
        scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        var turn = Vector3.Dot(piece.rigidbody.angularVelocity, target.rotation * axis);
        if (equivalent ? turn <= 0 : turn >= 0)
            throw new Exception(label + " force turned in the wrong direction: " + turn);

        Reset(piece, target, initial);
        ControllerType.GetMethod("Controlled", PrivateInstance).Invoke(controller, new object[] { true });
        Rotation(piece.transform.rotation, initial, label + " smooth handoff");
        Set(controller, "_moving", false);
        for (int i = 0; i < 3; i++)
        {
            ControllerType.GetMethod("LateUpdate", PrivateInstance).Invoke(controller, null);
            Rotation(piece.transform.rotation, expected, label + " handoff");
            Rotation(piece.rigidbody.rotation, expected, label + " handoff rigidbody");
            Rotation(colliders.rotation, expected * colliderRotation, label + " collider rotation");
            if (Vector3.Distance(colliders.position, target.position + expected * offset) > .0001f
                || Vector3.Distance(piece.transform.position, target.position) > .0001f
                || Vector3.Distance(piece.rigidbody.position, target.position) > .0001f)
                throw new Exception(label + " handoff position mismatch");
        }
    }

    private static void Reset(ReefscapeGamePiece piece, Transform target, Quaternion rotation)
    {
        PieceController.ClearState(piece.gameObject.GetInstanceID());
        piece.transform.SetPositionAndRotation(target.position + Vector3.right * .1f, rotation);
        piece.rigidbody.position = piece.transform.position;
        piece.rigidbody.rotation = rotation;
        piece.rigidbody.velocity = Vector3.zero;
        piece.rigidbody.angularVelocity = Vector3.zero;
    }

    private static void CheckReleases(Scene scene, Transform target, ReefscapeGamePiece piece,
        ReefscapeGamePieceController controller, Transform colliders)
    {
        var world = NewObject("Release world", scene).transform;
        world.rotation = Quaternion.Euler(71, 19, 38);
        Set(controller, "_gamePieceWorld", world);
        Set(controller, "_colliders", Array.Empty<Collider>());
        Set(controller, "_startingMass", 2f);
        Set(controller, "_startingDrag", 0f);
        Set(controller, "_startingAngularDrag", 0f);
        var robotBody = target.gameObject.AddComponent<Rigidbody>();
        robotBody.useGravity = false;
        robotBody.mass = 4;
        robotBody.freezeRotation = true;
        var force = new Vector3(1, 2, 3);
        var orientations = new[] { Quaternion.identity, Quaternion.Euler(180, 0, 0),
            Quaternion.Euler(0, 180, 0), Quaternion.Euler(0, 0, 180) };
        foreach (ForceMode mode in Enum.GetValues(typeof(ForceMode)))
        foreach (var orientation in orientations)
        {
            Prepare(orientation);
            var worldForce = target.TransformDirection(force);
            piece.rigidbody.velocity = Vector3.one; // Release must still clear prior motion.
            controller.Release(force, mode);
            piece.rigidbody.useGravity = false;
            scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Vector(piece.rigidbody.velocity, Velocity(worldForce, piece.rigidbody.mass, mode), "release direction/magnitude " + mode);
            Vector(robotBody.velocity, mode == ForceMode.VelocityChange ? Vector3.zero
                : Velocity(-worldForce, robotBody.mass, mode), "release recoil " + mode);
            if (piece.transform.parent != world || colliders.parent != piece.transform)
                throw new Exception("Release did not detach the piece and restore its colliders");
        }

        foreach (ForceMode mode in Enum.GetValues(typeof(ForceMode)))
        foreach (var orientation in orientations)
        {
            Prepare(orientation);
            var worldForce = target.TransformDirection(force);
            var release = controller.ContinuedRelease(force, 10, 10, mode).Value;
            release.MoveNext();
            piece.rigidbody.useGravity = false;
            scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Vector(piece.rigidbody.velocity, Velocity(worldForce, piece.rigidbody.mass, mode),
                "continued release ignores starting flip " + mode);
            Vector(robotBody.velocity, Vector3.zero, "continued release recoil unchanged " + mode);
            (release as IDisposable)?.Dispose();
        }

        Prepare(orientations[2]);
        var releaseRotation = piece.transform.rotation;
        var releaseWorldForce = target.TransformDirection(force);
        var continued = controller.ContinuedRelease(force, 10, 10, ForceMode.VelocityChange).Value;
        var expected = releaseWorldForce;
        continued.MoveNext();
        piece.rigidbody.useGravity = false;
        scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        Vector(piece.rigidbody.velocity, expected, "continued release compensated starting force");
        Vector(robotBody.velocity, Vector3.zero, "continued release recoil unchanged");

        piece.transform.rotation = target.rotation * orientations[1];
        piece.rigidbody.rotation = piece.transform.rotation;
        var rotationSinceRelease = piece.transform.rotation * Quaternion.Inverse(releaseRotation);
        expected += rotationSinceRelease * releaseWorldForce;
        continued.MoveNext(); // WaitForFixedUpdate
        continued.MoveNext(); // Next application follows rotation since release, compensating the starting flip.
        scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        Vector(piece.rigidbody.velocity, expected, "continued release rotation tracking");

        expected = Vector3.right * 11;
        piece.rigidbody.velocity = expected;
        continued.MoveNext();
        continued.MoveNext();
        scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        Vector(piece.rigidbody.velocity, expected, "continued release speed cap");
        (continued as IDisposable)?.Dispose();

        Prepare(orientations[3]);
        continued = controller.ContinuedRelease(force, 0, 10, ForceMode.VelocityChange).Value;
        if (continued.MoveNext()) throw new Exception("Zero-duration release did not finish");
        piece.rigidbody.useGravity = false;
        scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
        Vector(piece.rigidbody.velocity, Vector3.zero, "zero-duration continued release");

        Set(controller, "_gamePieceWorld", null);
        continued = controller.ContinuedRelease(force, 10, 10).Value;
        if (continued.MoveNext()) throw new Exception("Missing-world release did not finish");

        void Prepare(Quaternion orientation)
        {
            piece.transform.SetParent(target, false);
            piece.owner = target.gameObject;
            colliders.SetParent(target, false);
            Reset(piece, target, target.rotation * orientation);
            robotBody.velocity = Vector3.zero;
            robotBody.angularVelocity = Vector3.zero;
        }
    }

    private static Vector3 Velocity(Vector3 force, float mass, ForceMode mode)
    {
        var change = mode == ForceMode.Impulse || mode == ForceMode.Force ? force / mass : force;
        return mode == ForceMode.Force || mode == ForceMode.Acceleration ? change * Time.fixedDeltaTime : change;
    }

    private static void Vector(Vector3 actual, Vector3 expected, string label)
    {
        if (Vector3.Distance(actual, expected) > .001f)
            throw new Exception($"{label}: expected {expected}, got {actual}");
    }

    private static GameObject NewObject(string name, Scene scene)
    {
        var obj = new GameObject(name);
        SceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }

    private static void Set(object controller, string name, object value) =>
        ControllerType.GetField(name, PrivateInstance).SetValue(controller, value);

    private static void Rotation(Quaternion actual, Quaternion expected, string label)
    {
        if (Quaternion.Angle(actual, expected) > .1f) throw new Exception(label + " rotation mismatch");
    }
}
