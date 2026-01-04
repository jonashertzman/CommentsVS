using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.Handlers
{
    [Export(typeof(IMouseProcessorProvider))]
    [Name("IssueReferenceClickHandler")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class IssueReferenceMouseProcessorProvider : IMouseProcessorProvider
    {
        [Import]
        internal ITextStructureNavigatorSelectorService NavigatorService { get; set; }

        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            return wpfTextView.Properties.GetOrCreateSingletonProperty(
                () => new IssueReferenceMouseProcessor(wpfTextView, NavigatorService));
        }
    }

    /// <summary>
    /// Handles mouse clicks on issue references (#123) to open them in the browser.
    /// </summary>
    internal sealed class IssueReferenceMouseProcessor : MouseProcessorBase
    {
        private readonly IWpfTextView _textView;
        private readonly ITextStructureNavigatorSelectorService _navigatorService;
        private GitRepositoryInfo _repoInfo;
        private bool _repoInfoInitialized;

        private static readonly Regex IssueReferenceRegex = new Regex(
            @"#(?<number>\d+)\b",
            RegexOptions.Compiled);

        private static readonly Regex CommentLineRegex = new Regex(
            @"^\s*(//|/\*|\*|')",
            RegexOptions.Compiled);

        public IssueReferenceMouseProcessor(
            IWpfTextView textView,
            ITextStructureNavigatorSelectorService navigatorService)
        {
            _textView = textView;
            _navigatorService = navigatorService;
        }

        public override void PreprocessMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!General.Instance.EnableIssueLinks)
            {
                return;
            }

            // Check for Ctrl+Click
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }

            // Initialize repo info lazily
            if (!_repoInfoInitialized)
            {
                InitializeRepoInfo();
            }

            if (_repoInfo == null)
            {
                return;
            }

            // Get the position under the mouse
            var position = GetMousePosition(e);
            if (position == null)
            {
                return;
            }

            // Check if we clicked on an issue reference
            string url = GetIssueUrlAtPosition(position.Value);
            if (!string.IsNullOrEmpty(url))
            {
                // Open the URL in the default browser
                try
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    e.Handled = true;
                }
                catch
                {
                    // Ignore errors opening browser
                }
            }
        }

        private SnapshotPoint? GetMousePosition(MouseButtonEventArgs e)
        {
            var point = e.GetPosition(_textView.VisualElement);
            var line = _textView.TextViewLines.GetTextViewLineContainingYCoordinate(point.Y + _textView.ViewportTop);

            if (line == null)
            {
                return null;
            }

            return line.GetBufferPositionFromXCoordinate(point.X + _textView.ViewportLeft);
        }

        private string GetIssueUrlAtPosition(SnapshotPoint position)
        {
            ITextSnapshotLine line = position.GetContainingLine();
            string lineText = line.GetText();

            // Check if this line is a comment
            if (!CommentLineRegex.IsMatch(lineText))
            {
                return null;
            }

            int positionInLine = position.Position - line.Start.Position;

            // Find issue references in the line
            foreach (Match match in IssueReferenceRegex.Matches(lineText))
            {
                // Check if the click position is within this match
                if (positionInLine >= match.Index && positionInLine <= match.Index + match.Length)
                {
                    if (int.TryParse(match.Groups["number"].Value, out int issueNumber))
                    {
                        return _repoInfo.GetIssueUrl(issueNumber);
                    }
                }
            }

            return null;
        }

        private void InitializeRepoInfo()
        {
            _repoInfoInitialized = true;

            if (_textView.TextBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            {
                _repoInfo = GitRepositoryService.GetRepositoryInfo(document.FilePath);
            }
        }
    }
}
