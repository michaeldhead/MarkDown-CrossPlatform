using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

/// <summary>
/// Represents a single open document tab. Owns all per-document services.
/// Shared services (ThemeService, SettingsService, etc.) are passed in
/// from the singleton container and are NOT owned by this class.
/// </summary>
public partial class TabViewModel : ObservableObject
{
    // --- Per-tab services (owned) ------------------------------------------------

    public EditorViewModel       EditorViewModel      { get; }
    public PreviewViewModel      PreviewViewModel     { get; }
    public FileService           FileService          { get; }
    public MarkdownParsingService MarkdownParsingService { get; }
    public SourceMappingService  SourceMappingService  { get; }

    // --- Tab display state -------------------------------------------------------

    /// <summary>Filename without extension, or "Untitled" if no file is open.</summary>
    public string DisplayName =>
        FileService.CurrentFilePath is null
            ? "Untitled"
            : Path.GetFileNameWithoutExtension(FileService.CurrentFilePath);

    /// <summary>True when the document has unsaved changes.</summary>
    public bool IsDirty => FileService.HasUnsavedChanges;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Last known preview scroll Y position. Saved on tab deactivation.</summary>
    public double SavedScrollY { get; set; } = 0;

    /// <summary>Last known anchor line for scroll sync. Saved on tab deactivation.</summary>
    public int SavedAnchorLine { get; set; } = -1;

    public double SavedEditorScrollY { get; set; } = 0;
    public int    SavedCaretLine     { get; set; } = 1;

    // --- Constructor (new tabs) --------------------------------------------------

    /// <summary>
    /// Construct a new tab with freshly created per-tab service instances.
    /// </summary>
    public TabViewModel(SettingsService settingsService, ThemeService themeService)
    {
        MarkdownParsingService = new MarkdownParsingService();
        EditorViewModel        = new EditorViewModel(MarkdownParsingService);
        SourceMappingService   = new SourceMappingService(MarkdownParsingService);
        PreviewViewModel       = new PreviewViewModel(MarkdownParsingService, themeService);
        FileService            = new FileService(EditorViewModel, settingsService);

        WirePropertyNotifications();
    }

    // --- Constructor (wrap existing instances) ------------------------------------

    /// <summary>
    /// Construct a tab that wraps pre-existing service instances (used for the
    /// initial tab, where services were previously resolved from DI as singletons).
    /// </summary>
    public TabViewModel(
        EditorViewModel       editorViewModel,
        PreviewViewModel      previewViewModel,
        FileService           fileService,
        MarkdownParsingService markdownParsingService,
        SourceMappingService  sourceMappingService)
    {
        EditorViewModel        = editorViewModel;
        PreviewViewModel       = previewViewModel;
        FileService            = fileService;
        MarkdownParsingService = markdownParsingService;
        SourceMappingService   = sourceMappingService;

        WirePropertyNotifications();
    }

    // --- Shared wiring -----------------------------------------------------------

    private void WirePropertyNotifications()
    {
        FileService.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(IsDirty));
        };
        EditorViewModel.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(ViewModels.EditorViewModel.DocumentText))
                OnPropertyChanged(nameof(IsDirty));
        };
        FileService.FileSaved += (_, _) =>
            OnPropertyChanged(nameof(IsDirty));
    }
}
