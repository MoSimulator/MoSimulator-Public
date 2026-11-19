using System;
using System.Linq;
using System.Reflection;
using GameSystems.Management;
using GameSystems.Settings;
using MoSimCore.Enums;
using RobotFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerSettings = GameSystems.Settings.PlayerSettings;

public static class RobotVariantSwitcherValidation
{
    public static string Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play mode.");

        var robotWithAlternate = AssetDatabase.FindAssets("t:RobotMetadataSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<RobotMetadataSO>)
            .FirstOrDefault(robot => robot && robot.RobotPrefab && robot.AlternateRobots != null &&
            robot.AlternateRobots.Any(variant => variant != null && variant.RobotPrefab));
        Require(robotWithAlternate, "No metadata asset with a valid alternate was found.");

        var alternateIndex = 1;
        while (robotWithAlternate.GetAlternateVariant(alternateIndex)?.RobotPrefab == null) alternateIndex++;
        var year = robotWithAlternate.GameYear;
        var type = robotWithAlternate.GameType;

        Require(RobotSpawnController.EditorValidateRobot(robotWithAlternate, 0, year, type, out _),
            "The main robot was rejected.");
        Require(RobotSpawnController.EditorValidateRobot(robotWithAlternate, alternateIndex, year, type, out _),
            "A valid alternate was rejected.");
        Require(!RobotSpawnController.EditorValidateRobot(null, 0, year, type, out _), "Missing metadata was accepted.");
        Require(!RobotSpawnController.EditorValidateRobot(robotWithAlternate, -1, year, type, out _),
            "A negative variant was accepted.");
        Require(!RobotSpawnController.EditorValidateRobot(robotWithAlternate, robotWithAlternate.AlternateRobots.Count + 1,
                year, type, out _), "An out-of-range variant was accepted.");
        Require(!RobotSpawnController.EditorValidateRobot(robotWithAlternate, 0, year + 1, type, out _),
            "A different game year was accepted.");

        var clone = UnityEngine.Object.Instantiate(robotWithAlternate);
        var invalidPrefab = new GameObject("Invalid robot prefab") { hideFlags = HideFlags.HideAndDontSave };
        invalidPrefab.SetActive(false);
        try
        {
            var variant = clone.GetAlternateVariant(alternateIndex);
            Require(!ReferenceEquals(variant, robotWithAlternate.GetAlternateVariant(alternateIndex)),
                "The temporary metadata clone shares its variant with the asset.");
            var prefabField = typeof(RobotMetadataSO.RobotVariant).GetField("robotPrefab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            prefabField.SetValue(variant, null);
            Require(!RobotSpawnController.EditorValidateRobot(clone, alternateIndex, year, type, out _),
                "An alternate without a prefab was accepted.");
            prefabField.SetValue(variant, invalidPrefab);
            Require(!RobotSpawnController.EditorValidateRobot(clone, alternateIndex, year, type, out _),
                "A prefab without required robot components was accepted.");
            invalidPrefab.AddComponent<Rigidbody>();
            invalidPrefab.AddComponent<PlayerInput>().actions =
                robotWithAlternate.GetAlternateVariant(alternateIndex).RobotPrefab.GetComponent<PlayerInput>().actions;
            Require(!RobotSpawnController.EditorValidateRobot(clone, alternateIndex, year, type, out _),
                "A prefab without a robot reset tag was accepted.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(invalidPrefab);
        }

        var original = new PlayerSettings { alliance = Alliance.Red, robotGuid = "original", robotVariantIndex = 0 };
        var expected = new Spawn { position = new Vector3(1, 2, 3), rotation = Quaternion.identity };
        original.GetSpawnListByYear(year).Add(new SavedSpawnpoint
        {
            key = $"{alternateIndex}:{robotWithAlternate.GetGuid()}",
            spawn = expected
        });
        var isolated = RobotSpawnController.EditorCreateSpawnSettings(original, robotWithAlternate, alternateIndex);
        Require(isolated.alliance == Alliance.Red && isolated.GetSpawnpoint(year)?.position == expected.position,
            "The alternate's saved spawn or alliance was not preserved.");
        isolated.GetSpawnpoint(year).position = Vector3.zero;
        var otherVariant = RobotSpawnController.EditorCreateSpawnSettings(original, robotWithAlternate, 0);
        Require(otherVariant.GetSpawnpoint(year) == null, "A different variant inherited the alternate's spawn.");

        var host = new GameObject("Spawn validation") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        try
        {
            var controller = host.AddComponent<RobotSpawnController>();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(RobotSpawnController).GetField("blueSpawnPoints", flags).SetValue(controller,
                new[] { host.transform });
            typeof(RobotSpawnController).GetField("gameYear", flags).SetValue(controller, year);
            var countBefore = otherVariant.GetSpawnListByYear(year).Count;
            var spawn = typeof(RobotSpawnController).GetMethod("GetSpawn", flags).Invoke(controller,
                new object[] { otherVariant, 0, robotWithAlternate.RobotPrefab, false });
            Require(spawn is Spawn && otherVariant.GetSpawnListByYear(year).Count == countBefore,
                "A temporary default spawn was persisted.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }

        otherVariant.GetSpawnListByYear(year).Add(new SavedSpawnpoint());
        Require(original.robotGuid == "original" && original.robotVariantIndex == 0 &&
                original.GetSpawnListByYear(year).Count == 1 &&
                original.GetSpawnListByYear(year)[0].spawn == expected && expected.position == new Vector3(1, 2, 3),
            "The original player settings were modified.");

        return $"PASS robot variant, prefab and temporary default-spawn validation ({robotWithAlternate.name}).";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
