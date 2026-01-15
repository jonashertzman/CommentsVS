using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.ToolWindows
{
    /// <summary>
    /// Handles navigation to anchor items, opening documents and positioning the caret at the anchor location.
    /// </summary>
    internal sealed class AnchorNavigationService
    {
        /// <summary>
        /// Navigates to the specified anchor, opening the document if needed and positioning the caret.
        /// </summary>
        /// <param name="anchor">The anchor item to navigate to.</param>
        public async Task NavigateToAnchorAsync(AnchorItem anchor)
        {
            if (anchor == null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Open the document
            DocumentView docView = await VS.Documents.OpenAsync(anchor.FilePath);
            if (docView?.TextView == null)
            {
                return;
            }

            // Navigate to the line
            try
            {
                ITextSnapshot snapshot = docView.TextView.TextSnapshot;
                if (anchor.LineNumber > 0 && anchor.LineNumber <= snapshot.LineCount)
                {
                    ITextSnapshotLine line = snapshot.GetLineFromLineNumber(anchor.LineNumber - 1);
                    SnapshotPoint point = line.Start.Add(Math.Min(anchor.Column, line.Length));

                    docView.TextView.Caret.MoveTo(point);
                    docView.TextView.ViewScroller.EnsureSpanVisible(
                        new SnapshotSpan(point, 0),
                        EnsureSpanVisibleOptions.AlwaysCenter);
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }

        /// <summary>
        /// Navigates to the next anchor in the control and opens the document.
        /// </summary>
        /// <param name="control">The code anchors control.</param>
        public async Task NavigateToNextAnchorAsync(CodeAnchorsControl control)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            AnchorItem anchor = control?.SelectNextAnchor();
            if (anchor != null)
            {
                await NavigateToAnchorAsync(anchor);
            }
        }

        /// <summary>
        /// Navigates to the previous anchor in the control and opens the document.
        /// </summary>
        /// <param name="control">The code anchors control.</param>
        public async Task NavigateToPreviousAnchorAsync(CodeAnchorsControl control)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            AnchorItem anchor = control?.SelectPreviousAnchor();
            if (anchor != null)
            {
                await NavigateToAnchorAsync(anchor);
            }
        }
    }
}
