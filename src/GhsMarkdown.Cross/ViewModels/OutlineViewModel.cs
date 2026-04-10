using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Services;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.ViewModels;

public partial class OutlineNodeViewModel : ObservableObject
{
    public string Label { get; }
    public int Level { get; }
    public int SourceLine { get; }
    public int IndentDepth { get; }

    [ObservableProperty]
    private bool _isActive;

    public OutlineNodeViewModel(string label, int level, int sourceLine)
    {
        Label = label.Length > 40 ? label[..40] + "\u2026" : label;
        Level = level;
        SourceLine = sourceLine;
        IndentDepth = (level - 1) * 10;
    }
}

public partial class OutlineViewModel : ViewModelBase
{
    private readonly MarkdownParsingService _parsingService;
    private readonly EditorViewModel _editorVm;

    public ObservableCollection<OutlineNodeViewModel> Nodes { get; } = new();

    [ObservableProperty]
    private OutlineNodeViewModel? _activeNode;

    public event Action<int>? NodeClicked;

    private CancellationTokenSource? _rebuildCts;
    private CancellationTokenSource? _activeNodeCts;

    public OutlineViewModel(MarkdownParsingService parsingService, EditorViewModel editorVm)
    {
        _parsingService = parsingService;
        _editorVm = editorVm;

        _parsingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownParsingService.ParsedDocument))
                ScheduleRebuild();
        };

        _editorVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.CaretLine))
                ScheduleActiveNodeUpdate(_editorVm.CaretLine);
        };
    }

    [RelayCommand]
    private void ClickNode(OutlineNodeViewModel node)
    {
        NodeClicked?.Invoke(node.SourceLine);
    }

    private void ScheduleRebuild()
    {
        _rebuildCts?.Cancel();
        _rebuildCts = new CancellationTokenSource();
        var token = _rebuildCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, token);
                if (token.IsCancellationRequested) return;

                var doc = _parsingService.ParsedDocument;
                if (doc is null) return;

                var newNodes = ExtractHeadings(doc);

                Dispatcher.UIThread.Post(() =>
                {
                    ApplyNodes(newNodes);
                    UpdateActiveNode(_editorVm.CaretLine);
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void ScheduleActiveNodeUpdate(int caretLine)
    {
        _activeNodeCts?.Cancel();
        _activeNodeCts = new CancellationTokenSource();
        var token = _activeNodeCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() => UpdateActiveNode(caretLine));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void UpdateActiveNode(int caretLine)
    {
        OutlineNodeViewModel? best = null;
        foreach (var node in Nodes)
        {
            if (node.SourceLine <= caretLine)
                best = node;
            else
                break;
        }

        if (ActiveNode == best) return;

        if (ActiveNode is not null)
            ActiveNode.IsActive = false;

        ActiveNode = best;

        if (ActiveNode is not null)
            ActiveNode.IsActive = true;
    }

    private static List<OutlineNodeViewModel> ExtractHeadings(MarkdownDocument doc)
    {
        var result = new List<OutlineNodeViewModel>();
        foreach (var block in doc)
        {
            if (block is HeadingBlock heading)
            {
                var text = string.Empty;
                if (heading.Inline is not null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var inline in heading.Inline)
                        sb.Append(inline.ToString());
                    text = sb.ToString().Trim();
                }
                result.Add(new OutlineNodeViewModel(text, heading.Level, heading.Line + 1));
            }
        }
        return result;
    }

    private void ApplyNodes(List<OutlineNodeViewModel> newNodes)
    {
        if (Nodes.Count == newNodes.Count)
        {
            bool structureMatch = true;
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].SourceLine != newNodes[i].SourceLine ||
                    Nodes[i].Level != newNodes[i].Level ||
                    Nodes[i].Label != newNodes[i].Label)
                {
                    structureMatch = false;
                    break;
                }
            }
            if (structureMatch) return;
        }

        Nodes.Clear();
        foreach (var node in newNodes)
            Nodes.Add(node);
    }
}
