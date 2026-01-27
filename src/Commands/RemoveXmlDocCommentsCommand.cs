using System.Collections.Generic;
using System.Linq;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to remove only XML documentation comments from the current document.
    /// </summary>
    [Command(PackageIds.RemoveXmlDocComments)]
    internal sealed class RemoveXmlDocCommentsCommand : BaseCommand<RemoveXmlDocCommentsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView view = docView.TextView;
            IEnumerable<IMappingSpan> mappingSpans = CommentRemovalService.GetClassificationSpans(view, "xml doc comment");

            if (!mappingSpans.Any())
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                CommentRemovalService.RemoveXmlDocComments(view, mappingSpans);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }
    }
}
