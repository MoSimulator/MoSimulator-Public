using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Steam;
using Steamworks;
using UnityEditor;
using SteamEditor = Steamworks.Ugc.Editor;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    public readonly struct PublishOutcome
    {
        public bool Success { get; }
        public ulong FileId { get; }
        public string Message { get; }

        public PublishOutcome(bool success, ulong fileId, string message)
        {
            Success = success;
            FileId = fileId;
            Message = message;
        }
    }

    /// <summary>Creates or updates a modpack's Workshop item from an already staged folder.</summary>
    public static class WorkshopPublisher
    {
        public const int MaxTitleLength = 128;
        public const int MaxDescriptionLength = 8000;
        public const int MaxChangeNoteLength = 8000;
        public const long MaxPreviewBytes = 1024 * 1024;
        public const string VersionTagKey = "version";

        public static List<string> Validate(ModpackWorkshopMetadata metadata, string stagingFolder, string changeNote)
        {
            var problems = new List<string>();
            if (metadata == null || metadata.Modpack == null)
            {
                problems.Add("The metadata asset has no modpack assigned.");
                return problems;
            }

            if (string.IsNullOrWhiteSpace(metadata.Title))
            {
                problems.Add("Title is required.");
            }
            else if (metadata.Title.Length > MaxTitleLength)
            {
                problems.Add($"Title is over {MaxTitleLength} characters.");
            }

            if (!string.IsNullOrEmpty(metadata.Description) && metadata.Description.Length > MaxDescriptionLength)
            {
                problems.Add($"Description is over {MaxDescriptionLength} characters.");
            }

            if (!WorkshopVersion.IsValid(metadata.Modpack.ModpackVersion))
            {
                problems.Add($"Modpack version '{metadata.Modpack.ModpackVersion}' must look like 1.0.0.");
            }

            if (string.IsNullOrWhiteSpace(changeNote))
            {
                problems.Add("A change note is required.");
            }
            else if (changeNote.Length > MaxChangeNoteLength)
            {
                problems.Add($"Change note is over {MaxChangeNoteLength} characters.");
            }

            foreach (var tag in metadata.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag) && !WorkshopTags.IsValid(tag.Trim())))
            {
                problems.Add($"Tag '{tag}' is invalid: tags cannot contain commas or exceed {WorkshopTags.MaxTagLength} characters.");
            }

            if (metadata.PreviewImage == null)
            {
                if (metadata.PublishedFileId == 0)
                {
                    problems.Add("A preview image is required for the first upload.");
                }
            }
            else
            {
                var previewFile = new FileInfo(GetPreviewPath(metadata));
                if (!previewFile.Exists)
                {
                    problems.Add($"Preview image file not found: {previewFile.FullName}");
                }
                else if (previewFile.Length > MaxPreviewBytes)
                {
                    problems.Add($"Preview image is {previewFile.Length / 1024} KB; Steam rejects previews over 1 MB.");
                }
            }

            problems.AddRange(ModpackWorkshopStaging.Validate(metadata.Modpack, stagingFolder));
            return problems;
        }

        /// <summary>Summary shown for confirmation before anything is sent to Steam.</summary>
        public static string Describe(ModpackWorkshopMetadata metadata, string stagingFolder)
        {
            var files = Directory.GetFiles(stagingFolder, "*", SearchOption.AllDirectories);
            var megabytes = files.Sum(file => new FileInfo(file).Length) / (1024f * 1024f);
            var target = metadata.PublishedFileId == 0
                ? "Creates a NEW Workshop item"
                : $"Updates Workshop item {metadata.PublishedFileId}";
            return $"{target}\n\n" +
                   $"Title: {metadata.Title}\n" +
                   $"Version: {metadata.Modpack.ModpackVersion}\n" +
                   $"Visibility: {metadata.Visibility}\n" +
                   $"Tags: {string.Join(", ", WorkshopTags.Build(metadata))}\n" +
                   $"Content: {files.Length} files, {megabytes:F1} MB";
        }

        public static async Task<PublishOutcome> PublishAsync(ModpackWorkshopMetadata metadata, string stagingFolder,
            string changeNote, IProgress<float> progress)
        {
            var problems = Validate(metadata, stagingFolder, changeNote);
            if (problems.Count > 0)
            {
                return new PublishOutcome(false, metadata == null ? 0 : metadata.PublishedFileId,
                    string.Join("\n", problems));
            }

            if (!SteamEditorSession.TryOpen(out var session, out var steamError))
            {
                return new PublishOutcome(false, metadata.PublishedFileId, steamError);
            }

            using (session)
            {
                var editor = metadata.PublishedFileId == 0
                    ? SteamEditor.NewCommunityFile.ForAppId(SteamStarter.AppId)
                    : new SteamEditor(metadata.PublishedFileId);

                editor = editor
                    .WithTitle(metadata.Title)
                    .WithDescription(metadata.Description ?? string.Empty)
                    .WithChangeLog(changeNote)
                    .WithContent(stagingFolder)
                    .RemoveKeyValueTags(VersionTagKey)
                    .AddKeyValueTag(VersionTagKey, metadata.Modpack.ModpackVersion.Trim());

                foreach (var tag in WorkshopTags.Build(metadata))
                {
                    editor = editor.WithTag(tag);
                }

                if (metadata.PreviewImage != null)
                {
                    editor = editor.WithPreviewFile(GetPreviewPath(metadata));
                }

                editor = metadata.Visibility switch
                {
                    WorkshopVisibility.Private => editor.WithPrivateVisibility(),
                    WorkshopVisibility.FriendsOnly => editor.WithFriendsOnlyVisibility(),
                    _ => editor.WithPublicVisibility()
                };

                // Steam's callbacks can arrive off the main thread, and the asset can only be saved from it.
                var mainThread = SynchronizationContext.Current;

                void SaveFileId(ulong fileId)
                {
                    if (metadata.PublishedFileId == fileId)
                    {
                        return;
                    }

                    metadata.PublishedFileId = fileId;
                    EditorUtility.SetDirty(metadata);
                    AssetDatabase.SaveAssets();
                }

                // The id is saved the moment Steam creates the item, so a failed content upload is retried
                // as an update instead of creating a duplicate.
                var result = await editor.SubmitAsync(progress,
                    created => mainThread?.Post(_ => SaveFileId(created.FileId.Value), null));
                if (result.FileId.Value != 0)
                {
                    SaveFileId(result.FileId.Value);
                }

                return new PublishOutcome(result.Success, result.FileId.Value, DescribeResult(result, metadata));
            }
        }

        public static string GetPreviewPath(ModpackWorkshopMetadata metadata)
        {
            return Path.GetFullPath(AssetDatabase.GetAssetPath(metadata.PreviewImage));
        }

        private static string DescribeResult(Steamworks.Ugc.PublishResult result, ModpackWorkshopMetadata metadata)
        {
            var legal = result.NeedsWorkshopAgreement
                ? "\nSteam needs you to accept the Workshop legal agreement before the item is visible: " +
                  $"https://steamcommunity.com/sharedfiles/workshoplegalagreement (item {result.FileId.Value})."
                : string.Empty;

            if (result.Success)
            {
                return $"Published item {result.FileId.Value}." + legal;
            }

            var reason = result.Result switch
            {
                Result.AccessDenied => "This Steam account cannot edit that item. Log into the account that created it.",
                Result.FileNotFound => $"Item {metadata.PublishedFileId} does not exist on Steam. " +
                                       "Clear Published File Id on the metadata asset to publish a new one.",
                Result.LimitExceeded => "The upload exceeds the Workshop size or item limit.",
                Result.Timeout or Result.ServiceUnavailable or Result.NoConnection =>
                    "Steam could not be reached. Try again.",
                _ => "Steam rejected the upload."
            };

            return $"{reason} ({result.Result})" + legal;
        }
    }
}
