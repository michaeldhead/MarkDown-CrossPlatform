using System.Text.RegularExpressions;
using AvaloniaEdit;
using GhsMarkdown.Cross.Models;

namespace GhsMarkdown.Cross.Services;

public partial class SnippetInsertionService
{
    private readonly SnippetModeController _controller;

    public SnippetInsertionService(SnippetModeController controller)
    {
        _controller = controller;
    }

    public void InsertSnippet(Snippet snippet, TextEditor editor)
    {
        var body = snippet.Body;
        var caretOffset = editor.TextArea.Caret.Offset;

        // Find all $N tokens
        var matches = TabStopRegex().Matches(body);
        if (matches.Count == 0)
        {
            // No fields — just insert text
            editor.Document.Insert(caretOffset, body);
            editor.TextArea.Caret.Offset = caretOffset + body.Length;
            return;
        }

        // Collect unique field numbers and their positions in the body
        var tokenPositions = new List<(int Index, int Length, int FieldNum)>();
        foreach (Match m in matches)
        {
            var fieldNum = int.Parse(m.Groups[1].Value);
            tokenPositions.Add((m.Index, m.Length, fieldNum));
        }

        // Sort by position (left to right) for removal
        tokenPositions.Sort((a, b) => a.Index.CompareTo(b.Index));

        // Build clean text and record field positions
        var cleanText = new System.Text.StringBuilder();
        var fieldOffsets = new List<(int FieldNum, int Start, int End)>();
        int srcPos = 0;
        int delta = 0;

        foreach (var (index, length, fieldNum) in tokenPositions)
        {
            // Append text before this token
            cleanText.Append(body, srcPos, index - srcPos);
            var fieldStart = caretOffset + index - delta;
            // The field has zero length (placeholder position)
            fieldOffsets.Add((fieldNum, fieldStart, fieldStart));
            delta += length;
            srcPos = index + length;
        }
        // Append remaining text
        cleanText.Append(body, srcPos, body.Length - srcPos);

        var cleanStr = cleanText.ToString();
        editor.Document.Insert(caretOffset, cleanStr);

        // Sort fields: $1, $2, ... $9, then $0 last
        fieldOffsets.Sort((a, b) =>
        {
            if (a.FieldNum == 0) return 1;
            if (b.FieldNum == 0) return -1;
            return a.FieldNum.CompareTo(b.FieldNum);
        });

        // Deduplicate by field number (take first occurrence)
        var seen = new HashSet<int>();
        var fields = new List<(int Start, int End)>();
        foreach (var (fieldNum, start, end) in fieldOffsets)
        {
            if (seen.Add(fieldNum))
                fields.Add((start, end));
        }

        if (fields.Count > 0)
        {
            _controller.BeginSession(editor.Document, fields);
            _controller.MoveNext(editor);
        }
        else
        {
            editor.TextArea.Caret.Offset = caretOffset + cleanStr.Length;
        }
    }

    [GeneratedRegex(@"\$(\d)")]
    private static partial Regex TabStopRegex();
}
