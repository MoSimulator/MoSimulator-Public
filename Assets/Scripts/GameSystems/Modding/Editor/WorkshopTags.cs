using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSystems.Modding.Authoring.GameSystems.Modding.Editor
{
    /// <summary>
    /// Collects the tags an upload carries. To add an automatic tag, add a provider below. Existing providers
    /// must keep emitting what they do today: the Workshop browser filters on the game tag, and Steam replaces
    /// the whole tag set on every update, so dropping a tag removes it from already published items.
    /// </summary>
    public static class WorkshopTags
    {
        public const int MaxTagLength = 255;

        private static readonly Func<ModpackWorkshopMetadata, IEnumerable<string>>[] Providers =
        {
            GameTag,
            metadata => metadata.Tags,
        };

        public static string[] Build(ModpackWorkshopMetadata metadata)
        {
            var tags = new List<string>();
            foreach (var provider in Providers)
            {
                tags.AddRange((provider(metadata) ?? Enumerable.Empty<string>())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Select(tag => tag.Trim()));
            }

            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string[] Parse(string commaSeparated)
        {
            return (commaSeparated ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool IsValid(string tag)
        {
            return !string.IsNullOrWhiteSpace(tag) && tag.Length <= MaxTagLength && !tag.Contains(',');
        }

        private static IEnumerable<string> GameTag(ModpackWorkshopMetadata metadata)
        {
            var gameOption = metadata.Modpack == null ? null : metadata.Modpack.GameOption;
            if (gameOption != null)
            {
                yield return gameOption.name;
            }
        }
    }
}
