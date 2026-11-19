using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace Editor
{
    internal static class RobotScriptTemplates
    {
        private const string TemplateFolder = "Assets/Editor/RobotTemplates/";

        [MenuItem("Assets/Create/MoSimulator/RebuiltTemplate", false, 81)]
        private static void CreateRebuilt()
        {
            Create("RebuiltTemplate");
        }

        [MenuItem("Assets/Create/MoSimulator/RebuiltTurretTemplate", false, 82)]
        private static void CreateRebuiltTurret()
        {
            Create("RebuiltTurretTemplate");
        }

        [MenuItem("Assets/Create/MoSimulator/ReefscapeTemplate", false, 83)]
        private static void CreateReefscape()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(
                0, ScriptableObject.CreateInstance<CreateReefscapeScripts>(), "ReefscapeTemplate.cs",
                EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D, null);
        }

        private static void Create(string templateName)
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                TemplateFolder + templateName + ".cs.txt", templateName + ".cs");
        }

        internal static void CreateReefscapeScriptsAtPath(string pathName)
        {
            pathName = AssetDatabase.GenerateUniqueAssetPath(pathName);
            string robotName = Path.GetFileNameWithoutExtension(pathName).Replace(" ", "");
            string setpointName = robotName + "Setpoint";
            string setpointPath = Path.Combine(Path.GetDirectoryName(pathName), setpointName + ".cs")
                .Replace('\\', '/');

            // A differently named companion file would not match the generated type.
            if (File.Exists(setpointPath))
            {
                Debug.LogError("Cannot create robot: " + setpointPath +
                    " already exists. Choose a different robot name.");
                return;
            }

            string rootNamespace = CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(pathName);
            string robotSource = RenderTemplate("ReefscapeTemplate", robotName, setpointName, rootNamespace, pathName);
            string setpointSource = RenderTemplate("ReefscapeSetpointTemplate", robotName, setpointName, rootNamespace, pathName);

            // Import both files together so the robot's setpoint type already exists when compiling.
            AssetDatabase.StartAssetEditing();
            try
            {
                File.WriteAllText(setpointPath, setpointSource);
                File.WriteAllText(pathName, robotSource);
                AssetDatabase.ImportAsset(setpointPath);
                AssetDatabase.ImportAsset(pathName);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(pathName));
        }

        internal static void CreateFromTemplateAtPath(string templateName, string pathName)
        {
            string className = Path.GetFileNameWithoutExtension(pathName);
            string rootNamespace = CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(pathName);
            File.WriteAllText(pathName, RenderTemplate(templateName, className, className + "Setpoint", rootNamespace, pathName));
            AssetDatabase.ImportAsset(pathName);
        }

        // Modpack name is the folder under Mods/ (Mods/<ModpackName>/<Robot>/Robot.cs); falls back to "Robot".
        private static string GetModpackName(string scriptPath)
        {
            string[] parts = scriptPath.Replace('\\', '/').Split('/');
            int mods = System.Array.IndexOf(parts, "Mods");
            return mods >= 0 && mods + 1 < parts.Length - 1 ? parts[mods + 1] : "Robot";
        }

        private static string RenderTemplate(string templateName, string robotName,
            string setpointName, string rootNamespace, string scriptPath)
        {
            string source = File.ReadAllText(TemplateFolder + templateName + ".cs.txt")
                .Replace("#SCRIPTNAME#", robotName)
                .Replace("#SETPOINTTYPE#", setpointName)
                .Replace("#MODPACKNAME#", GetModpackName(scriptPath))
                .Replace("\r\n", "\n");
            string[] sections = source.Split(new[] { "    #ROOTNAMESPACEBEGIN#", "#ROOTNAMESPACEEND#" },
                System.StringSplitOptions.None);

            if (sections.Length != 3)
                throw new System.InvalidOperationException("Missing namespace markers in " + templateName);

            string body = sections[1].Trim('\n');
            if (string.IsNullOrEmpty(rootNamespace))
                return sections[0] + body + sections[2];

            return sections[0] + "namespace " + rootNamespace + "\n{\n    " +
                body.Replace("\n", "\n    ") + "\n}" + sections[2];
        }

        private sealed class CreateReefscapeScripts : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                CreateReefscapeScriptsAtPath(pathName);
            }
        }
    }
}
