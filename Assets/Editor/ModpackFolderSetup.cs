using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates Prefabs/Games/{Game}/Robots/Mods/{ModName} with a modpack asset, an asmdef, and
/// scaffolded robot folders. Open via Tools > Modding > Create Modpack Folder.
/// </summary>
public class ModpackFolderSetup : EditorWindow
{
    private const string GamesRoot = "Assets/Prefabs/Games";
    private const string TutorialCardTemplatePath = "Assets/Prefabs/UI/Components/TutorialCardTemplate.prefab";
    private const int TutorialCardCount = 3;

    private string[] games = Array.Empty<string>();
    private int gameIndex;
    private string modName = "";
    private string authorName = "";
    private const int MaxRobots = 6;
    private readonly string[] robotNumbers = Enumerable.Repeat("", MaxRobots).ToArray();
    private readonly bool[] robotTurrets = new bool[MaxRobots];
    private readonly List<string>[] robotAlternates = Enumerable.Range(0, MaxRobots).Select(_ => new List<string>()).ToArray();
    private readonly bool[] robotFoldouts = new bool[MaxRobots];
    private Vector2 scroll;
    private readonly string[] robotNames = Enumerable.Repeat("", MaxRobots).ToArray();

    [MenuItem("Tools/Modding/Create Modpack Folder")]
    private static void Open()
    {
        var window = GetWindow<ModpackFolderSetup>(true, "Create Modpack Folder");
        window.minSize = new Vector2(480, 380);
        window.RefreshGames();
    }

    private void OnEnable() => RefreshGames();

    private void RefreshGames()
    {
        games = AssetDatabase.IsValidFolder(GamesRoot)
            ? AssetDatabase.GetSubFolders(GamesRoot).Select(Path.GetFileName).ToArray()
            : Array.Empty<string>();
        gameIndex = Mathf.Clamp(gameIndex, 0, Mathf.Max(0, games.Length - 1));
    }

