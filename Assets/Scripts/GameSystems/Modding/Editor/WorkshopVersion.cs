using System.Text.RegularExpressions;
using GameSystems.Management;
using UnityEditor;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    public enum VersionBump
    {
        Patch,
        Minor,
        Major
    }

    /// <summary>Semantic version checks and bumps for the modpack's version field.</summary>
    public static class WorkshopVersion
    {
        private static readonly Regex Pattern = new(@"^(\d+)\.(\d+)\.(\d+)$");

        public static bool IsValid(string version)
        {
            return !string.IsNullOrWhiteSpace(version) && Pattern.IsMatch(version.Trim());
        }

        public static bool TryBump(string version, VersionBump bump, out string result)
        {
            result = null;
            var match = Pattern.Match(version?.Trim() ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            var patch = int.Parse(match.Groups[3].Value);
            switch (bump)
            {
                case VersionBump.Major:
                    result = $"{major + 1}.0.0";
                    break;
                case VersionBump.Minor:
                    result = $"{major}.{minor + 1}.0";
                    break;
                default:
                    result = $"{major}.{minor}.{patch + 1}";
                    break;
            }

            return true;
        }

        /// <summary>Bumps the modpack asset's version in place. Returns false when the current one is not x.y.z.</summary>
        public static bool BumpModpack(BaseModpackSO modpack, VersionBump bump)
        {
            if (modpack == null || !TryBump(modpack.ModpackVersion, bump, out var next))
            {
                return false;
            }

            SetVersion(modpack, next);
            return true;
        }

        public static void SetVersion(BaseModpackSO modpack, string version)
        {
            var serialized = new SerializedObject(modpack);
            serialized.FindProperty("modpackVersion").stringValue = version;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
