using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommentsVS.Options;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Adornments
{
    /// <summary>
    /// Handles keyboard input for rendered comments, specifically the ESC key to switch
    /// a rendered comment into raw source editing mode.
    /// </summary>
    internal sealed class TaggerKeyboardHandler : IDisposable
    {
        private readonly IWpfTextView _view;
        private readonly CommentVisibilityManager _visibilityManager;
        private bool _disposed;

        /// <summary>
        /// Raised when a refresh of tags is needed after handling keyboard input.
        /// </summary>
        public event EventHandler RefreshRequested;

        /// <summary>
        /// Initializes a new instance of the <see cref="TaggerKeyboardHandler"/> class.
        /// </summary>
        /// <param name="view">The text view to handle keyboard input for.</param>
        /// <param name="visibilityManager">The visibility manager for comment state.</param>
        public TaggerKeyboardHandler(IWpfTextView view, CommentVisibilityManager visibilityManager)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _visibilityManager = visibilityManager ?? throw new ArgumentNullException(nameof(visibilityManager));

            // Hook into keyboard events
            _view.VisualElement.PreviewKeyDown += OnViewKeyDown;

            // Add input binding for ESC key
            var escapeBinding = new KeyBinding(
                new DelegateCommand(HandleEscapeKeyInternal),
                Key.Escape,
                ModifierKeys.None);
            _view.VisualElement.InputBindings.Add(escapeBinding);
        }

        /// <summary>
        /// Public method for command handler to invoke ESC key behavior.
        /// Returns true if the ESC key was handled (hidden a comment).
        /// </summary>
        /// <param name="startLine">The starting line of the comment block.</param>
        /// <returns>True if a comment was hidden; otherwise, false.</returns>
        public bool HandleEscapeKey(int startLine)
        {
            // If comment is rendered, hide it
            if (!_visibilityManager.IsCommentHidden(startLine))
            {
                _visibilityManager.HideComment(startLine);
                RefreshRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        private void HandleEscapeKeyInternal()
        {
            if (General.Instance.CommentRenderingMode != RenderingMode.Full)
            {
                return;
            }

            var caretLine = _view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;

            // Use cached blocks for performance
            IReadOnlyList<XmlDocCommentBlock> blocks = XmlDocCommentParser.GetCachedCommentBlocks(_view.TextBuffer);
            if (blocks == null)
            {
                return;
            }

            foreach (XmlDocCommentBlock block in blocks)
            {
                // When rendered, the adornment appears on StartLine only (the block is collapsed).
                // Check if caret is on the start line of a rendered (not hidden) comment.
                if (caretLine == block.StartLine && !_visibilityManager.IsCommentHidden(block.StartLine))
                {
                    HandleEscapeKey(block.StartLine);
                    return;
                }

                // Also check if caret is within an already-hidden comment (raw source view)
                if (_visibilityManager.IsCommentHidden(block.StartLine) &&
                    caretLine >= block.StartLine && caretLine <= block.EndLine)
                {
                    // Already hidden, ESC shouldn't do anything for this block
                    return;
                }
            }
        }

        private void OnViewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && General.Instance.CommentRenderingMode == RenderingMode.Full)
            {
                // Check if caret is on a rendered comment line
                var caretLine = _view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;

                // Use cached blocks for performance
                IReadOnlyList<XmlDocCommentBlock> blocks = XmlDocCommentParser.GetCachedCommentBlocks(_view.TextBuffer);
                if (blocks != null)
                {
                    // Find if caret is on the start line of a rendered comment
                    foreach (XmlDocCommentBlock block in blocks)
                    {
                        // When rendered, the adornment appears on StartLine only (the block is collapsed).
                        // Check if caret is on the start line of a rendered (not hidden) comment.
                        if (caretLine == block.StartLine && !_visibilityManager.IsCommentHidden(block.StartLine))
                        {
                            _visibilityManager.HideComment(block.StartLine);
                            RefreshRequested?.Invoke(this, EventArgs.Empty);
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view.VisualElement.PreviewKeyDown -= OnViewKeyDown;
        }
    }

    /// <summary>
    /// Simple command implementation for keyboard bindings.
    /// </summary>
    internal sealed class DelegateCommand(Action execute) : ICommand
    {
        private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

        public event EventHandler CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute();
    }
}
