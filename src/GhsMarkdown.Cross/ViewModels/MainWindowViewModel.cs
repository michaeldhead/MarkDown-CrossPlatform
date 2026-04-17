using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public enum ViewMode { Edit, Split, Preview }
public enum GutterSyncState { Synced, Drifted, Saved }

public partial class MainWindowViewModel : ObservableObject
{
    private readonly ThemeService    _themeService;
    private readonly SettingsService _settingsService;
    private readonly CommandRegistry _commandRegistry;
    private readonly TopologyViewModel _topologyVm;
    private readonly OutlineViewModel _outlineVm;
    private readonly SnippetStudioViewModel _snippetStudioVm;
    private readonly AiAssistViewModel _aiAssistVm;
    private readonly SnapshotService _snapshotService;
    private readonly FileBrowserViewModel _fileBrowserVm;
    private readonly SnippetService _snippetService;
    private System.Threading.Timer? _snapshotTimer;
    private System.Threading.Timer? _draftTimer;
    private ViewMode? _viewModeBeforeExport;

    // --- Tabs ---------------------------------------------------------------

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    private TabViewModel _activeTab = null!;
    public TabViewModel ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (SetProperty(ref _activeTab, value))
            {
                foreach (var t in Tabs)
                    t.IsActive = t == value;
            }
        }
    }

    // ─── Icon rail ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLeftPanelOpen = true;
    [ObservableProperty] private bool   _isRightPanelOpen = false;
    [ObservableProperty] private bool   _isSettingsOpen = false;
    [ObservableProperty] private double _leftPanelWidth = 220.0;
    [ObservableProperty] private double _rightPanelWidth = 0.0;
    private double _rightPanelOpenWidth = 200.0;

    private string _activeIcon = "Topology";
    public string ActiveIcon
    {
        get => _activeIcon;
        private set
        {
            if (SetProperty(ref _activeIcon, value))
            {
                OnPropertyChanged(nameof(IsTopologyActive));
                OnPropertyChanged(nameof(IsFilesActive));
                OnPropertyChanged(nameof(IsSnippetsActive));
                OnPropertyChanged(nameof(IsSearchActive));
            }
        }
    }

    public bool IsTopologyActive => _activeIcon == "Topology" && IsLeftPanelOpen && !IsSettingsOpen;
    public bool IsFilesActive    => _activeIcon == "Files"    && IsLeftPanelOpen && !IsSettingsOpen;
    public bool IsSnippetsActive => _activeIcon == "Snippets" && IsLeftPanelOpen && !IsSettingsOpen;
    public bool IsSearchActive   => _activeIcon == "Search"   && IsLeftPanelOpen && !IsSettingsOpen;

    // ─── Theme ───────────────────────────────────────────────────────────────

    [ObservableProperty] private string _currentThemeName = "GHS Dark";

    public bool IsThemeDark   => _themeService.CurrentTheme == GhsTheme.Dark;
    public bool IsThemeLight  => _themeService.CurrentTheme == GhsTheme.Light;
    public bool IsThemeCustom => _themeService.CurrentTheme == GhsTheme.Custom;
    public bool IsThemeAuto   => _themeService.CurrentTheme == GhsTheme.Auto;

    // ─── Window title ────────────────────────────────────────────────────────

    [ObservableProperty] private string _windowTitle = "GHS Markdown Editor — Untitled";

    // ─── View mode ───────────────────────────────────────────────────────────

    [ObservableProperty] private ViewMode _currentViewMode = ViewMode.Split;

    public bool EditorPaneVisible  => CurrentViewMode != ViewMode.Preview;
    public bool GutterVisible      => CurrentViewMode == ViewMode.Split;
    public bool PreviewPaneVisible => CurrentViewMode != ViewMode.Edit;

    public bool IsEditMode    => CurrentViewMode == ViewMode.Edit;
    public bool IsSplitMode   => CurrentViewMode == ViewMode.Split;
    public bool IsPreviewMode => CurrentViewMode == ViewMode.Preview;

    partial void OnCurrentViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(EditorPaneVisible));
        OnPropertyChanged(nameof(GutterVisible));
        OnPropertyChanged(nameof(PreviewPaneVisible));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsSplitMode));
        OnPropertyChanged(nameof(IsPreviewMode));
        SaveSettings();
    }

    // ─── Smart Gutter ─────────────────────────────────────────────────────────

    [ObservableProperty] private double _splitRatio = 0.5;
    [ObservableProperty] private GutterSyncState _gutterSyncState = GutterSyncState.Synced;
    [ObservableProperty] private int _gutterWordCount = 0;
    [ObservableProperty] private string _statusBarText = "Ln 1 · Col 1 · 0 words";

    public IBrush GutterDotBrush
    {
        get
        {
            var color = GutterSyncState switch
            {
                GutterSyncState.Saved   => Color.FromRgb(0x5A, 0xB8, 0x65), // Green
                GutterSyncState.Drifted => Color.FromRgb(0xEF, 0x9F, 0x27), // Amber
                _                       => Color.FromRgb(0x4A, 0x9E, 0xFF)  // Blue
            };
            return new SolidColorBrush(color);
        }
    }

    partial void OnGutterSyncStateChanged(GutterSyncState value)
    {
        OnPropertyChanged(nameof(GutterDotBrush));
    }

    // ─── Panel slots ────────────────────────────────────────────────────────

    private readonly PlaceholderPanelViewModel _placeholderVm = new();
    public PanelSlotViewModel LeftPanelSlot { get; } = new PanelSlotViewModel();
    public PanelSlotViewModel RightPanelSlot { get; } = new PanelSlotViewModel();

    // ─── Settings properties (Phase 9) ─────────────────────────────────────

    private string _editorFontFamily = "Cascadia Code, Consolas, monospace";
    public string EditorFontFamily
    {
        get => _editorFontFamily;
        set { if (SetProperty(ref _editorFontFamily, value)) { EditorFontChanged?.Invoke(this, EventArgs.Empty); SaveSettings(); } }
    }

    private double _editorFontSize = 14.0;
    public double EditorFontSize
    {
        get => _editorFontSize;
        set { if (SetProperty(ref _editorFontSize, Math.Clamp(value, 10, 24))) { EditorFontChanged?.Invoke(this, EventArgs.Empty); SaveSettings(); } }
    }

    private int _autoSaveIntervalSeconds = 60;
    public int AutoSaveIntervalSeconds
    {
        get => _autoSaveIntervalSeconds;
        set
        {
            if (SetProperty(ref _autoSaveIntervalSeconds, Math.Clamp(value, 30, 3600)))
            {
                var interval = TimeSpan.FromSeconds(_autoSaveIntervalSeconds);
                _draftTimer?.Change(interval, interval);
                SaveSettings();
            }
        }
    }

    private string _snippetLibraryPath = "";
    public string SnippetLibraryPath
    {
        get => _snippetLibraryPath;
        set { if (SetProperty(ref _snippetLibraryPath, value ?? "")) { SaveSettings(); _ = _snippetService.LoadAsync(value); } }
    }

    public event EventHandler? EditorFontChanged;

    public event EventHandler? CustomColorsReset;

    // ─── Custom Theme Colors ────────────────────────────────────────────────

    [ObservableProperty]
    private Dictionary<string, string> _customColors = new()
    {
        ["bg-shell"]         = "#1E1E1E",
        ["bg-panel"]         = "#252526",
        ["bg-toolbar"]       = "#2D2D2D",
        ["bg-editor"]        = "#1E1E1E",
        ["bg-preview"]       = "#212121",
        ["bg-gutter"]        = "#232323",
        ["accent"]           = "#4A9EFF",
        ["border"]           = "#3E3E42",
        ["text-primary"]     = "#E8E8E8",
        ["text-secondary"]   = "#ADADAD",
        ["text-hint"]        = "#909090",
        ["syntax-h1"]        = "#4A9EFF",
        ["syntax-h2"]        = "#5AB865",
        ["syntax-h3"]        = "#B8954A",
        ["syntax-h4"]        = "#888888",
        ["syntax-h5"]        = "#666666",
        ["syntax-h6"]        = "#555555",
        ["syntax-bold"]      = "#E8E8E8",
        ["syntax-italic"]    = "#D0D0D0",
        ["syntax-code"]      = "#C792EA",
        ["syntax-blockquote"] = "#ADADAD"
    };

    [RelayCommand]
    private void SetCustomColor(string colorSpec)
    {
        // colorSpec format: "key:hex" e.g. "accent:#FF0000"
        var parts = colorSpec.Split(':', 2);
        if (parts.Length != 2) return;
        var key = parts[0];
        var hex = parts[1];

        CustomColors[key] = hex;
        OnPropertyChanged(nameof(CustomColors));

        // Save to settings
        var s = _settingsService.Load();
        var colors = new Dictionary<string, string>(s.CustomThemeColors)
            { [key] = hex };
        _settingsService.Save(s with { CustomThemeColors = colors });

        // Chrome first, then WebView CSS
        _themeService.ApplyCustomColorsToChrome(CustomColors);
        _themeService.NotifyThemeChanged();
    }

    [RelayCommand]
    private void ResetCustomTheme()
    {
        CustomColors = new Dictionary<string, string>
        {
            ["bg-shell"]         = "#1E1E1E",
            ["bg-panel"]         = "#252526",
            ["bg-toolbar"]       = "#2D2D2D",
            ["bg-editor"]        = "#1E1E1E",
            ["bg-preview"]       = "#212121",
            ["bg-gutter"]        = "#232323",
            ["accent"]           = "#4A9EFF",
            ["border"]           = "#3E3E42",
            ["text-primary"]     = "#E8E8E8",
            ["text-secondary"]   = "#ADADAD",
            ["text-hint"]        = "#909090",
            ["syntax-h1"]        = "#4A9EFF",
            ["syntax-h2"]        = "#5AB865",
            ["syntax-h3"]        = "#B8954A",
            ["syntax-h4"]        = "#888888",
            ["syntax-h5"]        = "#666666",
            ["syntax-h6"]        = "#555555",
            ["syntax-bold"]      = "#E8E8E8",
            ["syntax-italic"]    = "#D0D0D0",
            ["syntax-code"]      = "#C792EA",
            ["syntax-blockquote"] = "#ADADAD"
        };
        var s = _settingsService.Load();
        _settingsService.Save(s with
            { CustomThemeColors = new Dictionary<string, string>(CustomColors) });

        // Apply defaults to chrome AND re-inject preview CSS
        _themeService.ApplyCustomColorsToChrome(CustomColors);
        _themeService.NotifyThemeChanged();
        CustomColorsReset?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetCustomThemeToLight()
    {
        CustomColors = new Dictionary<string, string>
        {
            ["bg-shell"]         = "#F9F6F0",
            ["bg-panel"]         = "#F2EFE8",
            ["bg-toolbar"]       = "#EAE7E0",
            ["bg-editor"]        = "#F9F6F0",
            ["bg-preview"]       = "#F4F1EB",
            ["bg-gutter"]        = "#EDE9E2",
            ["accent"]           = "#1A6BC4",
            ["border"]           = "#D8D4CC",
            ["text-primary"]     = "#1A1A1A",
            ["text-secondary"]   = "#5A5A5A",
            ["text-hint"]        = "#999999",
            ["syntax-h1"]        = "#1A6BC4",
            ["syntax-h2"]        = "#2E7D32",
            ["syntax-h3"]        = "#7B5E20",
            ["syntax-h4"]        = "#5A5A5A",
            ["syntax-h5"]        = "#777777",
            ["syntax-h6"]        = "#888888",
            ["syntax-bold"]      = "#1A1A1A",
            ["syntax-italic"]    = "#555566",
            ["syntax-code"]      = "#7C3AED",
            ["syntax-blockquote"] = "#5A5A5A"
        };
        var s = _settingsService.Load();
        _settingsService.Save(s with
            { CustomThemeColors = new Dictionary<string, string>(CustomColors) });

        _themeService.ApplyCustomColorsToChrome(CustomColors);
        _themeService.NotifyThemeChanged();
        CustomColorsReset?.Invoke(this, EventArgs.Empty);
    }

    // ─── AI Assist API Key ──────────────────────────────────────────────────

    private string _anthropicApiKey = "";
    public string AnthropicApiKey
    {
        get => _anthropicApiKey;
        set
        {
            if (SetProperty(ref _anthropicApiKey, value))
            {
                var s = _settingsService.Load();
                _settingsService.Save(s with { AnthropicApiKey = value });
                _aiAssistVm.RefreshConfiguration();
            }
        }
    }

    [ObservableProperty] private string _apiKeyStatus = "";

    [RelayCommand]
    private async Task TestApiKey()
    {
        ApiKeyStatus = "Testing...";
        var ok = await _aiAssistVm.TestConnectionAsync();
        ApiKeyStatus = ok ? "Connected" : "Failed - check key";
    }

    public IEnumerable<CommandDescriptor> ShortcutCommands =>
        _commandRegistry.GetAll()
            .Where(c => c.KeyboardShortcut is not null)
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Title);

    // ─── Right panel slot switcher ───────────────────────────────────────────

    public bool IsOutlineSlotActive  => RightPanelSlot.ActiveOccupant is OutlineViewModel;
    public bool IsSnippetsSlotActive => RightPanelSlot.ActiveOccupant is SnippetStudioViewModel;
    public bool IsAiAssistSlotActive => RightPanelSlot.ActiveOccupant is AiAssistViewModel;

    public string RightPanelSlotName => RightPanelSlot.ActiveOccupant switch
    {
        OutlineViewModel              => "Outline",
        SnippetStudioViewModel        => "Snippets",
        AiAssistViewModel  => "AI Assist",
        _                             => "Panel"
    };

    [RelayCommand]
    private void SwitchToOutline()   => RightPanelSlot.SwapOccupant(_outlineVm);

    [RelayCommand]
    private void SwitchToSnippets()  => RightPanelSlot.SwapOccupant(_snippetStudioVm);

    [RelayCommand]
    private void SwitchToAiAssist()  => RightPanelSlot.SwapOccupant(_aiAssistVm);

    // ─── Command Palette ────────────────────────────────────────────────────

    public CommandPaletteViewModel CommandPalette { get; }

    // ─── Timeline ─────────────────────────────────────────────────────────

    public TimelineViewModel Timeline { get; }

    // ─── Export Panel ─────────────────────────────────────────────────────

    public ExportPanelViewModel ExportPanel { get; }

    [RelayCommand]
    private void OpenExport() => ExportPanel.Open();

    // ─── Print ──────────────────────────────────────────────────────────────

    private Action? _printAction;
    public IRelayCommand PrintCommand { get; }

    public void SetPrintAction(Action printAction) => _printAction = printAction;

    // ─── Focus Mode ─────────────────────────────────────────────────────────

    private Action<bool>? _focusModeAction;
    public bool IsFocusModeActive { get; private set; }
    public IRelayCommand ToggleFocusModeCommand { get; }

    public void SetFocusModeAction(Action<bool> action) => _focusModeAction = action;

    // ─── Detach Preview ─────────────────────────────────────────────────────

    private Action? _detachPreviewAction;
    [ObservableProperty] private bool _isPreviewDetached = false;
    public IRelayCommand DetachPreviewCommand { get; }
    public void SetDetachPreviewAction(Action action) => _detachPreviewAction = action;

    // ─── Focus Editor ───────────────────────────────────────────────────────

    private Action? _focusEditorAction;
    public void SetFocusEditorAction(Action action) => _focusEditorAction = action;

    // ─── Formatting Toolbar ──────────────────────────────────────────────────

    public bool ShowFormattingToolbar { get; private set; }
    public IRelayCommand ToggleFormattingToolbarCommand { get; }

    [ObservableProperty] private bool _wordWrap = false;
    [ObservableProperty] private bool _showLineNumbers = true;
    [ObservableProperty] private bool _highlightCurrentLine = true;

    partial void OnWordWrapChanged(bool value)
    {
        var s = _settingsService.Load();
        _settingsService.Save(s with { WordWrap = value });
    }

    partial void OnShowLineNumbersChanged(bool value)
    {
        var s = _settingsService.Load();
        _settingsService.Save(s with { ShowLineNumbers = value });
    }

    partial void OnHighlightCurrentLineChanged(bool value)
    {
        var s = _settingsService.Load();
        _settingsService.Save(s with { HighlightCurrentLine = value });
    }

    [RelayCommand] private void ToggleWordWrap()             => WordWrap             = !WordWrap;
    [RelayCommand] private void ToggleShowLineNumbers()      => ShowLineNumbers      = !ShowLineNumbers;
    [RelayCommand] private void ToggleHighlightCurrentLine() => HighlightCurrentLine = !HighlightCurrentLine;

    public IRelayCommand FormatBoldCommand          { get; }
    public IRelayCommand FormatItalicCommand        { get; }
    public IRelayCommand FormatStrikethroughCommand { get; }
    public IRelayCommand FormatCodeCommand          { get; }
    public IRelayCommand FormatH1Command            { get; }
    public IRelayCommand FormatH2Command            { get; }
    public IRelayCommand FormatH3Command            { get; }
    public IRelayCommand FormatH4Command            { get; }
    public IRelayCommand FormatUnorderedListCommand { get; }
    public IRelayCommand FormatOrderedListCommand   { get; }
    public IRelayCommand FormatTableCommand         { get; }
    public IRelayCommand FormatHRCommand            { get; }
    public IRelayCommand FormatLinkCommand          { get; }
    public IRelayCommand FormatImageCommand         { get; }

    [RelayCommand]
    private void OpenPalette() => CommandPalette.Open();

    // ─── File state (exposed for status bar / title) ─────────────────────────

    public FileService FileService => ActiveTab.FileService;

    public bool HasUnsavedTabs => Tabs.Any(t => t.IsDirty);

    public IReadOnlyList<TabViewModel> GetDirtyTabs() =>
        Tabs.Where(t => t.IsDirty).ToList();

    // ─── Tab-aware file open (BL-34) ─────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="path"/>. If an existing tab already has this file
    /// open (case-insensitive, full-path match), that tab is activated instead
    /// of a new tab being created. If no match exists, the file is loaded into
    /// the current tab when it is empty/untitled, otherwise a new tab is spawned.
    /// Call this from any multi-tab entry point (file association / pipe, CLI
    /// args, drag-and-drop). Ctrl+O's replace-in-current-tab semantics are
    /// unchanged — that path still goes through <c>FileService.OpenFile</c> directly.
    /// </summary>
    public async Task OpenFileInTab(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        string normalized;
        try { normalized = Path.GetFullPath(path); }
        catch { normalized = path; }

        var existing = Tabs.FirstOrDefault(t =>
        {
            var tp = t.FileService.CurrentFilePath;
            if (tp is null) return false;
            string tpNormalized;
            try { tpNormalized = Path.GetFullPath(tp); }
            catch { tpNormalized = tp; }
            return string.Equals(tpNormalized, normalized, StringComparison.OrdinalIgnoreCase);
        });

        if (existing is not null)
        {
            // File already open — just switch to that tab. No new tab,
            // no reload. If the tab is dirty the user's edits are preserved.
            SwitchTabCommand.Execute(existing);
            return;
        }

        // Not already open. Load into current tab if it is empty, else new tab.
        var active = ActiveTab;
        bool activeIsEmpty = active.FileService.CurrentFilePath is null && !active.IsDirty;
        if (activeIsEmpty)
        {
            await active.FileService.OpenFile(path);
        }
        else
        {
            NewTabCommand.Execute(null);
            await ActiveTab.FileService.OpenFile(path);
        }
    }

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MainWindowViewModel(
        ThemeService themeService,
        SettingsService settingsService,
        CommandRegistry commandRegistry,
        TopologyViewModel topologyVm,
        OutlineViewModel outlineVm,
        SnippetStudioViewModel snippetStudioVm,
        AiAssistViewModel aiAssistVm,
        CommandPaletteViewModel commandPalette,
        SnapshotService snapshotService,
        TimelineViewModel timelineVm,
        ExportPanelViewModel exportPanel,
        FileBrowserViewModel fileBrowserVm,
        SnippetService snippetService)
    {
        _themeService    = themeService;
        _settingsService = settingsService;
        _commandRegistry = commandRegistry;
        _topologyVm      = topologyVm;
        _outlineVm       = outlineVm;
        _snippetStudioVm = snippetStudioVm;
        _aiAssistVm      = aiAssistVm;
        _snapshotService = snapshotService;
        _fileBrowserVm   = fileBrowserVm;
        _snippetService  = snippetService;
        CommandPalette   = commandPalette;
        Timeline         = timelineVm;
        ExportPanel      = exportPanel;

        // Wrap the initial tab's per-tab services (pre-constructed and registered
        // as instances in App.axaml.cs so dependent singletons share them).
        var initialTab = new TabViewModel(
            (EditorViewModel)App.Services.GetService(typeof(EditorViewModel))!,
            (PreviewViewModel)App.Services.GetService(typeof(PreviewViewModel))!,
            (FileService)App.Services.GetService(typeof(FileService))!,
            (MarkdownParsingService)App.Services.GetService(typeof(MarkdownParsingService))!,
            (SourceMappingService)App.Services.GetService(typeof(SourceMappingService))!);
        Tabs.Add(initialTab);
        ActiveTab = initialTab;
        PrintCommand     = new RelayCommand(() => _printAction?.Invoke());
        DetachPreviewCommand = new RelayCommand(() => _detachPreviewAction?.Invoke());
        ToggleFocusModeCommand = new RelayCommand(() =>
        {
            IsFocusModeActive = !IsFocusModeActive;
            OnPropertyChanged(nameof(IsFocusModeActive));
            _focusModeAction?.Invoke(IsFocusModeActive);
            var s = _settingsService.Load();
            _settingsService.Save(s with { FocusMode = IsFocusModeActive });
        });
        ToggleFormattingToolbarCommand = new RelayCommand(() =>
        {
            ShowFormattingToolbar = !ShowFormattingToolbar;
            OnPropertyChanged(nameof(ShowFormattingToolbar));
            var s = _settingsService.Load();
            _settingsService.Save(s with { ShowFormattingToolbar = ShowFormattingToolbar });
        });

        // Formatting toolbar commands — delegate to CommandRegistry
        FormatBoldCommand          = new RelayCommand(() => { _commandRegistry.Execute("editor.bold"); _focusEditorAction?.Invoke(); });
        FormatItalicCommand        = new RelayCommand(() => { _commandRegistry.Execute("editor.italic"); _focusEditorAction?.Invoke(); });
        FormatStrikethroughCommand = new RelayCommand(() => { _commandRegistry.Execute("editor.strikethrough"); _focusEditorAction?.Invoke(); });
        FormatCodeCommand          = new RelayCommand(() => { _commandRegistry.Execute("editor.inlineCode"); _focusEditorAction?.Invoke(); });
        FormatH1Command            = new RelayCommand(() => { _commandRegistry.Execute("editor.h1"); _focusEditorAction?.Invoke(); });
        FormatH2Command            = new RelayCommand(() => { _commandRegistry.Execute("editor.h2"); _focusEditorAction?.Invoke(); });
        FormatH3Command            = new RelayCommand(() => { _commandRegistry.Execute("editor.h3"); _focusEditorAction?.Invoke(); });
        FormatH4Command            = new RelayCommand(() => { _commandRegistry.Execute("editor.h4"); _focusEditorAction?.Invoke(); });
        FormatUnorderedListCommand = new RelayCommand(() => { _commandRegistry.Execute("editor.unorderedList"); _focusEditorAction?.Invoke(); });
        FormatOrderedListCommand   = new RelayCommand(() => { _commandRegistry.Execute("editor.orderedList"); _focusEditorAction?.Invoke(); });
        FormatTableCommand         = new RelayCommand(() => { _commandRegistry.Execute("editor.table"); _focusEditorAction?.Invoke(); });
        FormatHRCommand            = new RelayCommand(() => { _commandRegistry.Execute("editor.hr"); _focusEditorAction?.Invoke(); });
        FormatLinkCommand          = new RelayCommand(() => { _commandRegistry.Execute("editor.link"); _focusEditorAction?.Invoke(); });
        FormatImageCommand         = new RelayCommand(() => { _commandRegistry.Execute("editor.image"); _focusEditorAction?.Invoke(); });

        // Auto-switch to Edit mode when export panel opens, restore on close
        ExportPanel.Opened += (_, _) =>
        {
            _viewModeBeforeExport = CurrentViewMode;
            CurrentViewMode = ViewMode.Edit;
        };
        ExportPanel.Closed += (_, _) =>
        {
            if (_viewModeBeforeExport.HasValue)
            {
                CurrentViewMode = _viewModeBeforeExport.Value;
                _viewModeBeforeExport = null;
            }
        };

        // Set default panel occupants
        LeftPanelSlot.SwapOccupant(topologyVm);
        RightPanelSlot.SwapOccupant(outlineVm);

        // Track right panel slot changes for computed properties
        RightPanelSlot.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(PanelSlotViewModel.ActiveOccupant))
            {
                OnPropertyChanged(nameof(IsOutlineSlotActive));
                OnPropertyChanged(nameof(IsSnippetsSlotActive));
                OnPropertyChanged(nameof(IsAiAssistSlotActive));
                OnPropertyChanged(nameof(RightPanelSlotName));
            }
        };

        // Load persisted settings — use backing fields to skip partial-method side effects
        // during construction; all dependencies are already set above.
        var settings = _settingsService.Load();
