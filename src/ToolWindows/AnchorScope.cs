namespace CommentsVS.ToolWindows
{
    /// <summary>
    /// Defines the scope for filtering code anchors.
    /// </summary>
    public enum AnchorScope
    {
        /// <summary>
        /// Show anchors from the entire solution.
        /// </summary>
        EntireSolution,

        /// <summary>
        /// Show anchors from the current project only.
        /// </summary>
        CurrentProject,

        /// <summary>
        /// Show anchors from the current document only.
        /// </summary>
        CurrentDocument,

        /// <summary>
        /// Show anchors from all open documents.
        /// </summary>
        OpenDocuments
    }
}
