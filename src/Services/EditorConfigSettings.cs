using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommentsVS.Options;
using EditorConfig.Core;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Services
{
    /// <summary>
    /// Reads .editorconfig settings that override the Options page values.
    /// </summary>
    /// <remarks>
    /// Supports the following .editorconfig properties:
    /// <list type="bullet">
    /// <item><c>max_line_length</c> - Standard property for maximum line length (overrides MaxLineLength option)</item>
    /// <item><c>custom_anchor_tags</c> - Comma-separated list of custom anchor tags (overrides CustomTags option)</item>
    /// </list>
    /// </remarks>
    internal static class EditorConfigSettings
    {
        private static readonly EditorConfigParser _parser = new();

        // Cache for compiled regex objects keyed by the anchor pattern string
        private static readonly ConcurrentDictionary<string, Regex> _classificationRegexCache = new();
        private static readonly ConcurrentDictionary<string, Regex> _metadataRegexCache = new();
        private static readonly ConcurrentDictionary<string, Regex> _serviceRegexCache = new();

        // Cache for custom tags from .editorconfig files keyed by directory path
        private static readonly ConcurrentDictionary<string, HashSet<string>> _customTagsCache = new();

        /// <summary>
        /// Built-in anchor keywords that are always recognized.
        /// </summary>
        public static readonly string[] BuiltInAnchorTags = ["TODO", "HACK", "NOTE", "BUG", "FIXME", "UNDONE", "REVIEW", "ANCHOR"];

        /// <summary>
        /// Regex pattern for built-in anchor tags.
        /// </summary>
        public const string BuiltInAnchorPattern = "TODO|HACK|NOTE|BUG|FIXME|UNDONE|REVIEW|ANCHOR";

        /// <summary>
        /// Gets all anchor tags: built-in tags plus custom tags from .editorconfig or Options page.
        /// </summary>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>All anchor tags that should be recognized for the given file.</returns>
        public static IReadOnlyList<string> GetAllAnchorTags(string filePath)
        {
            HashSet<string> customTags = GetCustomAnchorTags(filePath);
            if (customTags.Count == 0)
            {
                return BuiltInAnchorTags;
            }

            return [.. BuiltInAnchorTags, .. customTags];
        }

        /// <summary>
        /// Builds the anchor keywords regex pattern for a specific file.
        /// </summary>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>A regex pattern string matching all anchor tags for this file.</returns>
        public static string GetAnchorKeywordsPattern(string filePath)
        {
            HashSet<string> customTags = GetCustomAnchorTags(filePath);
            if (customTags.Count == 0)
            {
                return BuiltInAnchorPattern;
            }

            IEnumerable<string> escapedCustomTags = customTags.Select(Regex.Escape);
            return BuiltInAnchorPattern + "|" + string.Join("|", escapedCustomTags);
        }

        /// <summary>
        /// Builds a classification regex for anchor tags in a specific file.
        /// Uses caching to avoid recompiling the same regex pattern multiple times.
        /// </summary>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>A compiled regex for matching anchor tags in comments.</returns>
        public static Regex GetAnchorClassificationRegex(string filePath)
        {
            string pattern = GetAnchorKeywordsPattern(filePath);

            return _classificationRegexCache.GetOrAdd(pattern, p => new Regex(
                @"(?<=//\s*)(?<tag>\b(?:" + p + @")\b:?)|" +
                @"(?<=/\*[\s\*]*)(?<tag>\b(?:" + p + @")\b:?)|" +
                @"(?<='\s*)(?<tag>\b(?:" + p + @")\b:?)|" +
                @"(?<=^\s*\*\s*)(?<tag>\b(?:" + p + @")\b:?)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline));
        }

        /// <summary>
        /// Builds a metadata regex for anchor tags in a specific file.
        /// Uses caching to avoid recompiling the same regex pattern multiple times.
        /// </summary>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>A compiled regex for matching anchor tags with metadata.</returns>
        public static Regex GetAnchorWithMetadataRegex(string filePath)
        {
            string pattern = GetAnchorKeywordsPattern(filePath);

            return _metadataRegexCache.GetOrAdd(pattern, p => new Regex(
                @"\b(?:" + p + @")\b(?<metadata>\s*(?:\([^)]*\)|\[[^\]]*\]))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }

        /// <summary>
        /// Builds a regex for scanning anchors in comments for the Code Anchors window.
        /// Uses caching to avoid recompiling the same regex pattern multiple times.
        /// </summary>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>A compiled regex for matching anchor tags with prefix, metadata, and message groups.</returns>
        public static Regex GetAnchorServiceRegex(string filePath)
        {
            string pattern = GetAnchorKeywordsPattern(filePath);

            return _serviceRegexCache.GetOrAdd(pattern, p => new Regex(
                @"(?<prefix>//|/\*|'|<!--)\s*(?<tag>\b(?:" + p + @")\b)\s*(?<metadata>(?:\([^)]*\)|\[[^\]]*\]))?\s*:?\s*(?<message>.*?)(?:\*/|-->|$)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }

        /// <summary>
        /// Checks if the given tag is a recognized anchor tag (built-in or custom).
        /// </summary>
        /// <param name="tag">The tag to check (case-insensitive).</param>
        /// <param name="filePath">The file path to get .editorconfig settings for (can be null).</param>
        /// <returns>True if the tag is recognized.</returns>
        public static bool IsAnchorTag(string tag, string filePath)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            string upperTag = tag.ToUpperInvariant();

            // Check built-in tags first (fast path)
            foreach (string builtIn in BuiltInAnchorTags)
            {
                if (builtIn == upperTag)
                {
                    return true;
                }
            }

            // Check custom tags
            return GetCustomAnchorTags(filePath).Contains(upperTag);
        }

        /// <summary>
        /// Gets the max line length from .editorconfig if defined, otherwise returns the value from Options.
        /// </summary>
        /// <param name="textView">The text view to get .editorconfig settings from.</param>
        /// <returns>The max line length value.</returns>
        public static int GetMaxLineLength(ITextView textView)
        {
            int? editorConfigValue = GetMaxLineLengthFromEditorConfig(textView);
            return editorConfigValue ?? General.Instance.MaxLineLength;
        }

        /// <summary>
        /// Gets the max_line_length value from .editorconfig settings for the current file.
        /// Uses the CodingConventionsSnapshot from the text view options.
        /// </summary>
        /// <param name="textView">The text view to get options from.</param>
        /// <returns>The max_line_length value if found, otherwise null.</returns>
        public static int? GetMaxLineLengthFromEditorConfig(ITextView textView)
        {
            try
            {
                // Get the coding conventions from the text view options
                // This contains all .editorconfig properties that apply to the current file
                if (textView?.Options?.GetOptionValue<IReadOnlyDictionary<string, object>>("CodingConventionsSnapshot") is IReadOnlyDictionary<string, object> conventions
                    && conventions.TryGetValue("max_line_length", out object value))
                {
                    if (value is int intValue)
                    {
                        return intValue;
                    }

                    if (value is string stringValue && int.TryParse(stringValue, out int parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch
            {
                // If we can't read from the text view options, just return null
            }

            return null;
        }

        /// <summary>
        /// Creates a CommentReflowEngine configured with the effective settings.
        /// Uses .editorconfig max_line_length if defined, otherwise falls back to Options page.
        /// </summary>
        /// <param name="textView">The text view to get .editorconfig settings from.</param>
        /// <returns>A configured CommentReflowEngine instance.</returns>
        public static CommentReflowEngine CreateReflowEngine(ITextView textView)
        {
            General options = General.Instance;
            return new CommentReflowEngine(
                GetMaxLineLength(textView),
                options.UseCompactStyleForShortSummaries,
                options.PreserveBlankLines);
        }

                        /// <summary>
                        /// Gets the custom anchor tags for a file, checking .editorconfig first then falling back to Options.
                        /// Uses caching to avoid repeated file I/O for .editorconfig parsing.
                        /// Cache is invalidated when the Refresh command is invoked or when a .editorconfig file is saved.
                        /// </summary>
                        /// <param name="filePath">The file path to get settings for (can be null).</param>
                        /// <returns>The custom anchor tags as a HashSet.</returns>
                        public static HashSet<string> GetCustomAnchorTags(string filePath)
                        {
                            if (!string.IsNullOrEmpty(filePath))
                            {
                                // Use directory as cache key since .editorconfig applies to all files in a directory
                                string directory = System.IO.Path.GetDirectoryName(filePath);
                                if (!string.IsNullOrEmpty(directory))
                                {
                                    // Check cache first
                                    if (_customTagsCache.TryGetValue(directory, out HashSet<string> cached))
                                    {
                                        return cached;
                                    }

                                    try
                                    {
                                        FileConfiguration config = _parser.Parse(filePath);
                                        if (config.Properties.TryGetValue("custom_anchor_tags", out string value)
                                            && !string.IsNullOrWhiteSpace(value))
                                        {
                                            HashSet<string> tags = ParseCustomTags(value);
                                            _customTagsCache[directory] = tags;
                                            return tags;
                                        }
                                        else
                                        {
                                            // Cache empty result too to avoid re-parsing
                                            HashSet<string> fallbackTags = General.Instance.GetCustomTagsSet();
                                            _customTagsCache[directory] = fallbackTags;
                                            return fallbackTags;
                                        }
                                    }
                                    catch
                                    {
                                        // Fall back to options on any error
                                    }
                                }
                            }

                            return General.Instance.GetCustomTagsSet();
                        }

                        /// <summary>
                        /// Clears the caches. Call when the Refresh command is invoked or when a .editorconfig file is saved.
                        /// </summary>
                        public static void ClearCaches()
                        {
                            _classificationRegexCache.Clear();
                            _metadataRegexCache.Clear();
                            _serviceRegexCache.Clear();
                            _customTagsCache.Clear();
                        }

                        private static HashSet<string> ParseCustomTags(string customTags)
                        {
                            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (!string.IsNullOrWhiteSpace(customTags))
                            {
                                foreach (string tag in customTags.Split([','], StringSplitOptions.RemoveEmptyEntries))
                                {
                                    string trimmed = tag.Trim().ToUpperInvariant();
                                    if (!string.IsNullOrEmpty(trimmed))
                                    {
                                        _ = tags.Add(trimmed);
                                    }
                                }
                            }
                            return tags;
                        }
                    }
                }
