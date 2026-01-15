using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CommentsVS.ToolWindows
{
    /// <summary>
    /// Filters anchors based on scope (solution, project, document, open documents).
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AnchorScopeFilter"/> class.
    /// </remarks>
    /// <param name="documentEventCoordinator">The document event coordinator for project lookup.</param>
    internal sealed class AnchorScopeFilter(DocumentEventCoordinator documentEventCoordinator)
    {
        private readonly DocumentEventCoordinator _documentEventCoordinator = documentEventCoordinator ?? throw new ArgumentNullException(nameof(documentEventCoordinator));

        /// <summary>
        /// Applies the specified scope filter to the given anchors.
        /// </summary>
        /// <param name="anchors">The anchors to filter.</param>
        /// <param name="scope">The scope to apply.</param>
        /// <returns>The filtered anchors.</returns>
        public async Task<IReadOnlyList<AnchorItem>> ApplyFilterAsync(IReadOnlyList<AnchorItem> anchors, AnchorScope scope)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            return scope switch
            {
                AnchorScope.EntireSolution => anchors,
                AnchorScope.CurrentProject => await FilterByCurrentProjectAsync(anchors),
                AnchorScope.CurrentDocument => await FilterByCurrentDocumentAsync(anchors),
                AnchorScope.OpenDocuments => await FilterByOpenDocumentsAsync(anchors),
                _ => anchors,
            };
        }

        private async Task<IReadOnlyList<AnchorItem>> FilterByCurrentProjectAsync(IReadOnlyList<AnchorItem> anchors)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();
            if (docView?.FilePath == null)
            {
                return anchors;
            }

            var projectName = await _documentEventCoordinator.GetProjectNameForFileAsync(docView.FilePath);
            if (string.IsNullOrEmpty(projectName))
            {
                return anchors;
            }

            return [.. anchors.Where(a => a.Project?.Equals(projectName, StringComparison.OrdinalIgnoreCase) == true)];
        }

        private async Task<IReadOnlyList<AnchorItem>> FilterByCurrentDocumentAsync(IReadOnlyList<AnchorItem> anchors)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            DocumentView docView = await VS.Documents.GetActiveDocumentViewAsync();

            if (docView?.FilePath == null)
            {
                return [];
            }

            return [.. anchors.Where(a => a.FilePath?.Equals(docView.FilePath, StringComparison.OrdinalIgnoreCase) == true)];
        }

        private async Task<IReadOnlyList<AnchorItem>> FilterByOpenDocumentsAsync(IReadOnlyList<AnchorItem> anchors)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Use DTE to get open documents
                EnvDTE.DTE dte = await VS.GetServiceAsync<EnvDTE.DTE, EnvDTE.DTE>();
                if (dte?.Documents == null)
                {
                    return anchors;
                }

                var openPathsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (EnvDTE.Document doc in dte.Documents)
                {
                    if (!string.IsNullOrEmpty(doc.FullName))
                    {
                        openPathsSet.Add(doc.FullName);
                    }
                }

                return [.. anchors.Where(a => a.FilePath != null && openPathsSet.Contains(a.FilePath))];
            }
            catch
            {
                // Fall back to returning all anchors if we can't get open documents
                return anchors;
            }
        }
    }
}
