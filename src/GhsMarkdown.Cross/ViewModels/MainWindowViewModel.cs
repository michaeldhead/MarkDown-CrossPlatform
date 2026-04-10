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
    private readonly FileService     _fileService;
    private readonly SettingsService _settingsService;
    private readonly CommandRegistry _commandRegistry;
    private readonly TopologyViewModel _topologyVm;
    private readonly OutlineViewModel _outlineVm;
    private readonly SnippetStudioViewModel _snippetStudioVm;
    private readonly AiAssistPlaceholderViewModel _aiAssistVm;
    private readonly SnapshotService _snapshotService;
    private readonly EditorViewModel _editorVm;
    private readonly FileBrowserViewModel _fileBrowserVm;
    private readonly SnippetService _snippetService;
    private System.Threading.Timer? _snapshotTimer;
    private System.Threading.Timer? _draftTimer;
    private ViewMode? _viewModeBeforeExport;

    // ─── Icon rail ───────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isLeftPanelOpen = true;
    [ObservableProperty] private bool   _isRightPanelOpen = false;
    [ObservableProperty] private bool   _isSettingsOpen = false;
    [ObservableProperty] private double _leftPanelWidth = 220.0;
    [ObservableProperty] private double _rightPanelWidth = 0.0;

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

    public bool IsThemeDark  => _themeService.CurrentTheme == GhsTheme.Dark;
    public bool IsThemeLight => _themeService.CurrentTheme == GhsTheme.Light;
    public bool IsThemeAuto  => _themeService.CurrentTheme == GhsTheme.Auto;

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

    public IEnumerable<CommandDescriptor> ShortcutCommands =>
        _commandRegistry.GetAll()
            .Where(c => c.KeyboardShortcut is not null)
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Title);

    // ─── Right panel slot switcher ───────────────────────────────────────────

    public bool IsOutlineSlotActive  => RightPanelSlot.ActiveOccupant is OutlineViewModel;
    public bool IsSnippetsSlotActive => RightPanelSlot.ActiveOccupant is SnippetStudioViewModel;
    public bool IsAiAssistSlotActive => RightPanelSlot.ActiveOccupant is AiAssistPlaceholderViewModel;

    public string RightPanelSlotName => RightPanelSlot.ActiveOccupant switch
    {
        OutlineViewModel              => "Outline",
        SnippetStudioViewModel        => "Snippets",
        AiAssistPlaceholderViewModel  => "AI Assist",
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

    [RelayCommand]
    private void OpenPalette() => CommandPalette.Open();

    // ─── File state (exposed for status bar / title) ─────────────────────────

    public FileService FileService => _fileService;

    // ─── Constructor ─────────────────────────────────────────────────────────

    public MainWindowViewModel(
        ThemeService themeService,
        FileService fileService,
        SettingsService settingsService,
        CommandRegistry commandRegistry,
        TopologyViewModel topologyVm,
        OutlineViewModel outlineVm,
        SnippetStudioViewModel snippetStudioVm,
        AiAssistPlaceholderViewModel aiAssistVm,
        CommandPaletteViewModel commandPalette,
        SnapshotService snapshotService,
        TimelineViewModel timelineVm,
        EditorViewModel editorVm,
        ExportPanelViewModel exportPanel,
        FileBrowserViewModel fileBrowserVm,
        SnippetService snippetService)
    {
        _themeService    = themeService;
        _fileService     = fileService;
        _settingsService = settingsService;
        _commandRegistry = commandRegistry;
        _topologyVm      = topologyVm;
        _outlineVm       = outlineVm;
        _snippetStudioVm = snippetStudioVm;
        _aiAssistVm      = aiAssistVm;
        _snapshotService = snapshotService;
        _editorVm        = editorVm;
        _fileBrowserVm   = fileBrowserVm;
        _snippetService  = snippetService;
        CommandPalette   = commandPalette;
        Timeline         = timelineVm;
        ExportPanel      = exportPanel;
        PrintCommand     = new RelayCommand(() => _printAction?.Invoke());

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
#pragma warning restore MVVMTK0034

        CurrentThemeName = themeService.CurrentThemeName;
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeAuto));
        themeService.ThemeChanged += (_, _) =>
        {
            CurrentThemeName = themeService.CurrentThemeName;
            OnPropertyChanged(nameof(IsThemeDark));
            OnPropertyChanged(nameof(IsThemeLight));
            OnPropertyChanged(nameof(IsThemeAuto));
        };

        _fileService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FileService.CurrentFilePath))
                UpdateWindowTitle();
        };

        _fileService.FileSaved += OnFileSaved;

        // Snapshot on manual save + reload timeline
        _fileService.FileSaved += async (_, _) =>
        {
            var path = _fileService.CurrentFilePath;
            var content = _editorVm.DocumentText;
            if (!string.IsNullOrEmpty(path))
            {
                await _snapshotService.SaveSnapshot(path, content);
                await Timeline.ReloadSnapshots(path);
            }
        };

        // Reload timeline on file open/new
        _fileService.PropertyChanged += async (_, pe) =>
        {
            if (pe.PropertyName == nameof(FileService.CurrentFilePath))
                await Timeline.ReloadSnapshots(_fileService.CurrentFilePath);
        };

        // Auto-save snapshot every 2 minutes
        _snapshotTimer = new System.Threading.Timer(async _ =>
        {
            var path = _fileService.CurrentFilePath;
            var content = await Dispatcher.UIThread.InvokeAsync(() => _editorVm.DocumentText);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(content))
                await _snapshotService.SaveSnapshot(path, content);
        }, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));

        // Draft auto-save timer
        var draftInterval = TimeSpan.FromSeconds(settings.AutoSaveIntervalSeconds);
        _draftTimer = new System.Threading.Timer(async _ =>
        {
            var path = _fileService.CurrentFilePath;
            var content = await Dispatcher.UIThread.InvokeAsync(() => _editorVm.DocumentText);
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(content))
                _fileService.WriteDraft(path, content);
        }, null, draftInterval, draftInterval);

        // Delete draft on explicit save
        _fileService.FileSaved += (_, _) =>
        {
            var p = _fileService.CurrentFilePath;
            if (!string.IsNullOrEmpty(p))
                _fileService.DeleteDraft(p);
        };

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

        // Settings
        r.Register(new CommandDescriptor("settings.theme.dark",  "Theme: GHS Dark",  "Settings", () => SetTheme("Dark")));
        r.Register(new CommandDescriptor("settings.theme.light", "Theme: GHS Light", "Settings", () => SetTheme("Light")));
    }

    private void UpdateWindowTitle()
    {
        var path = _fileService.CurrentFilePath;
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
            RecentFiles             = _fileService.GetRecentFiles().ToList()
        });
    }

    /// <summary>Called by code-behind after gutter drag ends.</summary>
    public void PersistSplitRatio() => SaveSettings();

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
    }

    [RelayCommand]
    private void ToggleRightPanel()
    {
        IsRightPanelOpen = !IsRightPanelOpen;
        RightPanelWidth  = IsRightPanelOpen ? 200.0 : 0.0;
    }

    // ─── Theme command ────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetTheme(string themeName)
    {
        var theme = themeName switch
        {
            "Light" => GhsTheme.Light,
            "Auto"  => GhsTheme.Auto,
            _       => GhsTheme.Dark
        };
        _themeService.SetTheme(theme);
    }

    // ─── View mode commands ───────────────────────────────────────────────────

    [RelayCommand] private void SetViewModeEdit()    => CurrentViewMode = ViewMode.Edit;
    [RelayCommand] private void SetViewModeSplit()   => CurrentViewMode = ViewMode.Split;
    [RelayCommand] private void SetViewModePreview() => CurrentViewMode = ViewMode.Preview;

    // ─── File commands ────────────────────────────────────────────────────────

    [RelayCommand] private async Task NewFile()    => await _fileService.NewFile();
    [RelayCommand] private async Task OpenFile()   => await _fileService.OpenFile();
    [RelayCommand] private async Task SaveFile()   => await _fileService.SaveFile();
    [RelayCommand] private async Task SaveFileAs() => await _fileService.SaveFileAs();
}
