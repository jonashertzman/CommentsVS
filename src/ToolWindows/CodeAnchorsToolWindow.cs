using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;

namespace CommentsVS.ToolWindows
{
    /// <summary>
    /// Tool window for displaying code anchors (TODO, HACK, ANCHOR, etc.) from open documents.
    /// </summary>
    public class CodeAnchorsToolWindow : BaseToolWindow<CodeAnchorsToolWindow>
    {
        private CodeAnchorsControl _control;
        private readonly AnchorService _anchorService = new();

        /// <summary>
        /// Gets the current instance of the tool window (set after CreateAsync is called).
        /// </summary>
        public static CodeAnchorsToolWindow Instance { get; private set; }

        /// <summary>
        /// Gets the control hosted in this tool window.
        /// </summary>
        public CodeAnchorsControl Control => _control;

        public override string GetTitle(int toolWindowId) => "Code Anchors";

        public override Type PaneType => typeof(Pane);


        public override async Task<FrameworkElement> CreateAsync(int toolWindowId, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;

            _control = new CodeAnchorsControl();
            _control.AnchorActivated += OnAnchorActivated;

            // Initial scan of open documents
            await ScanOpenDocumentsAsync();

            // Subscribe to document events
            VS.Events.DocumentEvents.Opened += OnDocumentOpened;
            VS.Events.DocumentEvents.Closed += OnDocumentClosed;
            VS.Events.DocumentEvents.Saved += OnDocumentSaved;

            return _control;
        }

        /// <summary>
        /// Scans all currently open documents for anchors.
        /// </summary>
        public async Task ScanOpenDocumentsAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _control?.ClearAnchors();

            var allAnchors = new List<AnchorItem>();

            // Get the running document table
            IVsRunningDocumentTable rdt = await VS.Services.GetRunningDocumentTableAsync();
            if (rdt == null)
            {
                return;
            }

            rdt.GetRunningDocumentsEnum(out IEnumRunningDocuments enumDocs);
            if (enumDocs == null)
            {
                return;
            }

            var cookies = new uint[1];
            while (enumDocs.Next(1, cookies, out var fetched) == 0 && fetched == 1)
            {
                rdt.GetDocumentInfo(
                    cookies[0],
                    out var flags,
                    out var readLocks,
                    out var editLocks,
                    out var filePath,
                    out IVsHierarchy hierarchy,
                    out var itemId,
                    out IntPtr docData);

                if (string.IsNullOrEmpty(filePath))
                {
                    continue;
                }

                // Get project name
                string projectName = null;
                if (hierarchy != null)
                {
                    hierarchy.GetProperty((uint)Microsoft.VisualStudio.VSConstants.VSITEMID.Root, (int)__VSHPROPID.VSHPROPID_Name, out var projNameObj);
                    projectName = projNameObj as string;
                }

                // Try to get text from the document
                var documentText = await GetDocumentTextAsync(filePath);
                if (!string.IsNullOrEmpty(documentText))
                {
                    IReadOnlyList<AnchorItem> anchors = _anchorService.ScanText(documentText, filePath, projectName);
                    allAnchors.AddRange(anchors);
                }
            }

            _control?.UpdateAnchors(allAnchors);
        }

        /// <summary>
        /// Scans a single document for anchors and updates the display.
        /// </summary>
        public async Task ScanDocumentAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (string.IsNullOrEmpty(filePath) || _control == null)
            {
                return;
            }

            // Remove existing anchors for this file
            _control.RemoveAnchorsForFile(filePath);

            // Get project name
            var projectName = await GetProjectNameForFileAsync(filePath);

