using System.Collections.Generic;
using System.Linq;
using CommentsVS.Options;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;

namespace CommentsVS.Commands
{
    /// <summary>
    /// Command to set the rendering mode to Off.
    /// </summary>
    [Command(PackageIds.SetRenderingModeOff)]
    internal sealed class SetRenderingModeOffCommand : BaseCommand<SetRenderingModeOffCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Checked = General.Instance.CommentRenderingMode == RenderingMode.Off;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await SetRenderingModeHelper.SetModeAsync(RenderingMode.Off);
        }
    }

    /// <summary>
    /// Command to set the rendering mode to Compact.
    /// </summary>
    [Command(PackageIds.SetRenderingModeCompact)]
    internal sealed class SetRenderingModeCompactCommand : BaseCommand<SetRenderingModeCompactCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Checked = General.Instance.CommentRenderingMode == RenderingMode.Compact;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await SetRenderingModeHelper.SetModeAsync(RenderingMode.Compact);
        }
    }

    /// <summary>
    /// Command to set the rendering mode to Full.
    /// </summary>
    [Command(PackageIds.SetRenderingModeFull)]
    internal sealed class SetRenderingModeFullCommand : BaseCommand<SetRenderingModeFullCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            Command.Checked = General.Instance.CommentRenderingMode == RenderingMode.Full;
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await SetRenderingModeHelper.SetModeAsync(RenderingMode.Full);
        }
    }

    /// <summary>
    /// Helper class to set rendering mode and handle associated state changes.
    /// </summary>
    internal static class SetRenderingModeHelper
    {
        /// <summary>
        /// Event raised when the rendered comments state changes.
        /// </summary>
        public static event EventHandler RenderedCommentsStateChanged;

        public static async Task SetModeAsync(RenderingMode mode)
        {
            RenderingMode previousMode = General.Instance.CommentRenderingMode;

            if (previousMode == mode)
            {
                return;
            }

            General.Instance.CommentRenderingMode = mode;
            await General.Instance.SaveAsync();

            // When switching to Compact or Full mode, we need to expand any collapsed
            // XML doc comment regions so they don't interfere with IntraText adornments.
            // The outlining tagger will stop providing regions, but VS keeps existing ones
            // until they're explicitly expanded.
            if ((mode == RenderingMode.Full || mode == RenderingMode.Compact) && previousMode == RenderingMode.Off)
            {
                await ExpandXmlDocCommentsAsync();
            }

            // Notify that rendered comments state changed
            RenderedCommentsStateChanged?.Invoke(null, EventArgs.Empty);

            // When switching back to Off mode from Compact/Full, the outlining tagger
            // will start providing regions again. If "Collapse by Default" is enabled,
            // we need to collapse them.
            if (mode == RenderingMode.Off && General.Instance.CollapseCommentsOnFileOpen)
            {
                await CollapseXmlDocCommentsAsync();
            }

            var modeName = mode switch
            {
                RenderingMode.Off => "Off",
                RenderingMode.Compact => "Compact",
                RenderingMode.Full => "Full",
                _ => "Off"
            };

            await VS.StatusBar.ShowMessageAsync($"Comment rendering mode: {modeName}");
        }

        /// <summary>
        /// Expands all collapsed XML doc comment regions (without re-collapsing).
        /// Used when switching to Compact/Full mode where IntraText adornments replace the text.
        /// </summary>
        private static async Task ExpandXmlDocCommentsAsync()
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView textView = docView.TextView;
            ITextSnapshot snapshot = textView.TextSnapshot;

            IComponentModel2 componentModel = await VS.Services.GetComponentModelAsync();
            IOutliningManagerService outliningManagerService = componentModel.GetService<IOutliningManagerService>();
            IOutliningManager outliningManager = outliningManagerService?.GetOutliningManager(textView);

            if (outliningManager == null)
            {
                return;
            }

            var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
            var collapsedRegions = outliningManager
                .GetCollapsedRegions(fullSpan)
                .Where(r => IsXmlDocCommentRegion(r, snapshot))
                .ToList();

            foreach (ICollapsed collapsed in collapsedRegions)
            {
                outliningManager.Expand(collapsed);
            }
        }

        /// <summary>
        /// Collapses all XML doc comment regions.
        /// Used when switching back to Off mode with "Collapse by Default" enabled.
        /// </summary>
        private static async Task CollapseXmlDocCommentsAsync()
        {
            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.TextView == null)
            {
                return;
            }

            IWpfTextView textView = docView.TextView;
            ITextSnapshot snapshot = textView.TextSnapshot;

            IComponentModel2 componentModel = await VS.Services.GetComponentModelAsync();
            IOutliningManagerService outliningManagerService = componentModel.GetService<IOutliningManagerService>();
            IOutliningManager outliningManager = outliningManagerService?.GetOutliningManager(textView);

            if (outliningManager == null)
            {
                return;
            }

            // Wait a bit for the outlining tagger to provide the new regions
            await Task.Delay(100);

            var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
            var regions = outliningManager
                .GetAllRegions(fullSpan)
                .Where(r => IsXmlDocCommentRegion(r, snapshot) && !r.IsCollapsed)
                .ToList();

            foreach (ICollapsible region in regions)
            {
                outliningManager.TryCollapse(region);
            }
        }

        private static bool IsXmlDocCommentRegion(ICollapsible region, ITextSnapshot snapshot)
        {
            SnapshotSpan extent = region.Extent.GetSpan(snapshot);
            var text = extent.GetText().TrimStart();
            return text.StartsWith("///") || text.StartsWith("'''");
        }
    }
}
