using System;
using System.Collections.Generic;
using System.IO;
using GameSystems.Management;
using UnityEngine;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    /// <summary>
    /// Copies a modpack's build into the folder that gets uploaded. The layout is one folder per platform,
    /// each holding modpack.json, the asset bundle and the mod DLL, which is what ModManager and the
    /// Workshop browser read back.
    /// </summary>
    public static class ModpackWorkshopStaging
    {
        // Build-time dependency listings: not read at runtime, and they record the author's asset paths.
        private const string BuildManifestExtension = ".manifest";

        public static bool PrepareStagingFolder(BaseModpackSO modpack, string sourceFolder, out string stagingFolder)
        {
            stagingFolder = ModBuildUtility.GetStagingPath(modpack);
            if (string.IsNullOrEmpty(stagingFolder))
            {
                Debug.LogWarning("Unable to determine the staging folder: modpack has no name.");
                return false;
            }

            if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder))
            {
                Debug.LogWarning($"Build folder not found: {sourceFolder}. Build the modpack first.");
                return false;
            }

            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, true);
            }

            CopyDirectory(new DirectoryInfo(sourceFolder), stagingFolder);
            return true;
        }

        /// <summary>Returns what is wrong with a staged folder; empty when it is ready to upload.</summary>
        public static List<string> Validate(BaseModpackSO modpack, string stagingFolder)
        {
            var problems = new List<string>();
            if (string.IsNullOrEmpty(stagingFolder) || !Directory.Exists(stagingFolder))
            {
                problems.Add("Nothing is staged yet. Build and stage the modpack first.");
                return problems;
            }

            foreach (var platform in ModBuildUtility.PlatformFolderNames)
            {
                var platformFolder = Path.Combine(stagingFolder, platform);
                var manifestPath = Path.Combine(platformFolder, "modpack.json");
                if (!File.Exists(manifestPath))
                {
                    problems.Add($"Staged folder has no {platform} build. Rebuild with all platforms.");
                    continue;
                }

                ModpackManifest manifest;
                try
                {
                    manifest = JsonUtility.FromJson<ModpackManifest>(File.ReadAllText(manifestPath));
                }
                catch (Exception exception)
                {
                    problems.Add($"{platform}/modpack.json is unreadable: {exception.Message}");
                    continue;
                }

                if (manifest == null || string.IsNullOrEmpty(manifest.id))
                {
                    problems.Add($"{platform}/modpack.json has no bundle id.");
                    continue;
                }

                if (manifest.modpack == null || manifest.modpack.metadataGuid != modpack.GetGuid())
                {
                    problems.Add($"{platform} build is for a different modpack or an older name/author/game. Rebuild.");
                }

                if (manifest.version != modpack.ModpackVersion)
                {
                    problems.Add($"{platform} build is version '{manifest.version}' but the modpack is '{modpack.ModpackVersion}'. Rebuild.");
                }

                if (!File.Exists(Path.Combine(platformFolder, manifest.id)))
                {
                    problems.Add($"{platform} is missing its asset bundle '{manifest.id}'.");
                }

                if (!string.IsNullOrEmpty(manifest.modDll) && !File.Exists(Path.Combine(platformFolder, manifest.modDll)))
                {
                    problems.Add($"{platform} is missing its mod DLL '{manifest.modDll}'.");
                }
            }

            return problems;
        }

        private static void CopyDirectory(DirectoryInfo source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in source.GetFiles())
            {
                if (string.Equals(file.Extension, BuildManifestExtension, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                file.CopyTo(Path.Combine(destination, file.Name), true);
            }

            foreach (var subDirectory in source.GetDirectories())
            {
                CopyDirectory(subDirectory, Path.Combine(destination, subDirectory.Name));
            }
        }
    }
}
