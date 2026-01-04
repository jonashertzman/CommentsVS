using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.Tagging
{
    [Export(typeof(ITaggerProvider))]
    [TagType(typeof(IOutliningRegionTag))]
    [ContentType("CSharp")]
    [ContentType("Basic")]
    internal sealed class XmlDocCommentOutliningTaggerProvider : ITaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
        {
            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new XmlDocCommentOutliningTagger(buffer)) as ITagger<T>;
        }
    }

    /// <summary>
    /// Provides outlining regions for XML documentation comments.
    /// The regions are always present; the toggle command collapses/expands them.
    /// </summary>
    internal sealed class XmlDocCommentOutliningTagger : ITagger<IOutliningRegionTag>
    {
        // Match <summary>content</summary> on a single line - captures content inside
        private static readonly Regex SingleLineSummaryRegex = new(
            @"<summary>\s*(.*?)\s*</summary>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ITextBuffer _buffer;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public XmlDocCommentOutliningTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Notify that tags may have changed when buffer content changes
            ITextSnapshot snapshot = _buffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(
                new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0)
            {
                yield break;
            }

            ITextSnapshot snapshot = spans[0].Snapshot;
            IContentType contentType = snapshot.ContentType;
            LanguageCommentStyle commentStyle = contentType.IsOfType("CSharp")
                ? LanguageCommentStyle.CSharp
                : LanguageCommentStyle.VisualBasic;

            var parser = new XmlDocCommentParser(commentStyle);
            IReadOnlyList<XmlDocCommentBlock> commentBlocks = parser.FindAllCommentBlocks(snapshot);

            // Check if we should auto-collapse on file open
            var collapseByDefault = General.Instance.CollapseCommentsOnFileOpen;

            foreach (XmlDocCommentBlock block in commentBlocks)
            {
                // Adjust span to start after indentation so the collapsed text
                // appears at the same column as the comment, not at column 0
                var adjustedStart = block.Span.Start + block.Indentation.Length;
                var adjustedSpan = new Span(adjustedStart, block.Span.End - adjustedStart);
                var blockSpan = new SnapshotSpan(snapshot, adjustedSpan);

                if (!spans.IntersectsWith(new NormalizedSnapshotSpanCollection(blockSpan)))
                {
                    continue;
                }

                // Get the collapsed text to display
                var collapsedText = GetCollapsedText(snapshot, block, commentStyle);

                var tag = new OutliningRegionTag(
                    collapsedForm: collapsedText,
                    collapsedHintForm: block.XmlContent,
                    isDefaultCollapsed: collapseByDefault,
                    isImplementation: false);

                yield return new TagSpan<IOutliningRegionTag>(blockSpan, tag);
            }
        }

        private static string GetCollapsedText(ITextSnapshot snapshot, XmlDocCommentBlock block, LanguageCommentStyle commentStyle)
        {
            // Get the first line text
            ITextSnapshotLine firstLine = snapshot.GetLineFromLineNumber(block.StartLine);
            var lineText = firstLine.GetText().Trim();

            // Check if this is a single-line compact summary: /// <summary>text</summary>
            Match match = SingleLineSummaryRegex.Match(lineText);
            if (match.Success)
            {
                // Return the whole line for compact summaries
                return lineText;
            }

            // For multi-line comments, return just the first line (e.g., "/// <summary>")
            return lineText;
        }
    }
}
