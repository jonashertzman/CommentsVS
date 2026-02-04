using CommentsVS.Services;
using CommentsVS.ToolWindows;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to refresh the anchors list by re-scanning the solution or open documents.
    /// </summary>
    [Command(PackageIds.RefreshAnchors)]
    internal sealed class RefreshAnchorsCommand : BaseCommand<RefreshAnchorsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            // Clear .editorconfig caches so any changes are picked up on refresh
            EditorConfigSettings.ClearCaches();

            CodeAnchorsToolWindow toolWindow = await CodeAnchorsToolWindow.GetInstanceAsync();
            if (toolWindow != null)
            {
                await toolWindow.RefreshAsync();
            }
        }
    }
}