    private void OnGUI()
    {
        if (games.Length == 0)
        {
            EditorGUILayout.HelpBox($"No game folders found under {GamesRoot}.", MessageType.Warning);
            if (GUILayout.Button("Refresh")) RefreshGames();
            return;
        }

        gameIndex = EditorGUILayout.Popup("Game", gameIndex, games);
        modName = EditorGUILayout.TextField("Mod Name", modName);
        authorName = EditorGUILayout.TextField("Author", authorName);

        EditorGUILayout.LabelField("Robots (leave blank rows empty)");
        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (var i = 0; i < MaxRobots; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{i + 1}", GUILayout.Width(18));
            EditorGUILayout.LabelField("Team #", GUILayout.Width(50));
            robotNumbers[i] = EditorGUILayout.TextField(robotNumbers[i], GUILayout.Width(70));
            EditorGUILayout.LabelField("Team Name", GUILayout.Width(70));
            robotNames[i] = EditorGUILayout.TextField(robotNames[i]);
            if (games[gameIndex] == "Rebuilt")
            {
                EditorGUILayout.LabelField("Turret", GUILayout.Width(40));
                robotTurrets[i] = EditorGUILayout.Toggle(robotTurrets[i], GUILayout.Width(20));
            }
            EditorGUILayout.EndHorizontal();

            var alts = robotAlternates[i];
            robotFoldouts[i] = EditorGUILayout.Foldout(robotFoldouts[i], $"Alternates ({alts.Count})", true);
            if (robotFoldouts[i])
            {
                EditorGUI.indentLevel++;
                for (var a = 0; a < alts.Count; a++)
                {
                    EditorGUILayout.BeginHorizontal();
                    alts[a] = EditorGUILayout.TextField($"Alternate {a + 1}", alts[a]);
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                    {
                        alts.RemoveAt(a);
                        GUIUtility.ExitGUI();
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("+ Add Alternate", GUILayout.Width(120)))
                    alts.Add("");
                EditorGUI.indentLevel--;
            }
        }
        EditorGUILayout.EndScrollView();

        var trimmed = modName.Trim();
        var robots = GetRobots(out var rowError);
        var error = rowError ?? Validate(trimmed, robots);
        if (error != null && (trimmed.Length > 0 || robots.Count > 0))
            EditorGUILayout.HelpBox(error, MessageType.Error);

        using (new EditorGUI.DisabledScope(error != null))
        {
            if (GUILayout.Button("Create"))
                Create(games[gameIndex], trimmed, authorName.Trim(), robots);
        }
    }

    private struct RobotEntry
    {
        public int Number;
        public string Name;
        public string Id;
        public bool Turret;
        public List<string> Alternates;
    }

    private List<RobotEntry> GetRobots(out string error)
    {
        error = null;
        var result = new List<RobotEntry>();
        for (var i = 0; i < MaxRobots; i++)
        {
            var numberText = robotNumbers[i].Trim();
            var name = robotNames[i].Trim();
            if (numberText.Length == 0 && name.Length == 0) continue;

            var number = 0;
            if (numberText.Length > 0 && !int.TryParse(numberText, out number))
                error ??= $"Robot {i + 1}: team number must be a whole number.";
            if (name.Length == 0)
            {
                // Fall back to the number so the robot still gets a usable name.
                name = numberText;
            }

            result.Add(new RobotEntry
            {
                Number = number, Name = name, Id = ToIdentifier(name), Turret = robotTurrets[i],
                Alternates = robotAlternates[i].Select(a => a.Trim()).Where(a => a.Length > 0).ToList()
            });
        }

        return result;
    }

    private static string ToIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, "Robot");
        return sb.ToString();
    }

    private static string Validate(string mod, List<RobotEntry> robots)
    {
        if (mod.Length == 0) return "Enter a mod name.";
        if (mod.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Mod name contains invalid characters.";
        if (robots.Count > 6) return "A modpack can contain at most 6 robots.";
        if (robots.GroupBy(r => r.Id).Any(g => g.Count() > 1)) return "Robot names must be unique.";
        if (robots.Any(r => r.Alternates.GroupBy(ToIdentifier).Any(g => g.Count() > 1)))
            return "Alternate names must be unique per robot.";
        return null;
    }

    private static void Create(string game, string mod, string author, List<RobotEntry> robots)
    {
        var modFolder = $"{GamesRoot}/{game}/Robots/Mods/{mod}";
        var modpackPath = $"{modFolder}/{mod}.asset";

        var modpackType = FindType<ScriptableObject>(game, "BaseModpackSO");
        if (modpackType == null)
        {
            EditorUtility.DisplayDialog("No modpack type",
                $"Could not find a concrete BaseModpackSO subclass for game '{game}'.", "OK");
            return;
        }

        var metadataType = robots.Count > 0 ? FindType<ScriptableObject>(game, "RobotMetadataSO") : null;
        if (robots.Count > 0 && metadataType == null)
        {
            EditorUtility.DisplayDialog("No robot metadata type",
                $"Could not find a concrete RobotMetadataSO subclass for game '{game}'.", "OK");
            return;
        }

        foreach (var robot in robots)
        {
            if (AssetDatabase.IsValidFolder($"{modFolder}/{robot.Id}"))
            {
                EditorUtility.DisplayDialog("Already exists", $"{modFolder}/{robot.Id} already exists.", "OK");
                return;
            }
        }

        EnsureFolder(modFolder);

        var modpack = AssetDatabase.LoadAssetAtPath(modpackPath, modpackType) as ScriptableObject;
        if (modpack == null)
        {
            modpack = ScriptableObject.CreateInstance(modpackType);
            var so = new SerializedObject(modpack);
            so.FindProperty("modpackName").stringValue = mod;
            so.FindProperty("modpackVersion").stringValue = "1.0.0";
            so.FindProperty("authorName").stringValue = author;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(modpack, modpackPath);
        }

        CreateAsmdef(game, mod, modFolder);

        var metadataAssets = new List<ScriptableObject>();
        foreach (var robot in robots)
            metadataAssets.Add(CreateRobot(game, modFolder, robot, metadataType));

        AddToModpack(modpack, metadataAssets);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(modpack);
        Selection.activeObject = modpack;
    }

    private static void CreateAsmdef(string game, string mod, string modFolder)
    {
        var gameAsmdef = FindAsmdefGuid(game);
        if (gameAsmdef == null)
        {
            Debug.LogWarning($"No '{game}' assembly definition found; skipping asmdef (scripts will compile into Assembly-CSharp).");
            return;
        }

        var path = $"{modFolder}/{ToIdentifier(mod)}.asmdef";
        if (File.Exists(path)) return;

        var refs = new List<string> { gameAsmdef };
        foreach (var name in new[] { "RobotFramework", "MoSimCore", "MoSimLib", "GameSystems" })
        {
            var guid = FindAsmdefGuid(name);
            if (guid != null) refs.Add(guid);
        }

        var json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine($"    \"name\": \"{ToIdentifier(mod)}\",");
        json.AppendLine("    \"rootNamespace\": \"\",");
        json.AppendLine("    \"references\": [");
        json.AppendLine(string.Join(",\n", refs.Select(g => $"        \"GUID:{g}\"")));
        json.AppendLine("    ],");
        json.AppendLine("    \"includePlatforms\": [],");
        json.AppendLine("    \"excludePlatforms\": [],");
        json.AppendLine("    \"allowUnsafeCode\": false,");
        json.AppendLine("    \"overrideReferences\": false,");
        json.AppendLine("    \"precompiledReferences\": [],");
        json.AppendLine("    \"autoReferenced\": true,");
        json.AppendLine("    \"defineConstraints\": [],");
        json.AppendLine("    \"versionDefines\": [],");
        json.AppendLine("    \"noEngineReferences\": false");
        json.AppendLine("}");

        File.WriteAllText(path, json.ToString());
        AssetDatabase.ImportAsset(path);
    }

    private static string FindAsmdefGuid(string assemblyName)
    {
        foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset " + assemblyName))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == assemblyName) return guid;
        }

        return null;
    }

    private class RobotAssets
    {
        public GameObject Robot, Menu, Spawn;
        public List<GameObject> Cards = new List<GameObject>();
    }

    // Creates prefabs, tutorial cards, script and (Reefscape) setpoints for one robot or alternate.
    private static RobotAssets CreateRobotContent(string game, string folder, string id, string displayName, bool turret)
    {
        var cardsFolder = $"{folder}/TutorialCards";
        EnsureFolder(cardsFolder);

        var assets = new RobotAssets
        {
            Robot = CreateRobotPrefab("BaseRobot", $"{folder}/{id}.prefab", id),
            Menu = CreateRobotPrefab("BaseRobot_Menu", $"{folder}/{id}MainMenu.prefab", id + "MainMenu"),
            Spawn = CreateRobotPrefab("BaseRobot_Spawn", $"{folder}/{id}SpawnSelect.prefab", id + "SpawnSelect")
        };

        var template = AssetDatabase.LoadAssetAtPath<GameObject>(TutorialCardTemplatePath);
        for (var i = 1; i <= TutorialCardCount; i++)
            assets.Cards.Add(CreateTutorialCard(template, $"{cardsFolder}/TutorialCard {i}.prefab", $"TutorialCard {i}", displayName, i));

        CreateRobotScript(game, $"{folder}/{id}.cs", id, turret);
        if (game == "Reefscape") QueueSetpoints($"{folder}/Setpoints", id + "Setpoint");
        return assets;
    }

    private static void WriteVariant(SerializedProperty variant, string displayName, RobotAssets assets)
    {
        variant.FindPropertyRelative("displayName").stringValue = displayName;
        variant.FindPropertyRelative("robotPrefab").objectReferenceValue = assets.Robot;
        variant.FindPropertyRelative("mainMenuPrefab").objectReferenceValue = assets.Menu;
        variant.FindPropertyRelative("spawnSelectionPrefab").objectReferenceValue = assets.Spawn;
        var cardList = variant.FindPropertyRelative("tutorialCards");
        cardList.arraySize = assets.Cards.Count;
        for (var i = 0; i < assets.Cards.Count; i++)
            cardList.GetArrayElementAtIndex(i).objectReferenceValue = assets.Cards[i];
    }

    private static ScriptableObject CreateRobot(string game, string modFolder, RobotEntry robot, Type metadataType)
    {
        var folder = $"{modFolder}/{robot.Id}";
        var mainAssets = CreateRobotContent(game, folder, robot.Id, robot.Name, robot.Turret);

        var alternates = new List<(string name, string id, RobotAssets assets)>();
        foreach (var alt in robot.Alternates)
        {
            var altId = robot.Id + ToIdentifier(alt);
            alternates.Add((alt, altId, CreateRobotContent(game, $"{folder}/Alternates/{ToIdentifier(alt)}", altId, alt, robot.Turret)));
        }

        var metadata = ScriptableObject.CreateInstance(metadataType);
        var so = new SerializedObject(metadata);
        so.FindProperty("teamNumber").intValue = robot.Number;
        so.FindProperty("teamName").stringValue = robot.Name;
        WriteVariant(so.FindProperty("mainRobot"), robot.Name, mainAssets);

        var altList = so.FindProperty("alternateRobots");
        altList.arraySize = alternates.Count;
        for (var i = 0; i < alternates.Count; i++)
            WriteVariant(altList.GetArrayElementAtIndex(i), alternates[i].name, alternates[i].assets);
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(metadata, $"{folder}/{robot.Id}.asset");
        return metadata;
    }

    private static GameObject CreateRobotPrefab(string templateName, string path, string name)
    {
        var template = Resources.Load<GameObject>($"RobotTemplates/{templateName}");
        if (template == null)
        {
            Debug.LogWarning($"Resources/RobotTemplates/{templateName} not found; creating a blank prefab.");
            return CreateBlankPrefab(path, name);
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(template);
        try
        {
            instance.name = name;
            return PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private static GameObject CreateBlankPrefab(string path, string name)
    {
        var go = new GameObject(name);
        try
        {
            return PrefabUtility.SaveAsPrefabAsset(go, path);
        }
        finally
        {
            DestroyImmediate(go);
        }
    }

    private static GameObject CreateTutorialCard(GameObject template, string path, string name, string robotName, int index)
    {
        if (template == null) return CreateBlankPrefab(path, name);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(template);
        try
        {
            instance.name = name;
            var text = instance.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = $"{robotName} tutorial tip {index}";
            return PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }

    private const string PendingSetpointsKey = "ModpackFolderSetup.PendingSetpoints";

    // Setpoints shared by the Lynk and MechTechDragons mods.
    private static readonly string[] StandardSetpoints =
        { "BargePrep", "BargeScore", "HighAlgae", "L1", "L2", "L3", "L4", "LowAlgae", "Intake", "Stow" };

    private static void QueueSetpoints(string setpointsFolder, string typeName)
    {
        EnsureFolder(setpointsFolder);
        var pending = SessionState.GetString(PendingSetpointsKey, "");
        SessionState.SetString(PendingSetpointsKey, pending + setpointsFolder + "|" + typeName + "\n");
    }

    // The setpoint type only exists after the new script compiles, so assets are created after the reload.
    [InitializeOnLoadMethod]
    private static void CreatePendingSetpoints()
    {
        var pending = SessionState.GetString(PendingSetpointsKey, "");
        if (pending.Length == 0) return;

        var remaining = new StringBuilder();
        foreach (var entry in pending.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('|');
            var type = TypeCache.GetTypesDerivedFrom<ScriptableObject>()
                .FirstOrDefault(t => !t.IsAbstract && t.Name == parts[1]);
            if (type == null)
            {
                remaining.Append(entry).Append('\n');
                continue;
            }

            foreach (var name in StandardSetpoints)
            {
                var path = $"{parts[0]}/{name}.asset";
                if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(type), path);
            }
        }

        SessionState.SetString(PendingSetpointsKey, remaining.ToString());
        AssetDatabase.SaveAssets();
    }

    private static void CreateRobotScript(string game, string path, string className, bool rebuiltTurret)
    {
        switch (game)
        {
            case "Reefscape":
                global::Editor.RobotScriptTemplates.CreateReefscapeScriptsAtPath(path);
                return;
            case "Rebuilt":
                global::Editor.RobotScriptTemplates.CreateFromTemplateAtPath(
                    rebuiltTurret ? "RebuiltTurretTemplate" : "RebuiltTemplate", path);
                return;
        }

        var baseType = FindType<MonoBehaviour>(game, $"{game}RobotBase", true);
        var source = baseType == null
            ? $"using UnityEngine;\n\npublic class {className} : MonoBehaviour\n{{\n}}\n"
            : $"using {baseType.Namespace};\n\npublic class {className} : {baseType.Name}\n{{\n}}\n";
        File.WriteAllText(path, source);
        AssetDatabase.ImportAsset(path);
    }

    private static void AddToModpack(ScriptableObject modpack, List<ScriptableObject> metadataAssets)
    {
        if (metadataAssets.Count == 0) return;

        var so = new SerializedObject(modpack);
        var list = so.FindProperty("robots");
        foreach (var metadata in metadataAssets)
        {
            if (list.arraySize >= 6)
            {
                Debug.LogWarning("Modpack already has 6 robots; not adding " + metadata.name);
                break;
            }

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = metadata;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(modpack);
    }

    private static Type FindType<T>(string game, string baseName, bool matchByName = false)
    {
        var candidates = TypeCache.GetTypesDerivedFrom<T>()
            .Where(t => !t.IsAbstract && (matchByName ? t.Name == baseName : InheritsFrom(t, baseName)))
            .ToList();

        return candidates.FirstOrDefault(t => (t.Namespace ?? "").Split('.').Contains(game, StringComparer.OrdinalIgnoreCase))
               ?? candidates.FirstOrDefault(t => t.FullName != null && t.FullName.IndexOf(game, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool InheritsFrom(Type type, string baseName)
    {
        for (var t = type.BaseType; t != null; t = t.BaseType)
            if (t.Name == baseName) return true;
        return false;
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
