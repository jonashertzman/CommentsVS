using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;

namespace CommentsVS.Services
{
    /// <summary>
    /// Helper methods for common ITextBuffer operations.
    /// </summary>
    internal static class TextBufferHelper
    {
        /// <summary>
        /// Gets the file path from a text buffer's associated document.
        /// </summary>
        /// <param name="textBuffer">The text buffer.</param>
        /// <returns>The file path, or null if no document is associated.</returns>
        public static string GetFilePath(ITextBuffer textBuffer)
        {
            if (textBuffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            {
                return document.FilePath;
            }

            return null;
        }

        /// <summary>
        /// Gets Git repository information for a text buffer's file asynchronously.
        /// </summary>
        /// <param name="textBuffer">The text buffer.</param>
        /// <returns>The repository info, or null if not in a Git repository.</returns>
        public static async Task<GitRepositoryInfo> GetRepositoryInfoAsync(ITextBuffer textBuffer)
        {
            var filePath = GetFilePath(textBuffer);
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            return await GitRepositoryService.GetRepositoryInfoAsync(filePath).ConfigureAwait(false);
        }
    }
}
