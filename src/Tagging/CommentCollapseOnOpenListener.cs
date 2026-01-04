using System;
using System.Collections.Generic;
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

            IOutliningManager outliningManager = OutliningManagerService?.GetOutliningManager(textView);
            if (outliningManager == null)
            {
                return;
            }

            ITextSnapshot snapshot = textView.TextSnapshot;
            var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);
            if (commentStyle == null)
            {
                return;
            }

            var parser = new XmlDocCommentParser(commentStyle);
            IReadOnlyList<XmlDocCommentBlock> commentBlocks = parser.FindAllCommentBlocks(snapshot);

            if (!commentBlocks.Any())
            {
                return;
            }

            // Get all collapsible regions
            var fullSpan = new SnapshotSpan(snapshot, 0, snapshot.Length);
            IEnumerable<ICollapsible> allRegions = outliningManager.GetAllRegions(fullSpan);

            // Collapse XML doc comment regions
            foreach (ICollapsible region in allRegions)
            {
                if (region.IsCollapsed)
                {
                    continue;
                }

                Span regionSpan = region.Extent.GetSpan(snapshot).Span;

                foreach (XmlDocCommentBlock block in commentBlocks)
                {
                    if (block.MatchesOutliningSpan(regionSpan))
                    {
                        outliningManager.TryCollapse(region);
                        break;
                    }
                }
            }
        }
    }
}
