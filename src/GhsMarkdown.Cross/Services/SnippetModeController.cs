using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace GhsMarkdown.Cross.Services;

public class SnippetModeController
{
    private List<(TextAnchor Start, TextAnchor End)> _fields = new();
    private int _fieldIndex = -1;

    public bool IsActive { get; private set; }

    public void BeginSession(TextDocument document, List<(int Start, int End)> fields)
    {
        Exit();

        _fields = new List<(TextAnchor, TextAnchor)>();
        foreach (var (start, end) in fields)
        {
            var startAnchor = document.CreateAnchor(start);
            startAnchor.MovementType = AnchorMovementType.BeforeInsertion;
            var endAnchor = document.CreateAnchor(end);
            endAnchor.MovementType = AnchorMovementType.AfterInsertion;
            _fields.Add((startAnchor, endAnchor));
        }

        _fieldIndex = -1;
        IsActive = _fields.Count > 0;
    }

    public bool MoveNext(TextEditor editor)
    {
        if (!IsActive || _fields.Count == 0) return false;

        _fieldIndex++;

        if (_fieldIndex >= _fields.Count)
        {
            Exit();
            return false;
        }

        var (start, end) = _fields[_fieldIndex];
        var startOffset = start.Offset;
        var length = Math.Max(0, end.Offset - startOffset);
        editor.TextArea.Caret.Offset = startOffset;
        editor.Select(startOffset, length);

        if (_fieldIndex == _fields.Count - 1)
        {
            // Last field — next Tab will exit
        }

        return true;
    }

    public void Exit()
    {
        IsActive = false;
        _fields.Clear();
        _fieldIndex = -1;
    }
}
