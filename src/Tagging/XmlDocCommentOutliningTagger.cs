using System.Collections.Generic;
using System.ComponentModel.Composition;
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
    /// Provides outlining regions for XML documentation comments with immediate collapse support.
    /// </summary>
    internal sealed class XmlDocCommentOutliningTagger : ITagger<IOutliningRegionTag>
    {
        private readonly ITextBuffer _buffer;

        public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

        public XmlDocCommentOutliningTagger(ITextBuffer buffer)
        {
            _buffer = buffer;
            _buffer.Changed += OnBufferChanged;
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
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
            LanguageCommentStyle commentStyle = LanguageCommentStyle.GetForContentType(contentType);

            if (commentStyle == null)
            {
                yield break;
            }

            var parser = new XmlDocCommentParser(commentStyle);
            IReadOnlyList<XmlDocCommentBlock> commentBlocks = parser.FindAllCommentBlocks(snapshot);

            bool collapseByDefault = General.Instance.CollapseCommentsOnFileOpen;

            foreach (XmlDocCommentBlock block in commentBlocks)
            {
                // Adjust span to start after indentation so collapsed text
                // appears at the same column as the comment
                int adjustedStart = block.Span.Start + block.Indentation.Length;
                var adjustedSpan = new Span(adjustedStart, block.Span.End - adjustedStart);
                var blockSpan = new SnapshotSpan(snapshot, adjustedSpan);

                if (!spans.IntersectsWith(new NormalizedSnapshotSpanCollection(blockSpan)))
                {
                    continue;
                }

                // Get first line as collapsed text (without indentation)
                ITextSnapshotLine firstLine = snapshot.GetLineFromLineNumber(block.StartLine);
                string collapsedText = firstLine.GetText().TrimStart();

                var tag = new OutliningRegionTag(
                    collapsedForm: collapsedText,
                    collapsedHintForm: block.XmlContent,
                    isDefaultCollapsed: collapseByDefault,
                    isImplementation: false);

                yield return new TagSpan<IOutliningRegionTag>(blockSpan, tag);
            }
        }
    }
}
