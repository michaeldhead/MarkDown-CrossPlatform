using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    public MarkdownParsingService ParsingService { get; }

    private string _documentText = string.Empty;

    public string DocumentText
    {
        get => _documentText;
        set
        {
            if (SetProperty(ref _documentText, value))
                ParsingService.RawText = value;
        }
    }

    // Caret line (1-based) — set by code-behind, observed by TopologyViewModel
    [ObservableProperty]
    private int _caretLine = 1;

    public EditorViewModel(MarkdownParsingService parsingService)
    {
        ParsingService = parsingService;
    }
}
