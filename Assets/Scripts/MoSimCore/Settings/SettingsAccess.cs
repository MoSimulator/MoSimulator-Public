using System;
using System.Collections.Generic;
using MoSimCore.GameSelection;
using UnityEngine;

namespace MoSimCore.Settings
{
    public static class SettingsAccess
    {
        public static Func<string, bool, string> GetBindingOverrides { get; set; }
        public static Action<string, bool, string> SetBindingOverrides { get; set; }
        public static Action<string> ClearBindingOverrides { get; set; }
        public static Func<int> GetSelectedControllerRebinding { get; set; }
        public static Func<int, float> GetDriveActionKeyPercent { get; set; }
        public static Func<int> GetActionKeyMode { get; set; }

        public static string ReadBindingOverrides(bool isOtherControlsAsset) =>
            ReadBindingOverrides(ResolveSelectedGameGuid(), isOtherControlsAsset);

        public static string ReadBindingOverrides(string gameGuid, bool isOtherControlsAsset) =>
            GetBindingOverrides?.Invoke(gameGuid, isOtherControlsAsset) ?? "";

        public static void WriteBindingOverrides(bool isOtherControlsAsset, string rebinds) =>
            WriteBindingOverrides(ResolveSelectedGameGuid(), isOtherControlsAsset, rebinds);

        public static void WriteBindingOverrides(string gameGuid, bool isOtherControlsAsset, string rebinds) =>
            SetBindingOverrides?.Invoke(gameGuid, isOtherControlsAsset, rebinds);

        public static void ClearBindingOverridesForSelectedGame() =>
            ClearBindingOverrides?.Invoke(ResolveSelectedGameGuid());

        public static int ReadSelectedControllerRebinding() =>
            GetSelectedControllerRebinding?.Invoke() ?? 0;

        public static float ReadDriveActionKeyPercent(int index) =>
            Mathf.Clamp01(GetDriveActionKeyPercent?.Invoke(index) ?? 1f);

        public static int ReadActionKeyMode() =>
            GetActionKeyMode?.Invoke() ?? 0;

        
        public static string SanitizeBindingOverridesJson(string json, out int droppedCount)
        {
            droppedCount = 0;
            if (string.IsNullOrEmpty(json)) return json;

            BindingOverrideList parsed;
            try { parsed = JsonUtility.FromJson<BindingOverrideList>(json); }
            catch { return json; }
            if (parsed?.bindings == null) return json;

            var kept = new List<BindingOverrideEntry>(parsed.bindings.Count);
            foreach (var entry in parsed.bindings)
            {
                if (entry == null || !Guid.TryParse(entry.id, out _)) { droppedCount++; continue; }
                kept.Add(entry);
            }
            if (droppedCount == 0) return json;
            return JsonUtility.ToJson(new BindingOverrideList { bindings = kept });
        }

        [Serializable]
        private class BindingOverrideList { public List<BindingOverrideEntry> bindings; }

        [Serializable]
        private class BindingOverrideEntry
        {
            public string action;
            public string id;
            public string path;
            public string interactions;
            public string processors;
        }

public static string ResolveSelectedGameGuid()
{
    var selected = SelectedGameManager.Instance?.SelectedGame;
    if (selected != null)
        return selected.GetGuid();

    return SelectedGameManager.PendingSelectedGameGuid ?? "";
}
    }
}