#pragma warning disable MVVMTK0034
        _splitRatio = Math.Clamp(settings.SplitRatio, 0.1, 0.9);
        _currentViewMode = settings.ViewMode switch
        {
            "Edit"    => ViewMode.Edit,
            "Preview" => ViewMode.Preview,
            _         => ViewMode.Split
        };
        _editorFontFamily        = settings.EditorFontFamily;
        _editorFontSize          = settings.EditorFontSize;
        _autoSaveIntervalSeconds = settings.AutoSaveIntervalSeconds;
        _snippetLibraryPath      = settings.SnippetLibraryPath;
        _anthropicApiKey         = settings.AnthropicApiKey;
        _rightPanelOpenWidth     = settings.RightPanelOpenWidth > 0
            ? settings.RightPanelOpenWidth
            : 200.0;

        // Restore custom theme colors
        var savedColors = settings.CustomThemeColors;
        if (savedColors.Count > 0)
            foreach (var kv in savedColors)
                _customColors[kv.Key] = kv.Value;

        // Apply custom colors to Avalonia chrome on startup
        if (_themeService.CurrentTheme == GhsTheme.Custom && _customColors.Count > 0)
        {
            Dispatcher.UIThread.Post(() =>
                _themeService.ApplyCustomColorsToChrome(_customColors),
                DispatcherPriority.Loaded);
        }

        // Restore left panel open/closed state and active icon
        if (!settings.LeftPanelOpen)
        {
            _isLeftPanelOpen = false;
            _leftPanelWidth  = 0.0;
        }
        if (!string.IsNullOrEmpty(settings.ActiveIcon))
            _activeIcon = settings.ActiveIcon;
