using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Discord.Sdk.Editor {
/// <summary>
/// Selects the correct Discord native library (Debug or Release) before each build.
/// Development builds use the Debug library; non-development (release) builds use Release.
/// In the Editor, the Debug library is always active.
/// </summary>
public class DiscordPluginSelector
  : IPreprocessBuildWithReport
  , IPostprocessBuildWithReport {
    public int callbackOrder => 0;

    // Runs on every domain reload so the Editor always references a native library that
    // exists on disk. Prefers the Debug lib (matching development builds) but SetPluginConfig
    // transparently falls back to Release when the Debug libs are missing from this checkout.
    [InitializeOnLoadMethod]
    private static void EnsureEditorPluginConfigured() {
        SetPluginConfig(true);
    }

    private static readonly string[] PluginFileNames = new[] {
        "discord_partner_sdk.dll",
        "libdiscord_partner_sdk.so",
        "libdiscord_partner_sdk.dylib",
        "discord_partner_sdk.prx",
        "discord_partner_sdk.aar",
        "discord_partner_sdk.framework",
    };

    private static readonly Dictionary<string, BuildTarget[]> PluginBuildTargets = new() {
        { "discord_partner_sdk.dll",
          new[] { BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64 } },
        { "libdiscord_partner_sdk.so", new[] { BuildTarget.StandaloneLinux64 } },
        { "libdiscord_partner_sdk.dylib", new[] { BuildTarget.StandaloneOSX } },
        { "discord_partner_sdk.prx", new BuildTarget[0] },
        { "discord_partner_sdk.aar", new[] { BuildTarget.Android } },
        { "discord_partner_sdk.framework", new[] { BuildTarget.iOS } },
    };

    private static readonly HashSet<string> EditorCompatiblePlugins = new() {
        "discord_partner_sdk.dll",
        "libdiscord_partner_sdk.so",
        "libdiscord_partner_sdk.dylib",
    };

    public void OnPreprocessBuild(BuildReport report) {
        bool isDevelopment = (report.summary.options & BuildOptions.Development) != 0;
        SetPluginConfig(isDevelopment);
    }

    // Release binaries are committed; Debug libs are gitignored (too large for GitHub).
    public void OnPostprocessBuild(BuildReport report) { SetPluginConfig(false); }

    private static void SetPluginConfig(bool useDebug) {
        string pluginsRoot = Path.GetFullPath("Packages/com.discord.partnersdk/Runtime/Plugins");

        if (!Directory.Exists(pluginsRoot)) {
            return;
        }

        var importers =
          PluginImporter.GetAllImporters()
            .Where(p => p.assetPath.StartsWith("Packages/com.discord.partnersdk/Runtime/Plugins"))
            .Where(p => IsDiscordNativePlugin(p.assetPath))
            .Where(p => p.assetPath.Contains("/Debug/") || p.assetPath.Contains("/Release/"))
            .ToArray();

        // Debug native libraries are gitignored (too large for GitHub), so many checkouts
        // only have the Release libraries. Falling back to Release keeps the Editor (and
        // builds) pointed at a native plugin that actually exists on disk; otherwise the
        // Editor is left referencing a missing DLL and DiscordRichPresence throws on init.
        if (useDebug && !importers.Any(p => p.assetPath.Contains("/Debug/"))) {
            Debug.LogWarning(
              "Discord SDK Debug native libraries were not found; using Release libraries instead.");
            useDebug = false;
        }

        // Disable first so we never have two same-named plugins enabled simultaneously.
        foreach (var importer in importers) {
            bool isDebugPlugin = importer.assetPath.Contains("/Debug/");
            bool shouldEnable = (useDebug && isDebugPlugin) || (!useDebug && !isDebugPlugin);
            if (!shouldEnable) {
                SetPluginEnabled(importer, false);
            }
        }

        foreach (var importer in importers) {
            bool isDebugPlugin = importer.assetPath.Contains("/Debug/");
            bool shouldEnable = (useDebug && isDebugPlugin) || (!useDebug && !isDebugPlugin);
            if (shouldEnable) {
                SetPluginEnabled(importer, true);
            }
        }
    }

    private static bool IsDiscordNativePlugin(string path) {
        string fileName = Path.GetFileName(path);
        return PluginFileNames.Any(n => n == fileName);
    }

    private static void SetPluginEnabled(PluginImporter importer, bool enabled) {
        bool changed = false;
        string fileName = Path.GetFileName(importer.assetPath);

        bool editorTarget = enabled && EditorCompatiblePlugins.Contains(fileName);
        if (importer.GetCompatibleWithEditor() != editorTarget) {
            importer.SetCompatibleWithEditor(editorTarget);
            changed = true;
        }

        BuildTarget[] supportedTargets = PluginBuildTargets.TryGetValue(fileName, out var targets)
          ? targets
          : System.Array.Empty<BuildTarget>();
        var supportedSet = new HashSet<BuildTarget>(supportedTargets);

        foreach (var target in new[] {
                     BuildTarget.StandaloneWindows,
                     BuildTarget.StandaloneWindows64,
                     BuildTarget.StandaloneLinux64,
                     BuildTarget.StandaloneOSX,
                     BuildTarget.Android,
                     BuildTarget.iOS,
                 }) {
            bool targetEnabled = enabled && supportedSet.Contains(target);
            if (importer.GetCompatibleWithPlatform(target) != targetEnabled) {
                importer.SetCompatibleWithPlatform(target, targetEnabled);
                changed = true;
            }
        }

        if (changed) {
            importer.SaveAndReimport();
        }
    }
}
}
