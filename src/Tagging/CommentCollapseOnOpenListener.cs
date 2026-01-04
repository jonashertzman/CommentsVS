using System;
using System.ComponentModel.Composition;
using System.Linq;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Outlining;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.Tagging
{
    /// <summary>
    /// Listens for text view creation and collapses XML doc comments if the option is enabled.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("CSharp")]
    [ContentType("Basic")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CommentCollapseOnOpenListener : IWpfTextViewCreationListener
    {
        [Import]
        internal IOutliningManagerService OutliningManagerService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            if (!General.Instance.CollapseCommentsOnFileOpen)
            {
                return;
            }

            // Defer the collapse until the outlining regions are available
            textView.LayoutChanged += OnLayoutChanged;
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            var textView = sender as IWpfTextView;
            if (textView == null)
            {
                return;
            }

            // Only run once
            textView.LayoutChanged -= OnLayoutChanged;

            if (!General.Instance.CollapseCommentsOnFileOpen)
            {
                return;
            }

            var outliningManager = OutliningManagerService?.GetOutliningManager(textView);
            if (outliningManager == null)
            {
                return;
            }

            var snapshot = textView.TextSnapshot;
            var contentType = snapshot.ContentType;
            var commentStyle = contentType.IsOfType("CSharp")
                ? LanguageCommentStyle.CSharp
                : LanguageCommentStyle.VisualBasic;

            var parser = new XmlDocCommentParser(commentStyle);
            var commentBlocks = parser.FindAllCommentBlocks(snapshot);

            if (!commentBlocks.Any())
            {
                return;
            }

            // Get all collapsible regions
            var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
            var allRegions = outliningManager.GetAllRegions(fullSpan);

            // Collapse XML doc comment regions
            foreach (var region in allRegions)
            {
                if (region.IsCollapsed)
                {
                    continue;
                }

                var regionSpan = region.Extent.GetSpan(snapshot);

                // Check if this region matches any of our comment blocks
                foreach (var block in commentBlocks)
                {
                    int adjustedStart = block.Span.Start + block.Indentation.Length;
                    if (regionSpan.Start == adjustedStart && regionSpan.End == block.Span.End)
                    {
                        outliningManager.TryCollapse(region);
                        break;
                    }
                }
            }
        }
    }
}