#pragma warning restore MVVMTK0034
        IsFocusModeActive        = settings.FocusMode;
        ShowFormattingToolbar    = settings.ShowFormattingToolbar;
#pragma warning disable MVVMTK0034
        _wordWrap             = settings.WordWrap;
        _showLineNumbers      = settings.ShowLineNumbers;
        _highlightCurrentLine = settings.HighlightCurrentLine;
#pragma warning restore MVVMTK0034

        // Swap left panel occupant to match restored icon
        if (_isLeftPanelOpen)
        {
            switch (_activeIcon)
            {
                case "Topology": LeftPanelSlot.SwapOccupant(topologyVm); break;
                case "Snippets": LeftPanelSlot.SwapOccupant(_snippetStudioVm); break;
                case "Search":   LeftPanelSlot.SwapOccupant(_placeholderVm); break;
                case "Files":    LeftPanelSlot.SwapOccupant(_fileBrowserVm); break;
            }
        }

        CurrentThemeName = themeService.CurrentThemeName;
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeCustom));
        OnPropertyChanged(nameof(IsThemeAuto));
        themeService.ThemeChanged += (_, _) =>
        {
            CurrentThemeName = themeService.CurrentThemeName;
            OnPropertyChanged(nameof(IsThemeDark));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeCustom));
            OnPropertyChanged(nameof(IsThemeAuto));
        };

        // Wire per-tab FileService subscriptions (named methods for unsub/resub)
        ActiveTab.FileService.PropertyChanged += OnActiveTabFilePathChanged;
        ActiveTab.FileService.PropertyChanged += OnActiveTabFilePathChangedForTimeline;
        ActiveTab.FileService.FileSaved       += OnFileSaved;
        ActiveTab.FileService.FileSaved       += OnActiveTabFileSavedForSnapshot;
        ActiveTab.FileService.FileSaved       += OnActiveTabFileSavedDeleteDraft;

        // Auto-save snapshot every 2 minutes (reads ActiveTab at fire time — correct for any tab)
        _snapshotTimer = new System.Threading.Timer(async _ =>
        {
            var path = ActiveTab.FileService.CurrentFilePath;
            var content = await Dispatcher.UIThread.InvokeAsync(() => ActiveTab.EditorViewModel.DocumentText);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(content))
                await _snapshotService.SaveSnapshot(path, content);
        }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));

        // Draft auto-save timer (reads ActiveTab at fire time — correct for any tab)
        var draftInterval = TimeSpan.FromSeconds(settings.AutoSaveIntervalSeconds);
        _draftTimer = new System.Threading.Timer(async _ =>
        {
            var path = ActiveTab.FileService.CurrentFilePath;
            var content = await Dispatcher.UIThread.InvokeAsync(() => ActiveTab.EditorViewModel.DocumentText);
            //if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(content))
            //    ActiveTab.FileService.WriteDraft(path, content);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(content)
                && ActiveTab.FileService.HasUnsavedChanges)
                ActiveTab.FileService.WriteDraft(path, content);
        }, null, draftInterval, draftInterval);

        RegisterCommands();
    }

    private void RegisterCommands()
    {
        var r = _commandRegistry;

        // Actions
        r.Register(new CommandDescriptor("file.new",    "New File",    "Actions", () => NewFileCommand.Execute(null), "Ctrl+N"));
        r.Register(new CommandDescriptor("file.open",   "Open File",   "Actions", () => OpenFileCommand.Execute(null), "Ctrl+O"));
        r.Register(new CommandDescriptor("file.save",   "Save",        "Actions", () => SaveFileCommand.Execute(null), "Ctrl+S"));
        r.Register(new CommandDescriptor("file.saveAs", "Save As",     "Actions", () => SaveFileAsCommand.Execute(null), "Ctrl+Shift+S"));

        // Navigation
        r.Register(new CommandDescriptor("view.editOnly",    "View: Edit Only",    "Navigation", () => CurrentViewMode = ViewMode.Edit, "Ctrl+Shift+E"));
        r.Register(new CommandDescriptor("view.split",       "View: Split",        "Navigation", () => CurrentViewMode = ViewMode.Split, "Ctrl+Shift+T"));
        r.Register(new CommandDescriptor("view.previewOnly", "View: Preview Only", "Navigation", () => CurrentViewMode = ViewMode.Preview, "Ctrl+Shift+W"));
        r.Register(new CommandDescriptor("panel.left.toggle",    "Toggle Left Panel",       "Navigation", () => ActivateIcon(_activeIcon)));
        r.Register(new CommandDescriptor("panel.right.toggle",   "Toggle Right Panel",      "Navigation", () => ToggleRightPanel()));
        r.Register(new CommandDescriptor("panel.right.outline",  "Right Panel: Outline",    "Navigation", () => { if (!IsRightPanelOpen) ToggleRightPanel(); SwitchToOutline(); }));
        r.Register(new CommandDescriptor("panel.right.snippets", "Right Panel: Snippets",   "Navigation", () => { if (!IsRightPanelOpen) ToggleRightPanel(); SwitchToSnippets(); }));
        r.Register(new CommandDescriptor("panel.right.aiAssist", "Right Panel: AI Assist",  "Navigation", () => { if (!IsRightPanelOpen) ToggleRightPanel(); SwitchToAiAssist(); }));

        // Snippets
        r.Register(new CommandDescriptor("snippets.open", "Open Snippet Studio", "Navigation", () => ActivateIcon("Snippets")));

        // Export
        r.Register(new CommandDescriptor("export.open",       "Export\u2026",                   "Actions", () => ExportPanel.Open()));
        r.Register(new CommandDescriptor("export.pdf",        "Export as PDF",                 "Actions", () => ExportPanel.OpenWithFormat(Models.ExportFormat.PdfStyled)));
        r.Register(new CommandDescriptor("export.docx",       "Export as Word Document",       "Actions", () => ExportPanel.OpenWithFormat(Models.ExportFormat.Docx)));
        r.Register(new CommandDescriptor("export.htmlStyled", "Export as HTML (Styled)",       "Actions", () => ExportPanel.OpenWithFormat(Models.ExportFormat.HtmlStyled)));
        r.Register(new CommandDescriptor("export.htmlClean",  "Export as HTML (Clean)",        "Actions", () => ExportPanel.OpenWithFormat(Models.ExportFormat.HtmlClean)));
        r.Register(new CommandDescriptor("export.plainText",  "Export as Plain Text",          "Actions", () => ExportPanel.OpenWithFormat(Models.ExportFormat.PlainText)));

        // Print
        r.Register(new CommandDescriptor("file.print", "Print\u2026", "Actions", () => PrintCommand.Execute(null), "Ctrl+Shift+P"));

        // Focus Mode
        r.Register(new CommandDescriptor("view.focusMode", "Toggle Focus Mode", "Navigation", () => ToggleFocusModeCommand.Execute(null), "Ctrl+Shift+F"));

        // Formatting Toolbar
        r.Register(new CommandDescriptor("view.formattingToolbar", "Toggle Formatting Toolbar", "Navigation", () => ToggleFormattingToolbarCommand.Execute(null), "Ctrl+Shift+B"));

        // Detach Preview
        r.Register(new CommandDescriptor("view.detachPreview", "Detach Preview Window", "Navigation", () => DetachPreviewCommand.Execute(null), "Ctrl+D"));

        // Settings
        r.Register(new CommandDescriptor("settings.theme.dark",   "Theme: GHS Dark",   "Settings", () => SetTheme("Dark")));
        r.Register(new CommandDescriptor("settings.theme.light",  "Theme: GHS Light",  "Settings", () => SetTheme("Light")));
        r.Register(new CommandDescriptor("settings.theme.custom", "Theme: GHS Custom", "Settings", () => SetTheme("Custom")));
    }

    private void UpdateWindowTitle()
    {
        var path = ActiveTab.FileService.CurrentFilePath;
        var name = path is null ? "Untitled" : Path.GetFileName(path);
        WindowTitle = $"GHS Markdown Editor — {name}";
    }

    // ─── Save state (green dots) ──────────────────────────────────────────────

    private CancellationTokenSource? _savedStateCts;

    private void OnFileSaved(object? sender, EventArgs e)
    {
        _savedStateCts?.Cancel();
        _savedStateCts = new CancellationTokenSource();
        var token = _savedStateCts.Token;

        GutterSyncState = GutterSyncState.Saved;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000, token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (GutterSyncState == GutterSyncState.Saved)
                        GutterSyncState = GutterSyncState.Synced;
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    // ─── Per-tab FileService handlers (named for unsub/resub on tab switch) ────

    private void OnActiveTabFilePathChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileService.CurrentFilePath))
        {
            UpdateWindowTitle();
            SaveSettings();
        }
    }

    private async void OnActiveTabFilePathChangedForTimeline(
        object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileService.CurrentFilePath))
            await Timeline.ReloadSnapshots(ActiveTab.FileService.CurrentFilePath);
    }

    private async void OnActiveTabFileSavedForSnapshot(object? sender, EventArgs e)
    {
        var path    = ActiveTab.FileService.CurrentFilePath;
        var content = ActiveTab.EditorViewModel.DocumentText;
        if (!string.IsNullOrEmpty(path))
        {
            await _snapshotService.SaveSnapshot(path, content);
            await Timeline.ReloadSnapshots(path);
        }
    }

    private void OnActiveTabFileSavedDeleteDraft(object? sender, EventArgs e)
    {
        var p = ActiveTab.FileService.CurrentFilePath;
        if (!string.IsNullOrEmpty(p))
            ActiveTab.FileService.DeleteDraft(p);
    }

    /// <summary>
    /// Called by code-behind after a tab switch completes. Re-wires all
    /// ViewModel-level per-tab subscriptions to the newly active tab.
    /// </summary>
    public void NotifyTabActivated(TabViewModel outgoing, TabViewModel incoming)
    {
        // Unsubscribe from outgoing tab's FileService
        outgoing.FileService.PropertyChanged -= OnActiveTabFilePathChanged;
        outgoing.FileService.PropertyChanged -= OnActiveTabFilePathChangedForTimeline;
        outgoing.FileService.FileSaved       -= OnFileSaved;
        outgoing.FileService.FileSaved       -= OnActiveTabFileSavedForSnapshot;
        outgoing.FileService.FileSaved       -= OnActiveTabFileSavedDeleteDraft;

        // Subscribe to incoming tab's FileService
        incoming.FileService.PropertyChanged += OnActiveTabFilePathChanged;
        incoming.FileService.PropertyChanged += OnActiveTabFilePathChangedForTimeline;
        incoming.FileService.FileSaved       += OnFileSaved;
        incoming.FileService.FileSaved       += OnActiveTabFileSavedForSnapshot;
        incoming.FileService.FileSaved       += OnActiveTabFileSavedDeleteDraft;

        // Rewire Topology, Outline, and Export to incoming tab's services
        _topologyVm.RewireParsingService(incoming.MarkdownParsingService, incoming.EditorViewModel);
        _outlineVm.RewireParsingService(incoming.MarkdownParsingService, incoming.EditorViewModel);
        ExportPanel.RewireDocumentSource(incoming.EditorViewModel, incoming.MarkdownParsingService);

        // Reload timeline for the new tab's file
        _ = Timeline.ReloadSnapshots(incoming.FileService.CurrentFilePath);

        // Update window title
        UpdateWindowTitleFromTab(incoming);
    }

    // ─── Settings persistence ─────────────────────────────────────────────────

    private void SaveSettings()
    {
        _settingsService.Save(new AppSettings
        {
            SplitRatio              = SplitRatio,
            ViewMode                = CurrentViewMode.ToString(),
            Theme                   = _themeService.CurrentTheme.ToString(),
            EditorFontFamily        = EditorFontFamily,
            EditorFontSize          = EditorFontSize,
            AutoSaveIntervalSeconds = AutoSaveIntervalSeconds,
            SnippetLibraryPath      = SnippetLibraryPath,
            RecentFiles             = ActiveTab.FileService.GetRecentFiles().ToList(),
            CustomThemeColors       = new Dictionary<string, string>(CustomColors),
            RightPanelOpenWidth     = _rightPanelOpenWidth,
            LeftPanelOpen           = IsLeftPanelOpen,
            ActiveIcon              = _activeIcon,
            OpenTabPaths            = Tabs.Select(t => t.FileService.CurrentFilePath).ToList(),
            ActiveTabIndex          = Tabs.IndexOf(ActiveTab),
        });
    }

    /// <summary>Called by code-behind after gutter drag ends.</summary>
    public void PersistSplitRatio() => SaveSettings();

    /// <summary>Called on window close to flush current state to disk.</summary>
    public void ForceSaveSettings() => SaveSettings();

    // ─── Icon rail commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateIcon(string iconName)
    {
        if (iconName == "Settings")
        {
            if (IsSettingsOpen && IsLeftPanelOpen)
            {
                // Toggle close
                IsSettingsOpen  = false;
                IsLeftPanelOpen = false;
                LeftPanelWidth  = 0.0;
            }
            else
            {
                IsSettingsOpen = true;
                if (!IsLeftPanelOpen)
                {
                    IsLeftPanelOpen = true;
                    LeftPanelWidth  = 220.0;
                }
            }
            OnPropertyChanged(nameof(IsTopologyActive));
            OnPropertyChanged(nameof(IsFilesActive));
            OnPropertyChanged(nameof(IsSnippetsActive));
            OnPropertyChanged(nameof(IsSearchActive));
            return;
        }

        IsSettingsOpen = false;

        if (_activeIcon == iconName && IsLeftPanelOpen)
        {
            IsLeftPanelOpen = false;
            LeftPanelWidth  = 0.0;
        }
        else
        {
            IsLeftPanelOpen = true;
            LeftPanelWidth  = 220.0;
            ActiveIcon      = iconName;
        }

        // Swap left panel occupant based on icon
        if (IsLeftPanelOpen)
        {
            switch (iconName)
            {
                case "Topology": LeftPanelSlot.SwapOccupant(_topologyVm); break;
                case "Snippets": LeftPanelSlot.SwapOccupant(_snippetStudioVm); break;
                case "Search":   LeftPanelSlot.SwapOccupant(_placeholderVm); break;
                case "Files":    LeftPanelSlot.SwapOccupant(_fileBrowserVm); break;
            }
        }

        OnPropertyChanged(nameof(IsTopologyActive));
        OnPropertyChanged(nameof(IsFilesActive));
        OnPropertyChanged(nameof(IsSnippetsActive));
        OnPropertyChanged(nameof(IsSearchActive));

        // Persist panel open/closed state and active icon
        var s = _settingsService.Load();
        _settingsService.Save(s with { LeftPanelOpen = IsLeftPanelOpen, ActiveIcon = _activeIcon });
    }

    [RelayCommand]
    private void ToggleRightPanel()
    {
        IsRightPanelOpen = !IsRightPanelOpen;
        RightPanelWidth  = IsRightPanelOpen ? _rightPanelOpenWidth : 0.0;
    }

    /// <summary>Called by code-behind after right panel drag ends.</summary>
    public void PersistRightPanelWidth()
    {
        _rightPanelOpenWidth = RightPanelWidth;
        var s = _settingsService.Load();
        _settingsService.Save(s with { RightPanelOpenWidth = RightPanelWidth });
    }

    // ─── Theme command ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTheme(string themeName)
    {
        var theme = themeName switch
        {
            "Light"  => GhsTheme.Light,
            "Custom" => GhsTheme.Custom,
            "Auto"   => GhsTheme.Auto,
            _        => GhsTheme.Dark
        };
        _themeService.SetTheme(theme);
        // When switching to Custom, load saved colors and apply everywhere
        if (theme == GhsTheme.Custom)
        {
            var s = _settingsService.Load();
            foreach (var kv in s.CustomThemeColors)
                if (CustomColors.ContainsKey(kv.Key))
                    CustomColors[kv.Key] = kv.Value;
            _themeService.NotifyThemeChanged();
            Dispatcher.UIThread.Post(() =>
                _themeService.ApplyCustomColorsToChrome(CustomColors),
                DispatcherPriority.Loaded);
        }
    }

    // ─── View mode commands ───────────────────────────────────────────────────

    [RelayCommand] private void SetViewModeEdit()    => CurrentViewMode = ViewMode.Edit;
    [RelayCommand] private void SetViewModeSplit()   => CurrentViewMode = ViewMode.Split;
    [RelayCommand] private void SetViewModePreview() => CurrentViewMode = ViewMode.Preview;

    // ─── File commands ────────────────────────────────────────────────────────

    [RelayCommand] private async Task NewFile()    => await ActiveTab.FileService.NewFile();
    [RelayCommand] private async Task OpenFile()   => await ActiveTab.FileService.OpenFile();
    [RelayCommand] private async Task SaveFile()   => await ActiveTab.FileService.SaveFile();
    [RelayCommand] private async Task SaveFileAs() => await ActiveTab.FileService.SaveFileAs();

    // --- Tab commands -----------------------------------------------------------

    [RelayCommand]
    private void NewTab()
    {
        var tab = new TabViewModel(_settingsService, _themeService);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private async Task CloseActiveTab() => await CloseTab(ActiveTab);

    [RelayCommand]
    private async Task CloseTab(TabViewModel tab)
    {
        if (tab.IsDirty)
        {
            var result = await FileService.PromptUnsavedChangesAsync();
            if (result == UnsavedAction.Cancel) return;
            if (result == UnsavedAction.Save)
                await tab.FileService.SaveFile();
        }

        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            NewTab();
            return;
        }

        var newIdx = Math.Max(0, Math.Min(idx, Tabs.Count - 1));
        ActiveTab = Tabs[newIdx];
    }

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count < 2) return;
        var idx = Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(idx + 1) % Tabs.Count];
    }

    [RelayCommand]
    private void SwitchTab(TabViewModel tab)
    {
        if (tab == ActiveTab) return;
        ActiveTab = tab;
    }

    public void UpdateWindowTitleFromTab(TabViewModel tab)
    {
        var path = tab.FileService.CurrentFilePath;
        var name = path is null ? "Untitled" : Path.GetFileName(path);
        WindowTitle = $"GHS Markdown Editor — {name}";
    }

    /// <summary>
    /// Restores tabs from persisted paths. Called by App after DI container is
    /// built and the window is shown. Silently skips missing files.
    /// </summary>
    public async Task RestoreTabsAsync(List<string?> paths, int activeIndex)
    {
        var validPaths = paths
            .Where(p => p is not null && File.Exists(p))
            .Select(p => p!)
            .ToList();

        if (validPaths.Count == 0)
            return;

        // Open the first path in the existing initial (untitled) tab
        await ActiveTab.FileService.OpenFile(validPaths[0]);

        // Open remaining paths each in a new tab
        for (int i = 1; i < validPaths.Count; i++)
        {
            var settings = _settingsService.Load();
            if (Tabs.Count >= settings.MaxRestoredTabs) break;

            NewTabCommand.Execute(null);
            await ActiveTab.FileService.OpenFile(validPaths[i]);
        }

        // Restore active tab index by matching path
        var targetPath = activeIndex >= 0 && activeIndex < paths.Count
            ? paths[activeIndex]
            : null;

        if (targetPath is not null)
        {
            var targetTab = Tabs.FirstOrDefault(
                t => t.FileService.CurrentFilePath == targetPath);
            if (targetTab is not null)
                SwitchTabCommand.Execute(targetTab);
        }
        else if (Tabs.Count > 0)
        {
            SwitchTabCommand.Execute(Tabs[0]);
        }
    }
}
