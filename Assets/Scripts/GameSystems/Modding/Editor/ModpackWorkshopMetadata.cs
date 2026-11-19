using GameSystems.Management;
using UnityEngine;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    /// <summary>
    /// Steam Workshop listing details for a modpack. Lives beside the modpack asset and is created from the uploader window.
    /// </summary>
    public class ModpackWorkshopMetadata : ScriptableObject
    {
        [SerializeField] private BaseModpackSO modpack;
        [SerializeField] private string title;
        [SerializeField, TextArea(3, 10)] private string description;
        [SerializeField, Tooltip("Steam rejects preview images over 1 MB.")]
        private Texture2D previewImage;
        [SerializeField, Tooltip("Extra tags. The game tag is always added automatically.")]
        private string[] tags = new string[0];
        [SerializeField] private WorkshopVisibility visibility = WorkshopVisibility.Public;
        [SerializeField, Tooltip("Set by the uploader after the item is first created. Clear it to publish as a new item.")]
        private ulong publishedFileId;

        [SerializeField, Tooltip("Raise the modpack patch version each time the uploader builds, so every upload gets a new version.")]
        private bool autoBumpPatchOnBuild;

        public bool AutoBumpPatchOnBuild => autoBumpPatchOnBuild;

        public BaseModpackSO Modpack
        {
            get => modpack;
            set => modpack = value;
        }

        public string Title
        {
            get => title;
            set => title = value;
        }

        public string Description
        {
            get => description;
            set => description = value;
        }

        public Texture2D PreviewImage
        {
            get => previewImage;
            set => previewImage = value;
        }

        public string[] Tags
        {
            get => tags;
            set => tags = value ?? new string[0];
        }

        public WorkshopVisibility Visibility
        {
            get => visibility;
            set => visibility = value;
        }

        public ulong PublishedFileId
        {
            get => publishedFileId;
            set => publishedFileId = value;
        }
    }

    public enum WorkshopVisibility
    {
        Public,
        FriendsOnly,
        Private
    }
}
