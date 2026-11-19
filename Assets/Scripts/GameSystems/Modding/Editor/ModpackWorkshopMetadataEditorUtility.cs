using System.IO;
using GameSystems.Management;
using UnityEditor;
using UnityEngine;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    public static class ModpackWorkshopMetadataEditorUtility
    {
        public static ModpackWorkshopMetadata FindMetadata(BaseModpackSO modpack)
        {
            if (modpack == null)
            {
                return null;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:ModpackWorkshopMetadata"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ModpackWorkshopMetadata>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.Modpack == modpack)
                {
                    return asset;
                }
            }

            return null;
        }

        public static ModpackWorkshopMetadata CreateMetadata(BaseModpackSO modpack)
        {
            var modpackPath = modpack == null ? null : AssetDatabase.GetAssetPath(modpack);
            var folder = string.IsNullOrEmpty(modpackPath) ? null : Path.GetDirectoryName(modpackPath);
            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            var asset = ScriptableObject.CreateInstance<ModpackWorkshopMetadata>();
            asset.Modpack = modpack;
            asset.Title = modpack.ModpackName;
            asset.Description = modpack.Description;
            AssetDatabase.CreateAsset(asset,
                AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, $"{modpack.name}_WorkshopMetadata.asset")));
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
