using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace CommentsVS.Services
{
    /// <summary>
    /// Helper methods for mouse position calculations in text views.
    /// </summary>
    internal static class MousePositionHelper
    {
        /// <summary>
        /// Gets the buffer position under the mouse cursor.
        /// </summary>
        /// <param name="textView">The text view.</param>
        /// <param name="e">The mouse event args.</param>
        /// <returns>The snapshot point at the mouse position, or null if not over text.</returns>
        public static SnapshotPoint? GetMousePosition(IWpfTextView textView, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(textView.VisualElement);
            ITextViewLine line = textView.TextViewLines.GetTextViewLineContainingYCoordinate(point.Y + textView.ViewportTop);

            if (line == null)
            {
                return null;
            }

            return line.GetBufferPositionFromXCoordinate(point.X + textView.ViewportLeft);
        }
    }
}
