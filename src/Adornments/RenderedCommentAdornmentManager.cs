using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CommentsVS.Commands;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Outlining;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace CommentsVS.Adornments
{
/// <summary>
/// Provides the adornment layer for rendered comments and creates the adornment manager.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener))]
[ContentType("CSharp")]
[ContentType("Basic")]
[TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class RenderedCommentAdornmentManagerProvider : IWpfTextViewCreationListener
{
    [Export(typeof(AdornmentLayerDefinition))]
    [Name("RenderedCommentAdornment")]
    [Order(After = PredefinedAdornmentLayers.Text, Before = PredefinedAdornmentLayers.Caret)]
    internal AdornmentLayerDefinition EditorAdornmentLayer = null;

    [Import]
    internal IOutliningManagerService OutliningManagerService { get; set; }

    [Import]
    internal IViewTagAggregatorFactoryService TagAggregatorFactoryService { get; set; }

    [Import]
    internal IEditorFormatMapService EditorFormatMapService { get; set; }

    public void TextViewCreated(IWpfTextView textView)
    {
        // Create the adornment manager for this view
        var formatMap = EditorFormatMapService?.GetEditorFormatMap(textView);
        textView.Properties.GetOrCreateSingletonProperty(
            () => new RenderedCommentAdornmentManager(
                textView, 
                OutliningManagerService,
                formatMap));
    }
}

    /// <summary>
    /// Manages rendered comment adornments that overlay collapsed outlining regions.
    /// </summary>
    internal sealed class RenderedCommentAdornmentManager : IDisposable
    {
        private readonly IWpfTextView _textView;
        private readonly IEditorFormatMap _editorFormatMap;
        private readonly IAdornmentLayer _adornmentLayer;
        private readonly IOutliningManager _outliningManager;
        private bool _disposed;

        public RenderedCommentAdornmentManager(
            IWpfTextView textView,
            IOutliningManagerService outliningManagerService,
            IEditorFormatMap editorFormatMap)
        {
            _textView = textView;
            _editorFormatMap = editorFormatMap;
            _adornmentLayer = textView.GetAdornmentLayer("RenderedCommentAdornment");
            _outliningManager = outliningManagerService?.GetOutliningManager(textView);

            _textView.LayoutChanged += OnLayoutChanged;
            _textView.Closed += OnViewClosed;

            if (_outliningManager != null)
            {
                _outliningManager.RegionsCollapsed += OnRegionsCollapsed;
                _outliningManager.RegionsExpanded += OnRegionsExpanded;
            }

            ToggleRenderedCommentsCommand.RenderedCommentsStateChanged += OnRenderedStateChanged;
        }

        private void OnRegionsCollapsed(object sender, RegionsCollapsedEventArgs e)
        {
            DeferredUpdateAdornments();
        }

        private void OnRegionsExpanded(object sender, RegionsExpandedEventArgs e)
        {
            DeferredUpdateAdornments();
        }

        private void OnRenderedStateChanged(object sender, EventArgs e)
        {
            DeferredUpdateAdornments();
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            DeferredUpdateAdornments();
        }

        private void DeferredUpdateAdornments()
        {
            // Defer to avoid layout exceptions when called during layout
#pragma warning disable VSTHRD001, VSTHRD110 // Intentional fire-and-forget for UI update
            _textView.VisualElement.Dispatcher.BeginInvoke(
                new Action(UpdateAdornments),
                System.Windows.Threading.DispatcherPriority.Background);
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private void UpdateAdornments()
        {
            if (_disposed || _textView.IsClosed)
                return;

            _adornmentLayer.RemoveAllAdornments();

            // In Compact/Full mode, IntraTextAdornment handles display
            // This manager is no longer needed for those modes
            // Keep this for potential future overlay needs
        }

        /// <summary>
        /// Gets the editor background brush from the editor format map.
        /// </summary>
        private Brush GetEditorBackgroundBrush()
        {
            // Try to get from the editor format map (most reliable source)
            if (_editorFormatMap != null)
            {
                // Try TextView Background first (this is usually the correct one)
                var textViewProps = _editorFormatMap.GetProperties("TextView Background");
                if (textViewProps != null && textViewProps.Contains("BackgroundColor"))
                {
                    var bgColor = (Color)textViewProps["BackgroundColor"];
                    if (bgColor.A > 0) // Has some opacity
                    {
                        return new SolidColorBrush(bgColor);
                    }
                }

                // Try Plain Text background
                var plainTextProps = _editorFormatMap.GetProperties("Plain Text");
                if (plainTextProps != null && plainTextProps.Contains("BackgroundColor"))
                {
                    var bgColor = (Color)plainTextProps["BackgroundColor"];
                    if (bgColor.A > 0)
                    {
                        return new SolidColorBrush(bgColor);
                    }
                }
            }

            // Fallback: detect dark vs light theme based on foreground color
            var defaultProps = _textView.FormattedLineSource?.DefaultTextProperties;
            var foreground = defaultProps?.ForegroundBrush as SolidColorBrush;
            if (foreground != null)
            {
                // If foreground is light (luminance > 128), we're in dark theme
                var luminance = 0.299 * foreground.Color.R + 0.587 * foreground.Color.G + 0.114 * foreground.Color.B;
                if (luminance > 128)
                {
                    // Dark theme - use VS dark background
                    return new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
                else
                {
                    // Light theme - use white
                    return Brushes.White;
                }
            }

            // Ultimate fallback to white
            return Brushes.White;
        }

        private void OnViewClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _textView.LayoutChanged -= OnLayoutChanged;
            _textView.Closed -= OnViewClosed;

            if (_outliningManager != null)
            {
                _outliningManager.RegionsCollapsed -= OnRegionsCollapsed;
                _outliningManager.RegionsExpanded -= OnRegionsExpanded;
            }

            ToggleRenderedCommentsCommand.RenderedCommentsStateChanged -= OnRenderedStateChanged;
        }
    }
}

