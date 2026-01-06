using CommentsVS.ToolWindows;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to refresh the anchors list in the Code Anchors tool window.
    /// </summary>
    [Command(PackageIds.RefreshAnchors)]
    internal sealed class RefreshAnchorsCommand : BaseCommand<RefreshAnchorsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            if (CodeAnchorsToolWindow.Instance != null)
            {
                await CodeAnchorsToolWindow.Instance.ScanOpenDocumentsAsync();
            }
        }
    }
}
