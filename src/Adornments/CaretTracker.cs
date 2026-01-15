using System;
using System.Collections.Generic;
using System.Linq;
using CommentsVS.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace CommentsVS.Adornments
{
    /// <summary>
    /// Tracks caret position changes and determines when the caret moves in or out of comment blocks.
    /// Coordinates with CommentVisibilityManager to restore rendered comments when the caret leaves them.
    /// </summary>
    internal sealed class CaretTracker : IDisposable
    {
        private readonly IWpfTextView _view;
        private readonly CommentVisibilityManager _visibilityManager;
        private int? _lastCaretLine;
        private bool _disposed;

        /// <summary>
        /// Raised when a refresh of tags is needed due to caret movement.
        /// </summary>
        public event EventHandler RefreshRequested;

        /// <summary>
        /// Initializes a new instance of the <see cref="CaretTracker"/> class.
        /// </summary>
        /// <param name="view">The text view to track.</param>
        /// <param name="visibilityManager">The visibility manager for comment state.</param>
        public CaretTracker(IWpfTextView view, CommentVisibilityManager visibilityManager)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _visibilityManager = visibilityManager ?? throw new ArgumentNullException(nameof(visibilityManager));

            _view.Caret.PositionChanged += OnCaretPositionChanged;
        }

        /// <summary>
        /// Gets the current caret line number.
        /// </summary>
        public int CurrentCaretLine => _view.Caret.Position.BufferPosition.GetContainingLine().LineNumber;

        /// <summary>
        /// Determines whether the caret is currently within the specified comment block.
        /// </summary>
        /// <param name="startLine">The starting line of the comment block.</param>
        /// <param name="endLine">The ending line of the comment block.</param>
        /// <returns>True if the caret is within the block; otherwise, false.</returns>
        public bool IsCaretInCommentBlock(int startLine, int endLine)
        {
            var caretLine = CurrentCaretLine;
            return caretLine >= startLine && caretLine <= endLine;
        }

        private void OnCaretPositionChanged(object sender, CaretPositionChangedEventArgs e)
        {
            var currentLine = e.NewPosition.BufferPosition.GetContainingLine().LineNumber;

            // If caret moved to a different line, check if we need to update rendering
            if (_lastCaretLine.HasValue && _lastCaretLine.Value != currentLine)
            {
                var shouldRefresh = false;

                // Use cached blocks for performance - avoids full document parse on every caret move
                IReadOnlyList<XmlDocCommentBlock> blocks = XmlDocCommentParser.GetCachedCommentBlocks(_view.TextBuffer);

                if (blocks != null)
                {
                    // Check if we moved away from any temporarily hidden comments (ESC key)
                    foreach (var hiddenLine in _visibilityManager.GetHiddenCommentLines())
                    {
                        XmlDocCommentBlock block = blocks.FirstOrDefault(b => b.StartLine == hiddenLine);
                        if (block != null)
                        {
                            // Check if caret is outside this comment's range
                            if (currentLine < block.StartLine || currentLine > block.EndLine)
                            {
                                _visibilityManager.ShowComment(hiddenLine);
                                            shouldRefresh = true;
                                        }
                                    }
                                }

                                // Check if we moved away from a recently edited comment - clear edit tracking
                                if (_visibilityManager.HasAnyRecentlyEditedLines)
                                {
                        // Find which comment block (if any) we moved away from
                        foreach (XmlDocCommentBlock block in blocks)
                        {
                            var wasInComment = _lastCaretLine.Value >= block.StartLine && _lastCaretLine.Value <= block.EndLine;
                            var nowInComment = currentLine >= block.StartLine && currentLine <= block.EndLine;

                            // If we moved out of a comment, clear the edit tracking for those lines
                            if (wasInComment && !nowInComment)
                            {
                                if (_visibilityManager.ClearEditTracking(block.StartLine, block.EndLine))
                                {
                                    shouldRefresh = true;
                                }
                            }
                        }
                    }
                }

                if (shouldRefresh)
                {
                    RefreshRequested?.Invoke(this, EventArgs.Empty);
                }
            }

            _lastCaretLine = currentLine;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _view.Caret.PositionChanged -= OnCaretPositionChanged;
        }
    }
}
