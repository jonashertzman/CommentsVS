using System.Collections.Generic;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to remove anchor comments (TODO, HACK, UNDONE, etc.) from the current document.
    /// </summary>
    [Command(PackageIds.RemoveAnchorComments)]
    internal sealed class RemoveAnchorCommentsCommand : BaseCommand<RemoveAnchorCommentsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView view = docView.TextView;
            IEnumerable<IMappingSpan> mappingSpans = CommentRemovalService.GetClassificationSpans(view, "comment");

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                CommentRemovalService.RemoveAnchorComments(view, mappingSpans);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }
    }
}
