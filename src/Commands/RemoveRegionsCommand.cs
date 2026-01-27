using CommentsVS.Services;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to remove #region and #endregion directives from the current document.
    /// </summary>
    [Command(PackageIds.RemoveRegions)]
    internal sealed class RemoveRegionsCommand : BaseCommand<RemoveRegionsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView view = docView.TextView;

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                CommentRemovalService.RemoveRegions(view);
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }
    }
}
