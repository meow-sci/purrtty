using System;
using System.Collections.Generic;
using System.Text;
using caTTY.Core.Types;
using caTTY.Display.Types;

namespace caTTY.Display.Utils;

/// <summary>
/// Utility class for extracting text from terminal cells within a selection.
/// Handles line endings, wrapped lines, and trailing space trimming.
/// </summary>
public static class TextExtractor
{
    /// <summary>
    /// Extracts text from the specified selection in the terminal viewport.
    /// </summary>
    /// <param name="selection">The text selection to extract</param>
    /// <param name="viewportRows">The viewport rows containing the terminal content</param>
    /// <param name="terminalWidth">The width of the terminal in columns</param>
    /// <param name="normalizeLineEndings">Whether to normalize line endings to \n</param>
    /// <param name="trimTrailingSpaces">Whether to trim trailing spaces from each line</param>
    /// <returns>The extracted text as a string</returns>
    public static string ExtractText(
        TextSelection selection,
        IReadOnlyList<ReadOnlyMemory<Cell>> viewportRows,
        int terminalWidth,
        bool normalizeLineEndings = true,
        bool trimTrailingSpaces = true)
    {
        if (selection.IsEmpty || viewportRows.Count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        
        // Handle single-line selection
        if (!selection.IsMultiLine)
        {
            ExtractSingleLineText(selection, viewportRows, result, trimTrailingSpaces);
        }
        else
        {
            ExtractMultiLineText(selection, viewportRows, terminalWidth, result, normalizeLineEndings, trimTrailingSpaces);
        }

        return result.ToString();
    }

    /// <summary>
    /// Extracts text from a single-line selection.
    /// </summary>
    private static void ExtractSingleLineText(
        TextSelection selection,
        IReadOnlyList<ReadOnlyMemory<Cell>> viewportRows,
        StringBuilder result,
        bool trimTrailingSpaces)
    {
        int row = selection.Start.Row;
        if (row < 0 || row >= viewportRows.Count)
        {
            return;
        }

        var rowSpan = viewportRows[row].Span;
        int startCol = Math.Max(0, selection.Start.Col);
        int endCol = Math.Min(rowSpan.Length - 1, selection.End.Col);

        if (startCol > endCol || startCol >= rowSpan.Length)
        {
            return;
        }

        // Extract characters from the selection range
        var lineText = new StringBuilder();
        for (int col = startCol; col <= endCol && col < rowSpan.Length; col++)
        {
            char ch = rowSpan[col].Character;
            lineText.Append(ch == '\0' ? ' ' : ch);
        }

        // Apply trimming if requested
        string line = trimTrailingSpaces ? lineText.ToString().TrimEnd() : lineText.ToString();
        result.Append(line);
    }

    /// <summary>
    /// Extracts text from a multi-line selection.
    /// </summary>
    private static void ExtractMultiLineText(
        TextSelection selection,
        IReadOnlyList<ReadOnlyMemory<Cell>> viewportRows,
        int terminalWidth,
        StringBuilder result,
        bool normalizeLineEndings,
        bool trimTrailingSpaces)
    {
        int startRow = Math.Max(0, selection.Start.Row);
        int endRow = Math.Min(viewportRows.Count - 1, selection.End.Row);

        for (int row = startRow; row <= endRow; row++)
        {
            if (row >= viewportRows.Count)
            {
                break;
            }

            var rowSpan = viewportRows[row].Span;
            int startCol, endCol;

            // Determine column range for this row
            if (row == startRow)
            {
                // First row: start from selection start column
                startCol = Math.Max(0, selection.Start.Col);
                endCol = rowSpan.Length - 1;
            }
            else if (row == endRow)
            {
                // Last row: end at selection end column
                startCol = 0;
                endCol = Math.Min(rowSpan.Length - 1, selection.End.Col);
            }
            else
            {
                // Middle rows: select entire row
                startCol = 0;
                endCol = rowSpan.Length - 1;
            }

            // Extract characters from this row
            var lineText = new StringBuilder();
            for (int col = startCol; col <= endCol && col < rowSpan.Length; col++)
            {
                char ch = rowSpan[col].Character;
                lineText.Append(ch == '\0' ? ' ' : ch);
            }

            // Apply trimming if requested
            string line = trimTrailingSpaces ? lineText.ToString().TrimEnd() : lineText.ToString();
            result.Append(line);

            // Add line ending if not the last row
            if (row < endRow)
            {
                if (normalizeLineEndings)
                {
                    result.Append('\n');
                }
                else
                {
                    result.Append(Environment.NewLine);
                }
            }
        }
    }

    /// <summary>
    /// Extracts text from the entire terminal viewport.
    /// Useful for copying all visible content.
    /// </summary>
    /// <param name="viewportRows">The viewport rows containing the terminal content</param>
    /// <param name="normalizeLineEndings">Whether to normalize line endings to \n</param>
    /// <param name="trimTrailingSpaces">Whether to trim trailing spaces from each line</param>
    /// <returns>The extracted text as a string</returns>
    public static string ExtractAllText(
        IReadOnlyList<ReadOnlyMemory<Cell>> viewportRows,
        bool normalizeLineEndings = true,
        bool trimTrailingSpaces = true)
    {
        if (viewportRows.Count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();

        for (int row = 0; row < viewportRows.Count; row++)
        {
            var rowSpan = viewportRows[row].Span;
            var lineText = new StringBuilder();

            // Extract all characters from this row
            for (int col = 0; col < rowSpan.Length; col++)
            {
                char ch = rowSpan[col].Character;
                lineText.Append(ch == '\0' ? ' ' : ch);
            }

            // Apply trimming if requested
            string line = trimTrailingSpaces ? lineText.ToString().TrimEnd() : lineText.ToString();
            result.Append(line);

            // Add line ending if not the last row
            if (row < viewportRows.Count - 1)
            {
                if (normalizeLineEndings)
                {
                    result.Append('\n');
                }
                else
                {
                    result.Append(Environment.NewLine);
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Determines if a line is likely wrapped (continues on the next line without an explicit line break).
    /// This is a simple heuristic that can be enhanced based on terminal behavior.
    /// </summary>
    /// <param name="rowSpan">The row span to check</param>
    /// <param name="terminalWidth">The width of the terminal</param>
    /// <returns>True if the line appears to be wrapped, false otherwise</returns>
    public static bool IsLineWrapped(ReadOnlySpan<Cell> rowSpan, int terminalWidth)
    {
        // Simple rule: if the row is full width and the last character is not whitespace,
        // it's likely wrapped to the next line
        if (rowSpan.Length < terminalWidth)
        {
            return false;
        }

        // Check if the last character is non-whitespace
        char lastChar = rowSpan[terminalWidth - 1].Character;
        return lastChar != ' ' && lastChar != '\0' && lastChar != '\t';
    }

    /// <summary>
    /// Extracts text with enhanced wrapped line handling.
    /// This version attempts to detect wrapped lines and join them appropriately.
    /// </summary>
    /// <param name="selection">The text selection to extract</param>
    /// <param name="viewportRows">The viewport rows containing the terminal content</param>
    /// <param name="terminalWidth">The width of the terminal in columns</param>
    /// <param name="normalizeLineEndings">Whether to normalize line endings to \n</param>
    /// <param name="trimTrailingSpaces">Whether to trim trailing spaces from each line</param>
    /// <returns>The extracted text with wrapped lines handled</returns>
    public static string ExtractTextWithWrappedLines(
        TextSelection selection,
        IReadOnlyList<ReadOnlyMemory<Cell>> viewportRows,
        int terminalWidth,
        bool normalizeLineEndings = true,
        bool trimTrailingSpaces = true)
    {
        if (selection.IsEmpty || viewportRows.Count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        int startRow = Math.Max(0, selection.Start.Row);
        int endRow = Math.Min(viewportRows.Count - 1, selection.End.Row);

        for (int row = startRow; row <= endRow; row++)
        {
            if (row >= viewportRows.Count)
            {
                break;
            }

            var rowSpan = viewportRows[row].Span;
            int startCol, endCol;

            // Determine column range for this row
            if (row == startRow && row == endRow)
            {
                // Single row selection
                startCol = Math.Max(0, selection.Start.Col);
                endCol = Math.Min(rowSpan.Length - 1, selection.End.Col);
            }
            else if (row == startRow)
            {
                // First row: start from selection start column
                startCol = Math.Max(0, selection.Start.Col);
                endCol = rowSpan.Length - 1;
            }
            else if (row == endRow)
            {
                // Last row: end at selection end column
                startCol = 0;
                endCol = Math.Min(rowSpan.Length - 1, selection.End.Col);
            }
            else
            {
                // Middle rows: select entire row
                startCol = 0;
                endCol = rowSpan.Length - 1;
            }

            // Extract characters from this row
            var lineText = new StringBuilder();
            for (int col = startCol; col <= endCol && col < rowSpan.Length; col++)
            {
                char ch = rowSpan[col].Character;
                lineText.Append(ch == '\0' ? ' ' : ch);
            }

            // Apply trimming if requested
            string line = trimTrailingSpaces ? lineText.ToString().TrimEnd() : lineText.ToString();
            result.Append(line);

            // Add line ending if not the last row
            if (row < endRow)
            {
                // Check if this line is wrapped to the next line
                bool isWrapped = IsLineWrapped(rowSpan, terminalWidth);
                
                if (isWrapped)
                {
                    // Don't add a line break for wrapped lines
                    // The text continues on the next line
                }
                else
                {
                    // Add explicit line break for non-wrapped lines
                    if (normalizeLineEndings)
                    {
                        result.Append('\n');
                    }
                    else
                    {
                        result.Append(Environment.NewLine);
                    }
                }
            }
        }

        return result.ToString();
    }
}