using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to toggle visibility of XML documentation comments by collapsing/expanding
    /// their outlining regions.
    /// </summary>
    [Command(PackageIds.ToggleCommentVisibility)]
    internal sealed class ToggleCommentVisibilityCommand : BaseCommand<ToggleCommentVisibilityCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            // Set checked state based on the option value
            Command.Checked = General.Instance.CollapseCommentsOnFileOpen;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView textView = docView.TextView;
            ITextSnapshot snapshot = textView.TextSnapshot;

            // Get the outlining manager service
            IComponentModel2 componentModel = await VS.Services.GetComponentModelAsync();
            IOutliningManagerService outliningManagerService = componentModel.GetService<IOutliningManagerService>();
            IOutliningManager outliningManager = outliningManagerService?.GetOutliningManager(textView);

            if (outliningManager == null)
            {
                return;
            }

            // Find all XML doc comment blocks
            var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);
            if (commentStyle == null)
            {
                return;
            }

            var parser = new XmlDocCommentParser(commentStyle);
            IReadOnlyList<XmlDocCommentBlock> commentBlocks = parser.FindAllCommentBlocks(snapshot);

            if (!commentBlocks.Any())
            {
                await VS.StatusBar.ShowMessageAsync("No XML documentation comments found");
                return;
            }

            // Determine current state - are most comments collapsed or expanded?
            var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
            IEnumerable<ICollapsible> allRegions = outliningManager.GetAllRegions(fullSpan);
            
            // Filter to only our XML doc comment regions
            var commentRegions = allRegions
                .Where(r => IsXmlDocCommentRegion(r, commentBlocks, snapshot))
                .ToList();

            if (!commentRegions.Any())
            {
                await VS.StatusBar.ShowMessageAsync("No XML documentation comment regions found");
                return;
            }

            // Check if majority are collapsed
            var collapsedCount = commentRegions.Count(r => r.IsCollapsed);
            var shouldExpand = collapsedCount > commentRegions.Count / 2;

            if (shouldExpand)
            {
                // Expand all XML doc comment regions
                foreach (ICollapsible region in commentRegions.Where(r => r.IsCollapsed))
                {
                    if (region is ICollapsed collapsed)
                    {
                        outliningManager.Expand(collapsed);
                    }
                }

                // Update setting so new files open with comments expanded
                General.Instance.CollapseCommentsOnFileOpen = false;
                await General.Instance.SaveAsync();

                await VS.StatusBar.ShowMessageAsync("XML documentation comments expanded");
            }
            else
            {
                // Collapse all XML doc comment regions
                foreach (ICollapsible region in commentRegions.Where(r => !r.IsCollapsed))
                {
                    outliningManager.TryCollapse(region);
                }

                // Update setting so new files open with comments collapsed
                General.Instance.CollapseCommentsOnFileOpen = true;
                await General.Instance.SaveAsync();

                await VS.StatusBar.ShowMessageAsync("XML documentation comments collapsed");
            }
        }

        private static bool IsXmlDocCommentRegion(
            ICollapsible region,
            IReadOnlyList<XmlDocCommentBlock> commentBlocks,
            ITextSnapshot snapshot)
        {
            Span regionSpan = region.Extent.GetSpan(snapshot).Span;

            foreach (XmlDocCommentBlock block in commentBlocks)
            {
                if (block.MatchesOutliningSpan(regionSpan))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
