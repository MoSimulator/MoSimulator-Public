using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameSystems.Management;
using RobotFramework;
using UnityEngine;

namespace GameSystems.Modding
{
    /// <summary>
    /// Checks shared by everything that opens mod bundles and assemblies, so they agree on what is already
    /// open. Unity refuses to open a bundle whose files are already open however the second copy was
    /// reached, so identity here is the bundle's name rather than the folder it was found in.
    /// </summary>
    public static class ModBundles
    {
        private const string RobotCountKeyPrefix = "ModRobotCount_";

        /// <summary>The open bundle with this name, or null. Names are the manifest's bundle id.</summary>
        public static AssetBundle FindLoaded(string bundleName)
        {
            if (string.IsNullOrWhiteSpace(bundleName))
            {
                return null;
            }

            return AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault(bundle =>
                bundle != null && string.Equals(bundle.name, bundleName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The already-loaded assembly a mod DLL would produce, or null. Mod DLLs are named for their assembly.</summary>
        public static Assembly FindLoadedAssembly(string dllFileName)
        {
            if (string.IsNullOrWhiteSpace(dllFileName))
            {
                return null;
            }

            var assemblyName = Path.GetFileNameWithoutExtension(dllFileName);
            return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Loads every asset in a bundle as a request of its own, reporting how far it has got. Loading a
        /// modpack in one call does the same work but has nothing to report until it is finished. Whatever
        /// loads the modpack next finds it all in memory.
        /// </summary>
        public static IEnumerator PreloadAssets(AssetBundle bundle, Action<float> onProgress)
        {
            var pending = bundle.GetAllAssetNames().Select(assetName => bundle.LoadAssetAsync(assetName)).ToList();
            var total = pending.Count;

            while (pending.Count > 0)
            {
                pending.RemoveAll(request => request.isDone);
                onProgress?.Invoke(1f - pending.Count / (float)total);
                yield return null;
            }
        }

        /// <summary>
        /// How many robots, variants included, a modpack held the last time it loaded, or 0 if it never has.
        /// A bundle cannot say what it holds until it is loaded, so the loading screen counts against this.
        /// </summary>
        public static int RememberedRobotCount(string modpackGuid)
        {
            return string.IsNullOrWhiteSpace(modpackGuid) ? 0 : PlayerPrefs.GetInt(RobotCountKeyPrefix + modpackGuid, 0);
        }

        public static void RememberRobotCount(BaseModpackSO modpack)
        {
            var robotCount = modpack.Robots?
                .Where(robot => robot != null)
                .Sum(robot => 1 + (robot.AlternateRobots?.Count ?? 0)) ?? 0;
            PlayerPrefs.SetInt(RobotCountKeyPrefix + modpack.GetGuid(), robotCount);
        }

        /// <summary>
        /// Points a freshly opened bundle's materials at the game's own copies of their shaders, where this
        /// platform needs it. A bundle packs its own copy of shaders such as URP Lit, compiled for its target;
        /// macOS and Linux bundles can contain copies that render pink regardless of which editor platform built them.
        /// Apply the repair on macOS and Linux for every build host. Materials with no matching shader in the game
        /// keep the bundled one.
        /// </summary>
        public static void UseGameShadersIfNeeded(AssetBundle bundle, ModpackManifest manifest, string modName)
        {
            var platform = Application.platform;
            var needsShaderRepair = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor ||
                                    platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;
            if (!needsShaderRepair || bundle == null)
            {
                return;
            }

            var host = string.IsNullOrEmpty(manifest?.buildHost) ? "an unknown platform" : manifest.buildHost;
            var rebound = BindToGameShaders(bundle, modName);
            Debug.Log($"Mod '{modName}' was built on {host}; bound {rebound} materials to the game's own shaders.");
        }

        /// <summary>Rebinds every material in the bundle to the game's shader of the same name. Returns how many changed.</summary>
        public static int BindToGameShaders(AssetBundle bundle, string modName)
        {
            var rebound = 0;
            var kept = new HashSet<string>();
            var materials = new HashSet<Material>(bundle.LoadAllAssets<Material>());
            // Older bundles include preloads only as metadata dependencies, so LoadAllAssets<Material>
            // does not expose their materials. Follow those references before repairing the shaders.
            foreach (var robot in bundle.LoadAllAssets<RobotMetadataSO>())
            {
                if (robot == null) continue;
                CollectMaterials(robot.RobotPreloadGamePiece, materials);
                if (robot.AlternateRobots == null) continue;
                foreach (var variant in robot.AlternateRobots)
                {
                    CollectMaterials(variant?.PreloadGamePiece, materials);
                }
            }

            foreach (var material in materials)
            {
                if (material == null || material.shader == null)
                {
                    continue;
                }

                var own = Shader.Find(material.shader.name);
                if (own == null)
                {
                    kept.Add(material.shader.name);
                }
                else if (own != material.shader)
                {
                    // Changing shaders resets the render queue to the shader default (opaque for URP Lit).
                    // Keep the mod's queue, including transparency, alpha testing and sorting offsets.
                    var renderQueue = material.renderQueue;
                    material.shader = own;
                    material.renderQueue = renderQueue;
                    rebound++;
                }
            }

            if (kept.Count > 0)
            {
                Debug.LogWarning($"Mod '{modName}' uses shaders the game does not have ({string.Join(", ", kept)}); " +
                                 "those keep the copies packed in the bundle and may render pink on this platform.");
            }

            return rebound;
        }

        private static void CollectMaterials(GameObject prefab, HashSet<Material> materials)
        {
            if (prefab == null) return;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                materials.UnionWith(renderer.sharedMaterials);
            }
        }
    }
}
