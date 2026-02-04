namespace CommentsVS.Services
{
    /// <summary>
    /// Shared constants used across the extension.
    /// </summary>
    internal static class Constants
    {
        /// <summary>
        /// Maximum file size (in characters) to process. Files larger than this are skipped for performance.
        /// </summary>
        public const int MaxFileSize = 150_000;
    }
}
