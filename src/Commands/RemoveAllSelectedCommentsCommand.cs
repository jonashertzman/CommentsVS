using System.Collections.Generic;
using System.Linq;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to remove all comments within the current selection.
    /// </summary>
    [Command(PackageIds.RemoveAllSelectedComments)]
    internal sealed class RemoveAllSelectedCommentsCommand : BaseCommand<RemoveAllSelectedCommentsCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            // Only enable when there is a selection
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
                Command.Enabled = docView?.TextView != null && !docView.TextView.Selection.IsEmpty;
            });
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView view = docView.TextView;

            if (view.Selection.IsEmpty)
            {
                return;
            }

            IEnumerable<IMappingSpan> mappingSpans = CommentRemovalService.GetSelectionClassificationSpans(view, "comment");

            if (!mappingSpans.Any())
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                CommentRemovalService.RemoveComments(view, mappingSpans);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }
    }
}
