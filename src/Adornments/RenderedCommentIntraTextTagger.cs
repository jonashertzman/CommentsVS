using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CommentsVS.Commands;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.Adornments
{
    /// <summary>
    /// Simple command implementation for keyboard bindings
    /// </summary>
    internal class DelegateCommand : ICommand
    {
        private readonly Action _execute;

        public DelegateCommand(Action execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute();
    }

    [Export(typeof(IViewTaggerProvider))]
    [ContentType("CSharp")]
    [ContentType("Basic")]
    [TagType(typeof(IntraTextAdornmentTag))]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RenderedCommentIntraTextTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T> CreateTagger<T>(ITextView textView, ITextBuffer buffer) where T : ITag
        {
            if (textView == null || !(textView is IWpfTextView wpfTextView))
                return null;

            if (textView.TextBuffer != buffer)
                return null;

            return wpfTextView.Properties.GetOrCreateSingletonProperty(
                () => new RenderedCommentIntraTextTagger(wpfTextView)) as ITagger<T>;
        }
    }

    internal sealed class RenderedCommentIntraTextTagger : IntraTextAdornmentTagger<XmlDocCommentBlock, FrameworkElement>
    {
        private readonly HashSet<int> _temporarilyHiddenComments = new HashSet<int>();
        private int? _lastCaretLine;

        public RenderedCommentIntraTextTagger(IWpfTextView view) : base(view)
        {
            ToggleRenderedCommentsCommand.RenderedCommentsStateChanged += OnRenderedStateChanged;
            view.Caret.PositionChanged += OnCaretPositionChanged;

            // Listen for zoom level changes to refresh adornments with new font size
            view.ZoomLevelChanged += OnZoomLevelChanged;

            // Hook into keyboard events at multiple levels
            view.VisualElement.PreviewKeyDown += OnViewKeyDown;

            // Add input binding for ESC key
            var escapeBinding = new KeyBinding(
                new DelegateCommand(HandleEscapeKeyInternal),
                Key.Escape,
                ModifierKeys.None);
            view.VisualElement.InputBindings.Add(escapeBinding);

            // Store tagger in view properties so command handler can find it
            view.Properties[typeof(RenderedCommentIntraTextTagger)] = this;
        }

        private void OnZoomLevelChanged(object sender, ZoomLevelChangedEventArgs e)
        {
            // Refresh all adornments when zoom changes so font size updates
            RefreshTags();
        }

        /// <summary>
        /// Public method for command handler to invoke ESC key behavior.
        /// Returns true if the ESC key was handled (hidden a comment).
        /// </summary>
        public bool HandleEscapeKey(int startLine)
        {
            // If comment is rendered, hide it
            if (!_temporarilyHiddenComments.Contains(startLine))
            {
                HideCommentRendering(startLine);
                return true;
            }

            return false;
        }

        private void HandleEscapeKeyInternal()
        {
            if (General.Instance.CommentRenderingMode != RenderingMode.Full)
                return;

            var caretLine = view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);

            if (commentStyle != null)
            {
                var parser = new XmlDocCommentParser(commentStyle);
                var blocks = parser.FindAllCommentBlocks(snapshot);

                foreach (var block in blocks)
                {
                    if (caretLine >= block.StartLine && caretLine <= block.EndLine)
                    {
                        HandleEscapeKey(block.StartLine);
                        return;
                    }
                }
            }
        }

        private void OnViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && General.Instance.CommentRenderingMode == RenderingMode.Full)
            {
                // Check if caret is on a rendered comment line
                var caretLine = view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;
                var snapshot = view.TextBuffer.CurrentSnapshot;
                var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);

                if (commentStyle != null)
                {
                    var parser = new XmlDocCommentParser(commentStyle);
                    var blocks = parser.FindAllCommentBlocks(snapshot);

                    // Find if caret is within any rendered comment
                    foreach (var block in blocks)
                    {
                        if (caretLine >= block.StartLine && caretLine <= block.EndLine)
                        {
                            // If comment is rendered (not temporarily hidden), hide it
                            if (!_temporarilyHiddenComments.Contains(block.StartLine))
                            {
                                HideCommentRendering(block.StartLine);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            var currentLine = e.NewPosition.BufferPosition.GetContainingLine().LineNumber;

            // If caret moved to a different line, check if we should re-enable rendering
            if (_lastCaretLine.HasValue && _lastCaretLine.Value != currentLine)
            {
                // Check if we moved away from any temporarily hidden comments
                if (_temporarilyHiddenComments.Count > 0)
                {
                    var snapshot = view.TextBuffer.CurrentSnapshot;
                    var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);
                    if (commentStyle != null)
                    {
                        var parser = new XmlDocCommentParser(commentStyle);
                        var blocks = parser.FindAllCommentBlocks(snapshot);

                        // Find which comments should be re-enabled
                        var toReEnable = new List<int>();
                        foreach (var hiddenLine in _temporarilyHiddenComments)
                        {
                            var block = blocks.FirstOrDefault(b => b.StartLine == hiddenLine);
                            if (block != null)
                            {
                                // Check if caret is outside this comment's range
                                if (currentLine < block.StartLine || currentLine > block.EndLine)
                                {
                                    toReEnable.Add(hiddenLine);
                                }
                            }
                        }

                        // Re-enable rendering for comments the caret moved away from
                        if (toReEnable.Count > 0)
                        {
                            foreach (var line in toReEnable)
                            {
                                _temporarilyHiddenComments.Remove(line);
                            }

                            // Defer the refresh to avoid layout exceptions
#pragma warning disable VSTHRD001, VSTHRD110 // Intentional fire-and-forget for UI update
                            view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!view.IsClosed)
                                {
                                    RefreshTags();
                                }
                            }), System.Windows.Threading.DispatcherPriority.Background);
#pragma warning restore VSTHRD001, VSTHRD110
                        }
                    }
                }
            }

            _lastCaretLine = currentLine;
        }

        private void OnRenderedStateChanged(object sender, EventArgs e)
        {
            // Clear temporary hides when toggling rendered mode
            _temporarilyHiddenComments.Clear();

            // Defer refresh to avoid layout exceptions
#pragma warning disable VSTHRD001, VSTHRD110 // Intentional fire-and-forget for UI update
            view.VisualElement.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!view.IsClosed)
                {
                    var snapshot = view.TextBuffer.CurrentSnapshot;
                    RaiseTagsChanged(new SnapshotSpan(snapshot, 0, snapshot.Length));
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
#pragma warning restore VSTHRD001, VSTHRD110
        }








        protected override IEnumerable<Tuple<SnapshotSpan, PositionAffinity?, XmlDocCommentBlock>> GetAdornmentData(
            NormalizedSnapshotSpanCollection spans)
        {
            var renderingMode = General.Instance.CommentRenderingMode;

            // Only provide adornments in Compact or Full mode
            if (renderingMode != RenderingMode.Compact && renderingMode != RenderingMode.Full)
            {
                yield break;
            }

            if (spans.Count == 0)
            {
                yield break;
            }

            ITextSnapshot snapshot = spans[0].Snapshot;
            var commentStyle = LanguageCommentStyle.GetForContentType(snapshot.ContentType);

            if (commentStyle == null)
            {
                yield break;
            }

            var parser = new XmlDocCommentParser(commentStyle);
            IReadOnlyList<XmlDocCommentBlock> commentBlocks = parser.FindAllCommentBlocks(snapshot);

            foreach (XmlDocCommentBlock block in commentBlocks)
            {
                // Skip temporarily hidden comments (user pressed ESC to edit)
                if (_temporarilyHiddenComments.Contains(block.StartLine))
                {
                    continue;
                }

                var blockSpan = new SnapshotSpan(snapshot, block.Span);

                if (!spans.IntersectsWith(new NormalizedSnapshotSpanCollection(blockSpan)))
                {
                    continue;
                }

                // The adornment replaces the entire comment block span
                yield return Tuple.Create(blockSpan, (PositionAffinity?)PositionAffinity.Predecessor, block);
            }
        }

        protected override FrameworkElement CreateAdornment(XmlDocCommentBlock block, SnapshotSpan span)
        {
            var renderingMode = General.Instance.CommentRenderingMode;

            // Get editor font settings - use 1pt smaller than editor font
            var editorFontSize = view.FormattedLineSource?.DefaultTextProperties?.FontRenderingEmSize ?? 13.0;
            var fontSize = Math.Max(editorFontSize - 1.0, 8.0); // At least 8pt
            var fontFamily = view.FormattedLineSource?.DefaultTextProperties?.Typeface?.FontFamily
                ?? new FontFamily("Consolas");

            // Gray color for subtle appearance
            var textBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
            var headingBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));

            // Calculate the pixel width for the indentation margin
            var indentMargin = CalculateIndentationWidth(block.Indentation, fontFamily, editorFontSize);

            if (renderingMode == RenderingMode.Full)
            {
                return CreateFullModeAdornment(block, fontSize, fontFamily, textBrush, headingBrush, indentMargin);
            }
            else
            {
                return CreateCompactModeAdornment(block, fontSize, fontFamily, textBrush, indentMargin);
            }
        }

        /// <summary>
        /// Calculates the pixel width of the indentation string using the editor font.
        /// </summary>
        private double CalculateIndentationWidth(string indentation, FontFamily fontFamily, double fontSize)
        {
            if (string.IsNullOrEmpty(indentation))
            {
                return 0;
            }

            // Use a FormattedText to measure the width of the indentation
            var formattedText = new FormattedText(
                indentation,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                fontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(view.VisualElement).PixelsPerDip);

            return formattedText.WidthIncludingTrailingWhitespace;
        }

        private FrameworkElement CreateCompactModeAdornment(XmlDocCommentBlock block, double fontSize,
            FontFamily fontFamily, Brush textBrush, double indentMargin)
        {
            // Compact: single line with stripped summary
            var strippedSummary = XmlDocCommentRenderer.GetStrippedSummary(block);
            if (string.IsNullOrWhiteSpace(strippedSummary))
            {
                strippedSummary = "...";
            }

            var textBlock = new TextBlock
            {
                Text = strippedSummary,
                FontFamily = fontFamily,
                FontSize = fontSize,
                Foreground = textBrush,
                Background = Brushes.Transparent,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(indentMargin, 0, 0, 0),
                ToolTip = CreateTooltip(block)
            };

            return textBlock;
        }

        private FrameworkElement CreateFullModeAdornment(XmlDocCommentBlock block, double fontSize,
            FontFamily fontFamily, Brush textBrush, Brush headingBrush, double indentMargin)
        {
            RenderedComment rendered = XmlDocCommentRenderer.Render(block);

            // If only summary, use compact display
            if (!rendered.HasAdditionalSections)
            {
                return CreateCompactModeAdornment(block, fontSize, fontFamily, textBrush, indentMargin);
            }

            // Calculate line height for spacing
            var lineHeight = fontSize * 1.4;

            // Full mode: show all sections with improved formatting and whitespace
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Background = Brushes.Transparent,
                Margin = new Thickness(indentMargin, 0, 0, 0)
            };

            // Summary line (semibold for emphasis)
            var summary = XmlDocCommentRenderer.GetStrippedSummary(block);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = summary,
                    FontFamily = fontFamily,
                    FontSize = fontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = textBrush,
                    TextWrapping = TextWrapping.NoWrap,
                    Margin = new Thickness(0, 0, 0, lineHeight * 0.3) // Spacing after summary
                });
            }

            // Group params and type params
            var paramSections = rendered.AdditionalSections
                .Where(s => s.Type == CommentSectionType.Param)
                .ToList();
            var typeParamSections = rendered.AdditionalSections
                .Where(s => s.Type == CommentSectionType.TypeParam)
                .ToList();
            var otherSections = rendered.AdditionalSections
                .Where(s => s.Type != CommentSectionType.Param && s.Type != CommentSectionType.TypeParam)
                .ToList();

            // Type parameters (if any)
            if (typeParamSections.Count > 0)
            {
                foreach (RenderedCommentSection section in typeParamSections)
                {
                    AddParameterLine(panel, section, fontSize, fontFamily, textBrush, headingBrush, lineHeight);
                }
                // Add spacing after type params group if there are more sections
                if (paramSections.Count > 0 || otherSections.Count > 0)
                {
                    panel.Children.Add(CreateSpacer(lineHeight * 0.2));
                }
            }

            // Parameters (if any)
            if (paramSections.Count > 0)
            {
                foreach (RenderedCommentSection section in paramSections)
                {
                    AddParameterLine(panel, section, fontSize, fontFamily, textBrush, headingBrush, lineHeight);
                }
                // Add spacing after params group if there are more sections
                if (otherSections.Count > 0)
                {
                    panel.Children.Add(CreateSpacer(lineHeight * 0.2));
                }
            }

            // Other sections (Returns, Exceptions, Remarks, etc.)
            for (int i = 0; i < otherSections.Count; i++)
            {
                AddSectionLine(panel, otherSections[i], fontSize, fontFamily, textBrush, headingBrush, lineHeight);
            }

            return panel;
        }

        private static FrameworkElement CreateSpacer(double height)
        {
            return new Border { Height = height, Background = Brushes.Transparent };
        }

        private static void AddParameterLine(StackPanel panel, RenderedCommentSection section,
            double fontSize, FontFamily fontFamily, Brush textBrush, Brush headingBrush, double lineHeight)
        {
            var content = GetSectionContent(section);

            var textBlock = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                Foreground = textBrush,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, lineHeight * 0.1) // Small spacing between params
            };

            // Bullet + name (bold) + description
            textBlock.Inlines.Add(new Run("• ") { Foreground = textBrush });
            textBlock.Inlines.Add(new Run(section.Name ?? "")
            {
                Foreground = headingBrush,
                FontWeight = FontWeights.SemiBold
            });
            textBlock.Inlines.Add(new Run(" — " + content) { Foreground = textBrush });

            panel.Children.Add(textBlock);
        }

        private static void AddSectionLine(StackPanel panel, RenderedCommentSection section,
            double fontSize, FontFamily fontFamily, Brush textBrush, Brush headingBrush, double lineHeight)
        {
            var content = GetSectionContent(section);
            var heading = GetSectionHeading(section);

            var textBlock = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                Foreground = textBrush,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 0, lineHeight * 0.1) // Small spacing between sections
            };

            // Heading (bold) + content
            textBlock.Inlines.Add(new Run(heading)
            {
                Foreground = headingBrush,
                FontWeight = FontWeights.SemiBold
            });
            textBlock.Inlines.Add(new Run(" " + content) { Foreground = textBrush });

            panel.Children.Add(textBlock);
        }

        private static string GetSectionHeading(RenderedCommentSection section)
        {
            return section.Type switch
            {
                CommentSectionType.Returns => "Returns:",
                CommentSectionType.Exception => $"Throws {section.Name}:",
                CommentSectionType.Remarks => "Remarks:",
                CommentSectionType.Example => "Example:",
                CommentSectionType.Value => "Value:",
                CommentSectionType.SeeAlso => "See also:",
                _ => ""
            };
        }

        private static string GetSectionContent(RenderedCommentSection section)
        {
            return string.Join(" ", section.Lines
                .Where(l => !l.IsBlank)
                .SelectMany(l => l.Segments)
                .Select(s => s.Text));
        }

        private static object CreateTooltip(XmlDocCommentBlock block)
        {
            // Show the raw XML content as tooltip
            return block.XmlContent;
        }

        protected override bool UpdateAdornment(FrameworkElement adornment, XmlDocCommentBlock data)
        {
            // Always recreate to pick up font size changes
            return false;
        }

        private void HideCommentRendering(int startLine)
        {
            _temporarilyHiddenComments.Add(startLine);
            RefreshTags();
        }

        private void RefreshTags()
        {
            var snapshot = view.TextBuffer.CurrentSnapshot;
            RaiseTagsChanged(new SnapshotSpan(snapshot, 0, snapshot.Length));
        }
    }
}
