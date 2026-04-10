using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Services;
using Markdig.Syntax;

namespace GhsMarkdown.Cross.ViewModels;

public partial class TopologyNodeViewModel : ObservableObject
{
    public string Label { get; }
    public int Level { get; }
    public int SourceLine { get; }
    public int IndentDepth { get; }

    [ObservableProperty]
    private bool _isActive;

    public TopologyNodeViewModel(string label, int level, int sourceLine)
    {
        Label = label.Length > 32 ? label[..32] + "\u2026" : label;
        Level = level;
        SourceLine = sourceLine;
        IndentDepth = (level - 1) * 12;
    }
}

public partial class SectionBalanceBar : ObservableObject
{
    public string Label { get; }
    public int WordCount { get; }

    [ObservableProperty]
    private double _widthFraction;

    [ObservableProperty]
    private bool _isActive;

    public SectionBalanceBar(string label, double widthFraction, int wordCount)
    {
        Label = label.Length > 16 ? label[..16] + "\u2026" : label;
        _widthFraction = widthFraction;
        WordCount = wordCount;
    }
}

public partial class TopologyViewModel : ViewModelBase
{
    private readonly MarkdownParsingService _parsingService;
    private readonly EditorViewModel _editorVm;

    public ObservableCollection<TopologyNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<SectionBalanceBar> BalanceBars { get; } = new();

    [ObservableProperty]
    private TopologyNodeViewModel? _activeNode;

    // Event raised when a node is clicked — code-behind wires this to editor/preview navigation
    public event Action<int>? NodeClicked;

    private CancellationTokenSource? _rebuildCts;
    private CancellationTokenSource? _activeNodeCts;

