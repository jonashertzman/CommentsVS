using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommentsVS.Adornments;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;

namespace CommentsVS.SuggestedActions
{
    /// <summary>
    /// Light bulb action to reflow an XML documentation comment block.
    /// </summary>
    internal sealed class ReflowCommentSuggestedAction(
        ITrackingSpan trackingSpan,
        XmlDocCommentBlock commentBlock,
        ITextBuffer textBuffer,
        IWpfTextView textView,
        IOutliningManager outliningManager) : ISuggestedAction
    {
        public string DisplayText => "Reflow comment";

        public bool HasActionSets => false;

        public bool HasPreview => true;

        public string IconAutomationText => null;

        public ImageMoniker IconMoniker => KnownMonikers.TextLeft;

        public string InputGestureText => null;

        public Task<IEnumerable<SuggestedActionSet>> GetActionSetsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IEnumerable<SuggestedActionSet>>(null);
        }

        public Task<object> GetPreviewAsync(CancellationToken cancellationToken)
        {
            return Task.Run<object>(async () =>
            {
                General options = await General.GetLiveInstanceAsync();
                CommentReflowEngine engine = options.CreateReflowEngine();

                var reflowed = engine.ReflowComment(commentBlock);

                if (string.IsNullOrEmpty(reflowed))
                {
                    return "No changes needed";
                }

                return reflowed;
            }, cancellationToken);
        }

        public void Invoke(CancellationToken cancellationToken)
        {
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                // If in rendered mode, switch to raw source view first
                RenderingMode renderingMode = General.Instance.CommentRenderingMode;
                if (renderingMode == RenderingMode.Compact || renderingMode == RenderingMode.Full)
                {
                    // Hide the rendered adornment for this comment
                    if (textView.Properties.TryGetProperty(typeof(RenderedCommentIntraTextTagger), out RenderedCommentIntraTextTagger tagger))
                    {
                        tagger.HandleEscapeKey(commentBlock.StartLine);
                    }
                }

                // Expand any collapsed outlining regions that contain this comment
                if (outliningManager != null)
                {
                    try
                    {
                        ITextSnapshot snapshot = textBuffer.CurrentSnapshot;
                        var blockSpan = new SnapshotSpan(snapshot, commentBlock.Span);
                        IEnumerable<ICollapsed> collapsedRegions = outliningManager.GetCollapsedRegions(blockSpan);

                        foreach (ICollapsed region in collapsedRegions)
                        {
                            outliningManager.Expand(region);
                        }
                    }
                    catch
                    {
                        // Ignore errors when expanding regions
                    }
                }

                General options = await General.GetLiveInstanceAsync();
                CommentReflowEngine engine = options.CreateReflowEngine();

                var reflowed = engine.ReflowComment(commentBlock);

                if (!string.IsNullOrEmpty(reflowed))
                {
                    ITextSnapshot snapshot = textBuffer.CurrentSnapshot;

                    // Re-parse to get current span (might have shifted)
                    LanguageCommentStyle commentStyle = commentBlock.CommentStyle;
                    var parser = new XmlDocCommentParser(commentStyle);

                    SnapshotSpan currentSpan = trackingSpan.GetSpan(snapshot);
                    XmlDocCommentBlock currentBlock = parser.FindCommentBlockAtPosition(snapshot, currentSpan.Start);

                    if (currentBlock != null)
                    {
                        using (ITextEdit edit = textBuffer.CreateEdit())
                        {
                            edit.Replace(currentBlock.Span, reflowed);
                            edit.Apply();
                        }
                    }
                }
            });
        }

        public bool TryGetTelemetryId(out Guid telemetryId)
        {
            telemetryId = Guid.Empty;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
