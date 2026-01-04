using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using CommentsVS.Services;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.SuggestedActions
{
    /// <summary>
    /// MEF provider that creates the suggested actions source for XML doc comment reflow.
    /// </summary>
    [Export(typeof(ISuggestedActionsSourceProvider))]
    [Name("Reflow XML Doc Comment Suggested Actions")]
    [ContentType("text")]
    internal sealed class ReflowCommentSuggestedActionsSourceProvider : ISuggestedActionsSourceProvider
    {
        [Import(typeof(ITextStructureNavigatorSelectorService))]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        public ISuggestedActionsSource CreateSuggestedActionsSource(ITextView textView, ITextBuffer textBuffer)
        {
            if (textView == null || textBuffer == null)
            {
                return null;
            }

            return new ReflowCommentSuggestedActionsSource(textView, textBuffer);
        }
    }

    /// <summary>
    /// Provides suggested actions for reflowing XML documentation comments.
    /// </summary>
    internal sealed class ReflowCommentSuggestedActionsSource(
        ITextView textView,
        ITextBuffer textBuffer) : ISuggestedActionsSource
    {
        public event EventHandler<EventArgs> SuggestedActionsChanged { add { } remove { } }

        public void Dispose()
        {
        }

        public IEnumerable<SuggestedActionSet> GetSuggestedActions(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            XmlDocCommentBlock block = TryGetCommentBlockUnderCaret();
            if (block == null)
            {
                return [];
            }

            ITrackingSpan trackingSpan = range.Snapshot.CreateTrackingSpan(
                block.Span,
                SpanTrackingMode.EdgeInclusive);

            var action = new ReflowCommentSuggestedAction(trackingSpan, block, textBuffer);

            return
            [
                new SuggestedActionSet(
                    categoryName: PredefinedSuggestedActionCategoryNames.Refactoring,
                    actions: [action],
                    title: "XML Documentation",
                    priority: SuggestedActionSetPriority.Low)
            ];
        }

        public Task<bool> HasSuggestedActionsAsync(
            ISuggestedActionCategorySet requestedActionCategories,
            SnapshotSpan range,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                XmlDocCommentBlock block = TryGetCommentBlockUnderCaret();
                return block != null;
            }, cancellationToken);
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        /// <summary>
        /// Tries to find an XML doc comment block at the current caret position.
        /// </summary>
        private XmlDocCommentBlock TryGetCommentBlockUnderCaret()
        {
            SnapshotPoint caretPosition = textView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caretPosition.Snapshot;

            var contentType = textBuffer.ContentType.TypeName;
            var commentStyle = LanguageCommentStyle.GetForContentType(contentType);

            if (commentStyle == null)
            {
                return null;
            }

            var parser = new XmlDocCommentParser(commentStyle);
            return parser.FindCommentBlockAtPosition(snapshot, caretPosition.Position);
        }
    }
}