    public TopologyViewModel(MarkdownParsingService parsingService, EditorViewModel editorVm)
    {
        _parsingService = parsingService;
        _editorVm = editorVm;

        // Subscribe to ParsedDocument changes → debounced rebuild
        _parsingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MarkdownParsingService.ParsedDocument))
                ScheduleRebuild();
        };

        // Subscribe to caret line changes → debounced active node update
        _editorVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.CaretLine))
                ScheduleActiveNodeUpdate(_editorVm.CaretLine);
        };
    }

    [RelayCommand]
    private void ClickNode(TopologyNodeViewModel node)
    {
        NodeClicked?.Invoke(node.SourceLine);
    }

    // ─── Debounced rebuild (400ms) ───────────────────────────────────────────

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
                var text = _parsingService.RawText;
                if (doc is null) return;

                var newNodes = ExtractHeadings(doc);
                var newBars = ComputeBalanceBars(doc, text);

                Dispatcher.UIThread.Post(() =>
                {
                    ApplyNodes(newNodes);
                    ApplyBalanceBars(newBars);
                    // Re-apply active node for current caret
                    UpdateActiveNode(_editorVm.CaretLine);
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    // ─── Debounced active node tracking (150ms) ──────────────────────────────

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
        TopologyNodeViewModel? best = null;
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

        // Update balance bar active state
        UpdateActiveBalanceBar(caretLine);
    }

    private void UpdateActiveBalanceBar(int caretLine)
    {
        // Find which H1 section the caret is in
        int activeH1Line = -1;
        foreach (var node in Nodes)
        {
            if (node.Level == 1 && node.SourceLine <= caretLine)
                activeH1Line = node.SourceLine;
            else if (node.Level == 1 && node.SourceLine > caretLine)
                break;
        }

        int barIndex = -1;
        int idx = 0;
        foreach (var node in Nodes)
        {
            if (node.Level == 1)
            {
                if (node.SourceLine == activeH1Line)
                    barIndex = idx;
                idx++;
            }
        }

        // If no H1 found but we have a "Document" bar, activate it
        if (barIndex == -1 && BalanceBars.Count == 1 && BalanceBars[0].Label == "Document")
            barIndex = 0;

        for (int i = 0; i < BalanceBars.Count; i++)
            BalanceBars[i].IsActive = (i == barIndex);
    }

    // ─── Heading extraction from AST ─────────────────────────────────────────

    private static List<TopologyNodeViewModel> ExtractHeadings(MarkdownDocument doc)
    {
        var result = new List<TopologyNodeViewModel>();
        foreach (var block in doc)
        {
            if (block is HeadingBlock heading)
            {
                var text = heading.Inline?.FirstChild?.ToString() ?? string.Empty;
                // Walk all inline children to build full heading text
                if (heading.Inline is not null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var inline in heading.Inline)
                        sb.Append(inline.ToString());
                    text = sb.ToString().Trim();
                }
                result.Add(new TopologyNodeViewModel(text, heading.Level, heading.Line + 1));
            }
        }
        return result;
    }

    // ─── Balance bar computation ─────────────────────────────────────────────

    private static List<SectionBalanceBar> ComputeBalanceBars(MarkdownDocument doc, string sourceText)
    {
        // Find H1 heading lines
        var h1Sections = new List<(string Label, int StartLine)>();
        foreach (var block in doc)
        {
            if (block is HeadingBlock { Level: 1 } heading)
            {
                var text = string.Empty;
                if (heading.Inline is not null)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var inline in heading.Inline)
                        sb.Append(inline.ToString());
                    text = sb.ToString().Trim();
                }
                h1Sections.Add((text, heading.Line + 1)); // 1-based
            }
        }

        if (h1Sections.Count == 0)
        {
            var totalWords = CountWords(sourceText);
            return new List<SectionBalanceBar>
            {
                new("Document", 1.0, totalWords)
            };
        }

        var lines = sourceText.Split('\n');
        var totalWordCount = 0;
        var sectionWordCounts = new List<(string Label, int Words)>();

        for (int i = 0; i < h1Sections.Count; i++)
        {
            var startLine = h1Sections[i].StartLine; // 1-based
            var endLine = (i + 1 < h1Sections.Count)
                ? h1Sections[i + 1].StartLine - 1
                : lines.Length;

            var sectionText = string.Join('\n',
                lines.Skip(startLine - 1).Take(endLine - startLine + 1));
            var words = CountWords(sectionText);
            totalWordCount += words;
            sectionWordCounts.Add((h1Sections[i].Label, words));
        }

        // Include any text before the first H1
        if (h1Sections[0].StartLine > 1)
        {
            var preText = string.Join('\n', lines.Take(h1Sections[0].StartLine - 1));
            totalWordCount += CountWords(preText);
        }

        if (totalWordCount == 0) totalWordCount = 1; // Avoid division by zero

        var bars = new List<SectionBalanceBar>();
        foreach (var (label, words) in sectionWordCounts)
        {
            bars.Add(new SectionBalanceBar(label, (double)words / totalWordCount, words));
        }
        return bars;
    }

    private static int CountWords(string text) =>
        text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

    // ─── Collection diffing ──────────────────────────────────────────────────

    private void ApplyNodes(List<TopologyNodeViewModel> newNodes)
    {
        // Simple diff: if structure matches (same count, same lines/levels), update in place
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
            if (structureMatch) return; // No changes needed
        }

        // Structure changed — full rebuild
        Nodes.Clear();
        foreach (var node in newNodes)
            Nodes.Add(node);
    }

    private void ApplyBalanceBars(List<SectionBalanceBar> newBars)
    {
        if (BalanceBars.Count == newBars.Count)
        {
            bool match = true;
            for (int i = 0; i < BalanceBars.Count; i++)
            {
                if (BalanceBars[i].Label != newBars[i].Label ||
                    Math.Abs(BalanceBars[i].WidthFraction - newBars[i].WidthFraction) > 0.001)
                {
                    match = false;
                    break;
                }
            }
            if (match) return;
        }

        BalanceBars.Clear();
        foreach (var bar in newBars)
            BalanceBars.Add(bar);
    }
}
