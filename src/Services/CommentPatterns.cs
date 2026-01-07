using System.Text.RegularExpressions;

namespace CommentsVS.Services
{
    /// <summary>
    /// Shared regex patterns for comment and anchor detection.
    /// </summary>
    internal static class CommentPatterns
    {
        /// <summary>
        /// Pattern string for anchor keywords (TODO, HACK, NOTE, etc.).
        /// </summary>
        public const string AnchorKeywordsPattern = "TODO|HACK|NOTE|BUG|FIXME|UNDONE|REVIEW|ANCHOR";

        /// <summary>
        /// Regex to match comment tags (anchors) with optional trailing colon.
        /// Captures the tag keyword in the "tag" group.
        /// </summary>
        public static readonly Regex CommentTagRegex = new(
            @"\b(?<tag>" + AnchorKeywordsPattern + @"|LINK)\b:?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Regex to match comment line prefixes (C-style, VB-style).
        /// </summary>
        public static readonly Regex CommentLineRegex = new(
            @"^\s*(//|/\*|\*|')",
            RegexOptions.Compiled);

        /// <summary>
        /// Regex to match anchors in comments for classification.
        /// Looks for anchor keywords after C-style (//), block comment (/*), or VB-style (') comment prefixes.
        /// </summary>
        public static readonly Regex AnchorClassificationRegex = new(
            @"(?<=//.*)(?<tag>\b(?:" + AnchorKeywordsPattern + @")\b:?)|" +
            @"(?<=/\*.*)(?<tag>\b(?:" + AnchorKeywordsPattern + @")\b:?)|" +
            @"(?<='.*)(?<tag>\b(?:" + AnchorKeywordsPattern + @")\b:?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Regex to match anchor keywords with optional metadata (parentheses or brackets).
        /// Captures the metadata in the "metadata" group.
        /// </summary>
        public static readonly Regex AnchorWithMetadataRegex = new(
            @"\b(?:" + AnchorKeywordsPattern + @")\b(?<metadata>\s*(?:\([^)]*\)|\[[^\]]*\]))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Regex to match anchors in comments for the anchor service.
        /// Captures prefix, tag, metadata, and message groups.
        /// Supports C-style (// and /* */), VB-style ('), and HTML-style (<!-- -->) comments.
        /// </summary>
        public static readonly Regex AnchorServiceRegex = new(
            @"(?<prefix>//|/\*|'|<!--)\s*(?<tag>\b(?:" + AnchorKeywordsPattern + @")\b)\s*(?<metadata>(?:\([^)]*\)|\[[^\]]*\]))?\s*:?\s*(?<message>.*?)(?:\*/|-->|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Regex to match anchor tags with optional metadata for parsing.
        /// </summary>
        public static readonly Regex MetadataParseRegex = new(
            @"(?<tag>" + AnchorKeywordsPattern + @")(?:\s*(?:\((?<metaParen>[^)]*)\)|\[(?<metaBracket>[^\]]*)\]))?\s*: ?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
