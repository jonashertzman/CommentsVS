using System.ComponentModel;
using System.Runtime.InteropServices;
using CommentsVS.Services;

namespace CommentsVS.Options
{
    /// <summary>
    /// Options provider for the CommentsVS extension.
    /// </summary>
    internal partial class OptionsProvider
    {
        [ComVisible(true)]
        public class GeneralOptions : BaseOptionPage<General> { }
    }

    /// <summary>
    /// Settings model for XML Doc Comment formatting options.
    /// </summary>
    public class General : BaseOptionModel<General>
    {
        private const string ReflowCategory = "Comment Reflow";

        [Category(ReflowCategory)]
        [DisplayName("Maximum Line Length")]
        [Description("The maximum line length for XML documentation comments before wrapping to a new line. Default is 120.")]
        [DefaultValue(120)]
        public int MaxLineLength { get; set; } = 120;

        [Category(ReflowCategory)]
        [DisplayName("Enable Reflow on Format Document")]
        [Description("When enabled, XML documentation comments will be reflowed when using Format Document (Ctrl+K, Ctrl+D).")]
        [DefaultValue(true)]
        public bool ReflowOnFormatDocument { get; set; } = true;

        [Category(ReflowCategory)]
        [DisplayName("Enable Reflow on Paste")]
        [Description("When enabled, XML documentation comments will be automatically reflowed when pasting text into a comment block.")]
        [DefaultValue(true)]
        public bool ReflowOnPaste { get; set; } = true;

        [Category(ReflowCategory)]
        [DisplayName("Enable Reflow While Typing")]
        [Description("When enabled, XML documentation comments will be automatically reflowed as you type when a line exceeds the maximum length.")]
        [DefaultValue(true)]
        public bool ReflowOnTyping { get; set; } = true;

        [Category(ReflowCategory)]
        [DisplayName("Use Compact Style for Short Summaries")]
        [Description("When enabled, short summaries that fit on one line will use the compact format: /// <summary>Short text.</summary>")]
        [DefaultValue(true)]
        public bool UseCompactStyleForShortSummaries { get; set; } = true;

        [Category(ReflowCategory)]
        [DisplayName("Preserve Blank Lines")]
        [Description("When enabled, blank lines within XML documentation comments (paragraph separators) will be preserved during reflow.")]
        [DefaultValue(true)]
        public bool PreserveBlankLines { get; set; } = true;

        private const string OutliningCategory = "Comment Outlining";

        [Category(OutliningCategory)]
        [DisplayName("Collapse Comments on File Open")]
        [Description("When enabled, XML documentation comments will be automatically collapsed when opening a file.")]
        [DefaultValue(false)]
        public bool CollapseCommentsOnFileOpen { get; set; } = false;

        /// <summary>
        /// Creates a CommentReflowEngine configured with the current options.
        /// </summary>
        public CommentReflowEngine CreateReflowEngine()
        {
            return new CommentReflowEngine(
                MaxLineLength,
                UseCompactStyleForShortSummaries,
                PreserveBlankLines);
        }
    }
}
