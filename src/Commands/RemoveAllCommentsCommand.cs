using System.Collections.Generic;
using System.Linq;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to remove all comments from the current document.
    /// </summary>
    [Command(PackageIds.RemoveAllComments)]
    internal sealed class RemoveAllCommentsCommand : BaseCommand<RemoveAllCommentsCommand>
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