            // Get document text
            var documentText = await GetDocumentTextAsync(filePath);
            if (!string.IsNullOrEmpty(documentText))
            {
                IReadOnlyList<AnchorItem> anchors = _anchorService.ScanText(documentText, filePath, projectName);
                _control.AddAnchors(anchors);
            }
        }

        /// <summary>
        /// Navigates to the next anchor in the list.
        /// </summary>
        public async Task NavigateToNextAnchorAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            AnchorItem anchor = _control?.SelectNextAnchor();
            if (anchor != null)
            {
                await NavigateToAnchorAsync(anchor);
            }
        }

        /// <summary>
        /// Navigates to the previous anchor in the list.
        /// </summary>
        public async Task NavigateToPreviousAnchorAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            AnchorItem anchor = _control?.SelectPreviousAnchor();
            if (anchor != null)
            {
                await NavigateToAnchorAsync(anchor);
            }
        }

        private async void OnAnchorActivated(object sender, AnchorItem anchor)
        {
            await NavigateToAnchorAsync(anchor);
        }

        private async void OnDocumentOpened(string filePath)
        {
            await ScanDocumentAsync(filePath);
        }

        private void OnDocumentClosed(string filePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _control?.RemoveAnchorsForFile(filePath);
        }

        private async void OnDocumentSaved(string filePath)
        {
            await ScanDocumentAsync(filePath);
        }

        private async Task NavigateToAnchorAsync(AnchorItem anchor)
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
                        Microsoft.VisualStudio.Text.Editor.EnsureSpanVisibleOptions.AlwaysCenter);
                }
            }
            catch (Exception ex)
            {
                await ex.LogAsync();
            }
        }

        private async Task<string> GetDocumentTextAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Try to get from open document first
                DocumentView docView = await VS.Documents.GetDocumentViewAsync(filePath);
                if (docView?.TextView != null)
                {
                    return docView.TextView.TextSnapshot.GetText();
                }

                // Fall back to reading from disk
                if (System.IO.File.Exists(filePath))
                {
                    return System.IO.File.ReadAllText(filePath);
                }
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }

        private async Task<string> GetProjectNameForFileAsync(string filePath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                PhysicalFile file = await PhysicalFile.FromFileAsync(filePath);
                Project project = file?.ContainingProject;
                return project?.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Pane for the Code Anchors tool window with integrated VS search support.
        /// </summary>
        [Guid("8B0B8A6E-5E7F-4B6E-9F8A-1C2D3E4F5A6B")]
        public class Pane : ToolWindowPane
        {
            private IVsEnumWindowSearchOptions _searchOptionsEnum;
            private IVsEnumWindowSearchFilters _searchFiltersEnum;
            private WindowSearchBooleanOption _matchCaseOption;

            public Pane()
            {
                BitmapImageMoniker = KnownMonikers.Bookmark;
                ToolBar = new System.ComponentModel.Design.CommandID(PackageGuids.CommentsVS, PackageIds.CodeAnchorsToolbar);
                ToolBarLocation = (int)VSTWT_LOCATION.VSTWT_TOP;
            }

            /// <summary>
            /// Gets the control hosted in this tool window.
            /// </summary>
            private CodeAnchorsControl Control => Content as CodeAnchorsControl;

            /// <summary>
            /// Gets a value indicating whether search is enabled for this tool window.
            /// </summary>
            public override bool SearchEnabled => true;

            /// <summary>
            /// Gets the match case search option.
            /// </summary>
            public WindowSearchBooleanOption MatchCaseOption
            {
                get
                {
                    if (_matchCaseOption == null)
                    {
                        _matchCaseOption = new WindowSearchBooleanOption("Match case", "Match case", false);
                    }
                    return _matchCaseOption;
                }
            }

            /// <summary>
            /// Gets the search options enumerator.
            /// </summary>
            public override IVsEnumWindowSearchOptions SearchOptionsEnum
            {
                get
                {
                    if (_searchOptionsEnum == null)
                    {
                        var options = new List<IVsWindowSearchOption> { MatchCaseOption };
                        _searchOptionsEnum = new WindowSearchOptionEnumerator(options);
                    }
                    return _searchOptionsEnum;
                }
            }

            /// <summary>
            /// Gets the search filters enumerator for anchor type filtering.
            /// </summary>
            public override IVsEnumWindowSearchFilters SearchFiltersEnum
            {
                get
                {
                    if (_searchFiltersEnum == null)
                    {
                        var filters = new List<IVsWindowSearchFilter>
                        {
                            new WindowSearchSimpleFilter("TODO", "Show only TODO anchors", "type", "TODO"),
                            new WindowSearchSimpleFilter("HACK", "Show only HACK anchors", "type", "HACK"),
                            new WindowSearchSimpleFilter("NOTE", "Show only NOTE anchors", "type", "NOTE"),
                            new WindowSearchSimpleFilter("BUG", "Show only BUG anchors", "type", "BUG"),
                            new WindowSearchSimpleFilter("FIXME", "Show only FIXME anchors", "type", "FIXME"),
                            new WindowSearchSimpleFilter("UNDONE", "Show only UNDONE anchors", "type", "UNDONE"),
                            new WindowSearchSimpleFilter("REVIEW", "Show only REVIEW anchors", "type", "REVIEW"),
                            new WindowSearchSimpleFilter("ANCHOR", "Show only ANCHOR anchors", "type", "ANCHOR"),
                        };
                        _searchFiltersEnum = new WindowSearchFilterEnumerator(filters);
                    }
                    return _searchFiltersEnum;
                }
            }

            /// <summary>
            /// Creates a search task for the given query.
            /// </summary>
            public override IVsSearchTask CreateSearch(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback)
            {
                if (pSearchQuery == null || pSearchCallback == null)
                {
                    return null;
                }

                return new CodeAnchorsSearchTask(dwCookie, pSearchQuery, pSearchCallback, this);
            }

            /// <summary>
            /// Clears the current search and restores all anchors.
            /// </summary>
            public override void ClearSearch()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Control?.ClearSearchFilter();
            }
        }

        /// <summary>
        /// Search task for filtering anchors based on search query.
        /// </summary>
        private class CodeAnchorsSearchTask(uint dwCookie, IVsSearchQuery pSearchQuery, IVsSearchCallback pSearchCallback, CodeAnchorsToolWindow.Pane pane)
            : VsSearchTask(dwCookie, pSearchQuery, pSearchCallback)
        {
            protected override void OnStartSearch()
            {
                ErrorCode = VSConstants.S_OK;
                uint resultCount = 0;

                try
                {
                    var searchString = SearchQuery.SearchString ?? string.Empty;
                    var matchCase = pane.MatchCaseOption.Value;

                    // Extract type filter if present (e.g., type:"TODO")
                    string typeFilter = null;
                    var filterPattern = "type:\"";
                    var filterIndex = searchString.IndexOf(filterPattern, StringComparison.OrdinalIgnoreCase);
                    if (filterIndex >= 0)
                    {
                        var startIndex = filterIndex + filterPattern.Length;
                        var endIndex = searchString.IndexOf('"', startIndex);
                        if (endIndex > startIndex)
                        {
                            typeFilter = searchString.Substring(startIndex, endIndex - startIndex);
                            // Remove the filter from the search string
                            searchString = searchString.Remove(filterIndex, endIndex - filterIndex + 1).Trim();
                        }
                    }

                    // Apply the search on the UI thread
                    ThreadHelper.Generic.Invoke(() =>
                    {
                        if (pane.Content is CodeAnchorsControl control)
                        {
                            resultCount = control.ApplySearchFilter(searchString, typeFilter, matchCase);
                        }
                    });

                    SearchResults = resultCount;
                }
                catch (Exception)
                {
                    ErrorCode = VSConstants.E_FAIL;
                }

                base.OnStartSearch();
            }

            protected override void OnStopSearch()
            {
                SearchResults = 0;
            }
        }
    }
}
