using System.ComponentModel.Composition;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.QuickInfo
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("IssueReferenceQuickInfo")]
    [ContentType("code")]
    [Order(Before = "Default Quick Info Presenter")]
    internal sealed class IssueReferenceQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer)
        {
            return textBuffer.Properties.GetOrCreateSingletonProperty(
                () => new IssueReferenceQuickInfoSource(textBuffer));
        }
    }

    /// <summary>
    /// Provides hover tooltips for issue references (#123) showing the full URL.
    /// </summary>
    internal sealed class IssueReferenceQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _textBuffer;
        private GitRepositoryInfo _repoInfo;
        private bool _repoInfoInitialized;

        private static readonly Regex IssueReferenceRegex = new Regex(
            @"#(?<number>\d+)\b",
            RegexOptions.Compiled);

        private static readonly Regex CommentLineRegex = new Regex(
            @"^\s*(//|/\*|\*|')",
            RegexOptions.Compiled);

        public IssueReferenceQuickInfoSource(ITextBuffer textBuffer)
        {
            _textBuffer = textBuffer;
        }

        public Task<QuickInfoItem> GetQuickInfoItemAsync(
            IAsyncQuickInfoSession session,
            CancellationToken cancellationToken)
        {
            if (!General.Instance.EnableIssueLinks)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            SnapshotPoint? triggerPoint = session.GetTriggerPoint(_textBuffer.CurrentSnapshot);
            if (!triggerPoint.HasValue)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            // Initialize repo info lazily
            if (!_repoInfoInitialized)
            {
                InitializeRepoInfo();
            }

            if (_repoInfo == null)
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            ITextSnapshotLine line = triggerPoint.Value.GetContainingLine();
            string lineText = line.GetText();

            // Check if this line is a comment
            if (!CommentLineRegex.IsMatch(lineText))
            {
                return Task.FromResult<QuickInfoItem>(null);
            }

            int positionInLine = triggerPoint.Value.Position - line.Start.Position;

            // Find issue references in the line
            foreach (Match match in IssueReferenceRegex.Matches(lineText))
            {
                // Check if the trigger point is within this match
                if (positionInLine >= match.Index && positionInLine <= match.Index + match.Length)
                {
                    if (int.TryParse(match.Groups["number"].Value, out int issueNumber))
                    {
                        string url = _repoInfo.GetIssueUrl(issueNumber);
                        if (!string.IsNullOrEmpty(url))
                        {
                            var span = new SnapshotSpan(line.Start + match.Index, match.Length);
                            var trackingSpan = _textBuffer.CurrentSnapshot.CreateTrackingSpan(
                                span, SpanTrackingMode.EdgeInclusive);

                            string providerName = GetProviderName(_repoInfo.Provider);
                            string tooltip = $"{providerName} Issue #{issueNumber}\n{url}\n\nCtrl+Click to open";

                            return Task.FromResult(new QuickInfoItem(trackingSpan, tooltip));
                        }
                    }
                }
            }

            return Task.FromResult<QuickInfoItem>(null);
        }

        private static string GetProviderName(GitHostingProvider provider)
        {
            switch (provider)
            {
                case GitHostingProvider.GitHub:
                    return "GitHub";
                case GitHostingProvider.GitLab:
                    return "GitLab";
                case GitHostingProvider.Bitbucket:
                    return "Bitbucket";
                case GitHostingProvider.AzureDevOps:
                    return "Azure DevOps Work Item";
                default:
                    return "Issue";
            }
        }

        private void InitializeRepoInfo()
        {
            _repoInfoInitialized = true;

            if (_textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            {
                _repoInfo = GitRepositoryService.GetRepositoryInfo(document.FilePath);
            }
        }

        public void Dispose()
        {
        }
    }
}
