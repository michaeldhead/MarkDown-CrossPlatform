using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.Services;
using GhsMarkdown.Cross.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace GhsMarkdown.Cross.Views;

public partial class MainWindow : Window
{
    // ─── Controls ─────────────────────────────────────────────────────────────
    private CenteringTextEditor?                  _editor;
    private Avalonia.Controls.NativeWebView?     _webView;
    private Grid?                                _centerGrid;
    private Border?                              _gutterBorder;

    // ─── ViewModels / Services ────────────────────────────────────────────────
    private PreviewViewModel?           _previewVm;
    private EditorViewModel?            _editorVm;
    private MainWindowViewModel?        _mainVm;
    private SourceMappingService?       _sourceMappingService;
    private FileService?                _fileService;
    private TopologyViewModel?          _topologyVm;
    private OutlineViewModel?           _outlineVm;
    private SnippetModeController?      _snippetModeController;
    private SnippetInsertionService?    _snippetInsertionService;
    private CommandRegistry?            _commandRegistry;

    // ─── WebView state ────────────────────────────────────────────────────────
    private bool _webViewReady;
    private double _savedScrollY;

    // ─── Active block highlight ───────────────────────────────────────────────
    private string? _lastActiveSelector;
    private CancellationTokenSource? _highlightCts;

    // ─── Gutter drag ──────────────────────────────────────────────────────────
    private bool   _isDraggingGutter;
    private double _dragStartX;
    private double _dragStartRatio;

    // ─── Right panel drag ─────────────────────────────────────────────────────
    private bool   _isDraggingRightPanel;
    private double _rightPanelDragStartX;
    private double _rightPanelDragStartWidth;
    private Transitions? _rightPanelTransitions;

    // ─── Tab switch state ────────────────────────────────────────────────────
    private TabViewModel? _previousActiveTab;
    private bool _tabSwitchInProgress;

    // ─── Scroll sync ──────────────────────────────────────────────────────────
    private bool   _isSyncingScroll;
    private double _lastProgrammaticScrollFraction = 0;
    private DateTime _lastEditorSyncTime = DateTime.MinValue;
    private const int ScrollSyncCooldownMs = 300;
    private int _lastSyncedAnchorLine = -1;

    // ─── Detached preview ────────────────────────────────────────────────────
    private DetachedPreviewWindow? _detachedWindow;

    // ─── Word count ───────────────────────────────────────────────────────────
    private CancellationTokenSource? _wordCountCts;
    private int _lastTotalWordCount;

    // ─── Git diff gutter ──────────────────────────────────────────────────────
    private GitDiffMargin?  _gitDiffMargin;
    private GitDiffService  _gitDiffService = new();
    private CancellationTokenSource? _gitDiffCts;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Drag-and-drop
        AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        });
        AddHandler(DragDrop.DropEvent, OnFileDrop);

        // Close detached preview window when main window closes
        Closing += (_, _) => _detachedWindow?.Close();
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var mdFile = files
            .Select(f => f.Path.LocalPath)
            .FirstOrDefault(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        if (mdFile is not null && _mainVm is not null)
        {
            await _mainVm.ActiveTab.FileService.OpenFile(mdFile);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        _mainVm = vm;
        _previousActiveTab = vm.ActiveTab;

        _editorVm             = vm.ActiveTab.EditorViewModel;
        _previewVm            = vm.ActiveTab.PreviewViewModel;
        _sourceMappingService = vm.ActiveTab.SourceMappingService;
        _fileService          = vm.ActiveTab.FileService;
        _topologyVm           = App.Services.GetService(typeof(TopologyViewModel))            as TopologyViewModel;
        _outlineVm            = App.Services.GetService(typeof(OutlineViewModel))             as OutlineViewModel;
        _snippetModeController   = App.Services.GetService(typeof(SnippetModeController))    as SnippetModeController;
        _snippetInsertionService = App.Services.GetService(typeof(SnippetInsertionService))  as SnippetInsertionService;

        InitializeEditor();
        InitializeWebView();
        InitializeCenterGrid();

        // Wire code block toolbar button
        var codeBlockBtn = this.FindControl<Button>("CodeBlockToolbarBtn");
        if (codeBlockBtn is not null)
            codeBlockBtn.Click += (_, _) =>
                CodeLanguagePickerWindow.Show(this, "Insert Code Block",
                    lang => InsertCodeBlock(lang));

        // Wire detach preview action
        var settingsSvc = App.Services.GetService(typeof(SettingsService)) as SettingsService;
        _mainVm?.SetDetachPreviewAction(() => ToggleDetachedPreview(settingsSvc!));

        // Apply editor theme colors and subscribe to theme changes
        var themeService = App.Services.GetService(typeof(ThemeService)) as ThemeService;
        if (themeService is not null && _editor is not null)
        {
            ApplyEditorTheme(themeService.CurrentThemeName);
            themeService.ThemeChanged += (_, _) =>
            {
                ApplyEditorTheme(themeService.CurrentThemeName);
                ApplyMarkdownHighlighting();
                if (_previewVm is not null && _webViewReady)
                    NavigateWebView(_previewVm.PreviewHtml);
            };
        }

        // Apply editor font from settings and subscribe to changes
        if (_editor is not null && _mainVm is not null)
        {
            ApplyEditorFont(_mainVm.EditorFontFamily, _mainVm.EditorFontSize);
            _mainVm.EditorFontChanged += (_, _) =>
                ApplyEditorFont(_mainVm.EditorFontFamily, _mainVm.EditorFontSize);
        }
        InitializeLeftPanelSlot();
        InitializeRightPanelSlot();

        // Draft restore prompt (snippet-mode exit now handled in OnFileServicePropertyChanged)
        if (_fileService is not null)
            _fileService.DraftFound += OnDraftFound;

        // Wire SnippetStudioViewModel delegates
        var snippetStudioVm = App.Services.GetService(typeof(SnippetStudioViewModel)) as SnippetStudioViewModel;
        if (snippetStudioVm is not null)
        {
            snippetStudioVm.InsertSnippetAction = InsertSnippetIntoEditor;
            snippetStudioVm.ShowEditDialogFunc = existing => SnippetEditDialog.Open(this, existing);
            snippetStudioVm.ShowConfirmFunc = async msg =>
            {
                var dlg = new Window
                {
                    Title = "Confirm",
                    Width = 320, Height = 140,
                    CanResize = false,
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x18, 0x18, 0x18)),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                bool result = false;
                var yesBtn = new Button { Content = "Delete", Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xC7, 0x50, 0x50)), Background = Avalonia.Media.Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Margin = new Avalonia.Thickness(0, 0, 8, 0) };
                var noBtn = new Button { Content = "Cancel", Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88)), Background = Avalonia.Media.Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                yesBtn.Click += (_, _) => { result = true; dlg.Close(); };
                noBtn.Click += (_, _) => dlg.Close();
                dlg.Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = msg, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)), FontSize = 13, Margin = new Avalonia.Thickness(0, 0, 0, 16) },
                        new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { noBtn, yesBtn } }
                    }
                };
                await dlg.ShowDialog<object?>(this);
                return result;
            };
        }

        // Wire TimelineViewModel confirm dialog
        var timelineVm = App.Services.GetService(typeof(TimelineViewModel)) as TimelineViewModel;
        if (timelineVm is not null)
        {
            timelineVm.ShowConfirmFunc = async msg =>
            {
                var dlg = new Window
                {
                    Title = "Restore Version",
                    Width = 360, Height = 160,
                    CanResize = false,
                    WindowDecorations = Avalonia.Controls.WindowDecorations.None,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x18, 0x18, 0x18)),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                bool result = false;
                var restoreBtn = new Button { Content = "Restore", Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x4A, 0x9E, 0xFF)), Background = Avalonia.Media.Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
                var cancelBtn = new Button { Content = "Cancel", Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88)), Background = Avalonia.Media.Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Margin = new Avalonia.Thickness(0, 0, 8, 0) };
                restoreBtn.Click += (_, _) => { result = true; dlg.Close(); };
                cancelBtn.Click += (_, _) => dlg.Close();
                dlg.Content = new StackPanel
                {
                    Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = msg, Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)), FontSize = 13, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 0, 16) },
                        new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { cancelBtn, restoreBtn } }
                    }
                };
                await dlg.ShowDialog<object?>(this);
                return result;
            };
        }

        // Wire ExportPanelViewModel save dialog delegate
        var exportPanelVm = App.Services.GetService(typeof(ExportPanelViewModel)) as ExportPanelViewModel;
        if (exportPanelVm is not null)
        {
            exportPanelVm.ShowSaveDialogFunc = async format =>
            {
                var dlg = new Avalonia.Platform.Storage.FilePickerSaveOptions
                {
                    Title = "Export",
                    SuggestedFileName = GetSuggestedExportName(_fileService, _editorVm),
                    DefaultExtension = format switch
                    {
                        ExportFormat.PdfStyled => "pdf",
                        ExportFormat.Docx => "docx",
                        ExportFormat.PlainText => "txt",
                        _ => "html"
                    },
                    FileTypeChoices = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType(format switch
                        {
                            ExportFormat.PdfStyled => "PDF Files",
                            ExportFormat.Docx => "Word Documents",
                            ExportFormat.PlainText => "Text Files",
                            _ => "HTML Files"
                        })
                        {
                            Patterns = new[] { format switch
                            {
                                ExportFormat.PdfStyled => "*.pdf",
                                ExportFormat.Docx => "*.docx",
                                ExportFormat.PlainText => "*.txt",
                                _ => "*.html"
                            }}
                        }
                    }
                };
                var result = await StorageProvider.SaveFilePickerAsync(dlg);
                return result?.Path.LocalPath;
            };
        }

        // Wire topology node click → editor/preview navigation
        if (_topologyVm is not null)
            _topologyVm.NodeClicked += OnNodeClicked;

        // Wire outline node click → editor/preview navigation
        if (_outlineVm is not null)
            _outlineVm.NodeClicked += OnNodeClicked;

        // Observe PreviewHtml changes to push to WebView
        if (_previewVm is not null)
            _previewVm.PropertyChanged += OnPreviewVmPropertyChanged;

        // Observe view mode to update center layout
        _mainVm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(MainWindowViewModel.CurrentViewMode))
                UpdateCenterLayout();
            if (pe.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen)
                && _mainVm.IsSettingsOpen)
            {
                var sv = this.FindControl<SettingsView>("SettingsViewControl");
                sv?.RefreshColorPickers();
            }
        };

        // BL-32: hide the NativeWebView while the Command Palette is open.
        // The WebView is an OS-level HWND that renders over all managed
        // Avalonia UI regardless of ZIndex, so the palette card gets clipped
        // on the right side in Split mode. Collapsing the preview column to
        // 0px (the same trick Edit mode uses) removes the HWND from screen
        // space. On close, UpdateCenterLayout() restores based on the current
        // view mode.
        _mainVm.CommandPalette.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName != nameof(CommandPaletteViewModel.IsOpen)) return;
            if (_mainVm.CommandPalette.IsOpen)
                CollapsePreviewForPalette();
            else
                UpdateCenterLayout();
        };

        // Subscribe to tab switches
        vm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(MainWindowViewModel.ActiveTab)
                && _mainVm?.ActiveTab is { } newTab)
            {
                SwitchActiveTab(newTab);
            }
        };

        // Wire tab strip buttons
        var newTabBtn = this.FindControl<Button>("NewTabButton");
        if (newTabBtn is not null)
            newTabBtn.Click += (_, _) => _mainVm?.NewTabCommand.Execute(null);

        Dispatcher.UIThread.Post(() => WireTabStripInteraction(), DispatcherPriority.Loaded);
    }

    // ─── Per-tab named event handlers (extractable for tab switch) ─────────────

    private void OnEditorVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.DocumentText)
            && _editor is not null
            && _editor.Document.Text != _editorVm?.DocumentText)
        {
            _editor.Document.Text = _editorVm?.DocumentText ?? string.Empty;
        }
    }

    private void OnEditorDocumentTextChanged(object? sender, EventArgs e)
    {
        if (_editor is not null && _editorVm is not null
            && _editor.Document.Text != _editorVm.DocumentText)
            _editorVm.DocumentText = _editor.Document.Text;
    }

    private void OnPreviewVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewViewModel.PreviewHtml) && _previewVm is not null)
            NavigateWebView(_previewVm.PreviewHtml);
    }

    private void OnFileServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileService.CurrentFilePath))
        {
            _snippetModeController?.Exit();
            _mainVm?.UpdateWindowTitleFromTab(_mainVm.ActiveTab);
            RefreshGitDiff(_fileService?.CurrentFilePath);
        }
    }

    private void OnFileSaved(object? sender, EventArgs e)
    {
        RefreshGitDiff(_fileService?.CurrentFilePath);
    }

    private async void OnDraftFound(object? sender, DraftFoundEventArgs args)
    {
        var tab = _mainVm?.Tabs.FirstOrDefault(t => t.FileService == sender as FileService);
        if (tab is null) return;

        var fileService = tab.FileService;
        var editorVm    = tab.EditorViewModel;

        var formatted = args.DraftTimestamp.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
        var restore = await ShowDraftRestoreDialog(formatted);
        if (restore)
        {
            var draftContent = await File.ReadAllTextAsync(args.DraftPath);
            await fileService.OpenFileSkipDraftCheck(args.FilePath);
            editorVm.DocumentText = draftContent;
        }
        else
        {
            fileService.DeleteDraft(args.FilePath);
            await fileService.OpenFileSkipDraftCheck(args.FilePath);
        }
    }

    // ─── Tab switching ──────────────────────────────────────────────────────────

    private void SwitchActiveTab(TabViewModel newTab)
    {
        // Block ALL cross-component sync (scroll, highlight, WebView messages)
        // until the tab switch fully settles.
        _tabSwitchInProgress = true;

        // Capture outgoing tab before _previousActiveTab is updated
        var outgoingTab = _previousActiveTab;

        // 0. Save scroll state of the tab we are leaving
        if (_previousActiveTab is { } outgoing && outgoing != newTab)
        {
            outgoing.SavedScrollY       = _savedScrollY;
            outgoing.SavedAnchorLine    = _lastSyncedAnchorLine;
            outgoing.SavedEditorScrollY = _editor?.TextArea.TextView.ScrollOffset.Y ?? 0;
            outgoing.SavedCaretLine     = _editor?.TextArea.Caret.Line ?? 1;
        }
        _previousActiveTab = newTab;

        // 1. Unsubscribe from current per-tab services
        if (_editorVm is not null)
            _editorVm.PropertyChanged -= OnEditorVmPropertyChanged;
        if (_previewVm is not null)
            _previewVm.PropertyChanged -= OnPreviewVmPropertyChanged;
        if (_fileService is not null)
        {
            _fileService.PropertyChanged -= OnFileServicePropertyChanged;
            _fileService.FileSaved       -= OnFileSaved;
            _fileService.DraftFound      -= OnDraftFound;
        }

        // 2. Swap references
        _editorVm             = newTab.EditorViewModel;
        _previewVm            = newTab.PreviewViewModel;
        _sourceMappingService = newTab.SourceMappingService;
        _fileService          = newTab.FileService;

        // 3. Re-subscribe to new tab's services
        _editorVm.PropertyChanged  += OnEditorVmPropertyChanged;
        _previewVm.PropertyChanged += OnPreviewVmPropertyChanged;
        _fileService.PropertyChanged += OnFileServicePropertyChanged;
        _fileService.FileSaved       += OnFileSaved;
        _fileService.DraftFound      += OnDraftFound;

        // 4. Swap TextDocument — each tab owns its own TextDocument instance,
        // so the undo stack is naturally per-tab. No UndoStack.ClearAll() needed.
        _isSyncingScroll = true;

        if (_editor is not null)
        {
            // Unsubscribe TextChanged from the outgoing document to prevent
            // the handler from pushing stale text to the wrong EditorViewModel.
            _editor.Document.TextChanged -= OnEditorDocumentTextChanged;

            // Swap to the incoming tab's document
            _editor.Document = newTab.Document;

            // If the document is empty but EditorViewModel has content
            // (e.g. after a file open before the document was swapped),
            // sync the content now.
            if (_editor.Document.Text != _editorVm.DocumentText)
                _editor.Document.Text = _editorVm.DocumentText ?? string.Empty;

            // Re-subscribe TextChanged on the new document
            _editor.Document.TextChanged += OnEditorDocumentTextChanged;
        }

        // 4b. Suppress MakeVisible until deferred scroll restore completes —
        // prevents intermediate layout from scrolling to stale selection/caret.
        if (_editor is not null)
        {
            _editor.SuppressMakeVisible = true;
            _editor.TextArea.ClearSelection();
        }

        // 4c. Defer scroll/caret/selection restore to after layout. Document.Text
        // replacement hasn't triggered a layout pass yet, so IScrollable.Offset
        // would clamp to the OLD document's extent. DispatcherPriority.Loaded
        // fires after the layout pass completes with the new content.
        var capturedEditor = _editor;
        var capturedTab = newTab;
        Dispatcher.UIThread.Post(() =>
        {
            if (capturedEditor is null) return;

            var targetScrollY = capturedTab.SavedEditorScrollY;
            var scrollable = (Avalonia.Controls.Primitives.IScrollable)capturedEditor.TextArea.TextView;

            // Set scroll directly on the TextView (IScrollable), not via ScrollViewer
            scrollable.Offset = new Avalonia.Vector(scrollable.Offset.X, targetScrollY);

            // Set caret to a visible line so BringCaretToView doesn't override scroll
            var lineHeight = capturedEditor.TextArea.TextView.DefaultLineHeight;
            var firstVisible = lineHeight > 0 ? Math.Max(1, (int)(targetScrollY / lineHeight) + 1) : 1;
            var caretLine = Math.Min(firstVisible, capturedEditor.Document.LineCount);
            capturedEditor.TextArea.Caret.Offset = 0;
            capturedEditor.TextArea.Caret.Line = caretLine;
            capturedEditor.TextArea.Caret.Column = 0;

            // Clear any carried-over selection (belt-and-suspenders — also cleared
            // synchronously above, but another layout pass could re-introduce one)
            capturedEditor.TextArea.ClearSelection();

            capturedEditor.SuppressMakeVisible = false;

            _lastEditorSyncTime = DateTime.UtcNow;
            _isSyncingScroll = false;
        }, DispatcherPriority.Background);
        //}, DispatcherPriority.Loaded); // MDH

        // Clear tab-switch guard after WebView settles.
        // Do NOT call ScheduleHighlightUpdate here — its scrollIntoView causes
        // a scroll drift via HandleScrollSync. The highlight will apply naturally
        // on the user's next interaction.
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(500);
            _tabSwitchInProgress = false;
            _lastEditorSyncTime = DateTime.UtcNow;
        });

        // 5. Restore incoming tab's scroll state before preview reload
        _savedScrollY         = newTab.SavedScrollY;
        _lastSyncedAnchorLine = newTab.SavedAnchorLine;
        _lastActiveSelector   = null;

        // 6. Trigger preview reload (NavigateWebView uses _savedScrollY)
        if (_webViewReady && _previewVm is not null)
            NavigateWebView(_previewVm.PreviewHtml);

        // 7. Refresh git diff for new file
        RefreshGitDiff(_fileService.CurrentFilePath);

        // 8. Update window title
        _mainVm?.UpdateWindowTitleFromTab(newTab);

        // 9. Notify ViewModel to rewire per-tab subscriptions (topology, outline, etc.)
        if (outgoingTab is not null && outgoingTab != newTab)
            _mainVm?.NotifyTabActivated(outgoingTab, newTab);

        // 10. Reset gutter sync state — preview reloads on switch so sync is fresh
        if (_mainVm is not null)
            _mainVm.GutterSyncState = GutterSyncState.Synced;
    }

    // ─── Tab strip interaction ───────────────────────────────────────────────────

    private void WireTabStripInteraction()
    {
        var tabStrip = this.FindControl<ItemsControl>("TabStrip");
        if (tabStrip is null || _mainVm is null) return;

        _mainVm.Tabs.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(WireTabItemHandlers, DispatcherPriority.Loaded);

        WireTabItemHandlers();
    }

    private void WireTabItemHandlers()
    {
        var tabStrip = this.FindControl<ItemsControl>("TabStrip");
        if (tabStrip is null || _mainVm is null) return;

        foreach (var tabVm in _mainVm.Tabs)
        {
            var container = tabStrip.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("tab-item") && b.DataContext == tabVm);

            if (container is null) continue;

            // Avoid double-wiring
            container.PointerPressed -= OnTabPointerPressed;
            container.PointerPressed += OnTabPointerPressed;

            // Wire close button
            var closeBtn = container.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(b => b.Tag is TabViewModel);
            if (closeBtn is not null)
            {
                closeBtn.Click -= OnTabCloseClick;
                closeBtn.Click += OnTabCloseClick;
            }
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not TabViewModel tab) return;

        var props = e.GetCurrentPoint(null).Properties;

        if (props.IsMiddleButtonPressed)
        {
            _ = _mainVm?.CloseTabCommand.ExecuteAsync(tab);
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            _mainVm?.SwitchTabCommand.Execute(tab);
        }
    }

    private void OnTabCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TabViewModel tab)
        {
            _ = _mainVm?.CloseTabCommand.ExecuteAsync(tab);
            e.Handled = true;
        }
    }

    // ─── AvaloniaEdit ─────────────────────────────────────────────────────────

    private void InitializeEditor()
    {
        _editor = this.FindControl<CenteringTextEditor>("MarkdownEditor");
        if (_editor is null) return;

        // Syntax highlighting disabled — AvaloniaEdit 12 xshd limitations.
        // Rule patterns fail on paste/Document.Replace; Span+\n End patterns
        // fail on load ($ injected as zero-width fallback). Custom IHighlighter
        // required. Deferred to v1.2. See docs/NOTES.md BL-12 section.
        // var uri    = new Uri("avares://GhsMarkdown.Cross/Assets/MarkdownSyntax.xshd");
        // var stream = AssetLoader.Open(uri);
        // using var reader = XmlReader.Create(stream);
        // var xshd = HighlightingLoader.LoadXshd(reader);
        // _editor.SyntaxHighlighting = HighlightingLoader.Load(xshd, HighlightingManager.Instance);

        // Syntax highlighting via DocumentColorizingTransformer
        // (replaces xshd — no zero-length match restrictions)
        ApplyMarkdownHighlighting();

        _editor.Options.ShowTabs                = false;
        _editor.Options.IndentationSize         = 2;
        _editor.Options.ConvertTabsToSpaces     = true;
        _editor.Options.HighlightCurrentLine    = _mainVm?.HighlightCurrentLine ?? true;
        _editor.ShowLineNumbers                 = _mainVm?.ShowLineNumbers      ?? true;
        _editor.WordWrap                        = _mainVm?.WordWrap             ?? false;
        _editor.Options.AllowScrollBelowDocument = true;

        // Live-apply editor toggle changes from context menu / VM
        if (_mainVm is not null)
        {
            _mainVm.PropertyChanged += (_, pe) =>
            {
                if (_editor is null) return;
                switch (pe.PropertyName)
                {
                    case nameof(MainWindowViewModel.WordWrap):
                        _editor.WordWrap = _mainVm.WordWrap;
                        break;
                    case nameof(MainWindowViewModel.ShowLineNumbers):
                        _editor.ShowLineNumbers = _mainVm.ShowLineNumbers;
                        break;
                    case nameof(MainWindowViewModel.HighlightCurrentLine):
                        _editor.Options.HighlightCurrentLine = _mainVm.HighlightCurrentLine;
                        break;
                }
            };
        }

        // Assign the initial tab's document so undo history is scoped correctly
        // from the very first keystroke. Without this, the editor starts with
        // AvaloniaEdit's default TextDocument, not the TabViewModel's instance.
        if (_mainVm?.ActiveTab is { } initialTab)
        {
            _editor.Document = initialTab.Document;

            // Pull initial text from ViewModel into the tab's document
            if (_editorVm is not null && !string.IsNullOrEmpty(_editorVm.DocumentText))
                _editor.Document.Text = _editorVm.DocumentText;
        }
        else if (_editorVm is not null && !string.IsNullOrEmpty(_editorVm.DocumentText))
        {
            _editor.Document.Text = _editorVm.DocumentText;
        }

        // Push text changes to EditorViewModel
        _editor.Document.TextChanged += OnEditorDocumentTextChanged;

        // Keep editor in sync when ViewModel text changes externally (Open, New)
        if (_editorVm is not null)
            _editorVm.PropertyChanged += OnEditorVmPropertyChanged;

        RegisterEditorCommands();

        // Snippet mode: intercept Tab/Escape/Enter before AvaloniaEdit handles them
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Double-click on a fence line opens the language picker
        _editor.TextArea.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2) return;
            var lang = GetFenceLanguage();
            if (lang is null) return;
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
                CodeLanguagePickerWindow.Show(this, "Change Language",
                    newLang => ChangeCodeLanguage(newLang)),
                Avalonia.Threading.DispatcherPriority.Input);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Scroll sync — subscribe to editor scroll events
        _editor.TextArea.TextView.ScrollOffsetChanged += OnEditorScrollChanged;

        // Caret changes: word count + active block highlight + topology tracking
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            var offset = _editor.TextArea.Caret.Offset;
            var line   = _editor.TextArea.Caret.Line;
            var col    = _editor.TextArea.Caret.Column + 1; // 1-based display

            // Update status bar line/col immediately (total word count updates async)
            if (_mainVm is not null)
                _mainVm.StatusBarText = $"Ln {line} · Col {col} · {_lastTotalWordCount} words";

            ScheduleWordCountUpdate(offset, line, col);
            ScheduleHighlightUpdate(line);

            // Feed caret line to EditorViewModel for TopologyViewModel subscription
            if (_editorVm is not null)
                _editorVm.CaretLine = line;
        };

        // Wire focus-return delegate for toolbar buttons (must be after _editor is confirmed non-null)
        _mainVm?.SetFocusEditorAction(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _editor?.Focus();
                _editor?.TextArea.Focus();
            }, DispatcherPriority.Input);
        });

        // Git diff margin
        _gitDiffMargin = new GitDiffMargin();
        _editor.TextArea.LeftMargins.Add(_gitDiffMargin);

        // Refresh diff on file open/save
        _fileService!.PropertyChanged += OnFileServicePropertyChanged;
        _fileService.FileSaved += OnFileSaved;

        RefreshGitDiff(_fileService.CurrentFilePath);

        AttachEditorContextMenu();
    }

    // ─── Editor context menu ─────────────────────────────────────────────────

    private void AttachEditorContextMenu()
    {
        if (_editor is null || _mainVm is null) return;

        // ── Toggle menu items (IsChecked updated on Opening) ─────────────────
        var wordWrapItem      = new MenuItem { Header = "Word Wrap",              ToggleType = MenuItemToggleType.CheckBox };
        var showLineNumsItem  = new MenuItem { Header = "Show Line Numbers",      ToggleType = MenuItemToggleType.CheckBox };
        var highlightLineItem = new MenuItem { Header = "Highlight Current Line", ToggleType = MenuItemToggleType.CheckBox };

        wordWrapItem.Click      += (_, _) => _mainVm.ToggleWordWrapCommand.Execute(null);
        showLineNumsItem.Click  += (_, _) => _mainVm.ToggleShowLineNumbersCommand.Execute(null);
        highlightLineItem.Click += (_, _) => _mainVm.ToggleHighlightCurrentLineCommand.Execute(null);

        // ── Heading submenu ───────────────────────────────────────────────────
        var headingItem = new MenuItem { Header = "Heading" };
        headingItem.Items.Add(new MenuItem
        {
            Header = "H1 — Heading 1",
            Command = _mainVm.FormatH1Command,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D1, Avalonia.Input.KeyModifiers.Control)
        });
        headingItem.Items.Add(new MenuItem
        {
            Header = "H2 — Heading 2",
            Command = _mainVm.FormatH2Command,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D2, Avalonia.Input.KeyModifiers.Control)
        });
        headingItem.Items.Add(new MenuItem
        {
            Header = "H3 — Heading 3",
            Command = _mainVm.FormatH3Command,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.D3, Avalonia.Input.KeyModifiers.Control)
        });

        // ── Cut ───────────────────────────────────────────────────────────────
        var cutItem = new MenuItem
        {
            Header = "Cut",
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.X, Avalonia.Input.KeyModifiers.Control)
        };
        cutItem.Click += (_, _) =>
        {
            if (_editor is null || _editor.TextArea.Selection.IsEmpty) return;
            var text = _editor.TextArea.Selection.GetText();
            var seg  = _editor.TextArea.Selection.SurroundingSegment;
            if (TopLevel.GetTopLevel(_editor)?.Clipboard is { } cb)
                _ = Avalonia.Input.Platform.ClipboardExtensions.SetValueAsync(cb, Avalonia.Input.DataFormat.Text, text);
            _editor.Document.Remove(seg.Offset, seg.Length);
        };

        // ── Copy ──────────────────────────────────────────────────────────────
        var copyItem = new MenuItem
        {
            Header = "Copy",
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.C, Avalonia.Input.KeyModifiers.Control)
        };
        copyItem.Click += (_, _) =>
        {
            if (_editor is null || _editor.TextArea.Selection.IsEmpty) return;
            var text = _editor.TextArea.Selection.GetText();
            if (TopLevel.GetTopLevel(_editor)?.Clipboard is { } cb)
                _ = Avalonia.Input.Platform.ClipboardExtensions.SetValueAsync(cb, Avalonia.Input.DataFormat.Text, text);
        };

        // ── Paste ─────────────────────────────────────────────────────────────
        var pasteItem = new MenuItem
        {
            Header = "Paste",
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.V, Avalonia.Input.KeyModifiers.Control)
        };
        pasteItem.Click += async (_, _) =>
        {
            if (_editor is null) return;
            var clipboard = TopLevel.GetTopLevel(_editor)?.Clipboard;
            if (clipboard is null) return;
            var text = await Avalonia.Input.Platform.ClipboardExtensions.TryGetTextAsync(clipboard);
            if (text is null) return;
            if (!_editor.TextArea.Selection.IsEmpty)
            {
                var seg = _editor.TextArea.Selection.SurroundingSegment;
                _editor.Document.Remove(seg.Offset, seg.Length);
            }
            _editor.Document.Insert(_editor.TextArea.Caret.Offset, text);
        };

        // ── Select All ────────────────────────────────────────────────────────
        var selectAllItem = new MenuItem
        {
            Header = "Select All",
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.A, Avalonia.Input.KeyModifiers.Control)
        };
        selectAllItem.Click += (_, _) =>
        {
            if (_editor is null) return;
            _editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(
                _editor.TextArea, 0, _editor.Document.TextLength);
        };

        // ── Assemble menu ──────────────────────────────────────────────────────
        var menu = new ContextMenu();

        // Formatting
        menu.Items.Add(new MenuItem
        {
            Header = "Bold",
            Command = _mainVm.FormatBoldCommand,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.B, Avalonia.Input.KeyModifiers.Control)
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Italic",
            Command = _mainVm.FormatItalicCommand,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.I, Avalonia.Input.KeyModifiers.Control)
        });
        menu.Items.Add(new MenuItem { Header = "Strikethrough", Command = _mainVm.FormatStrikethroughCommand });
        menu.Items.Add(new MenuItem { Header = "Inline Code",   Command = _mainVm.FormatCodeCommand });
        var codeBlockItem = new MenuItem { Header = "Code Block..." };
        codeBlockItem.Click += (_, _) =>
            CodeLanguagePickerWindow.Show(this, "Insert Code Block",
                lang => InsertCodeBlock(lang));
        menu.Items.Add(codeBlockItem);
        menu.Items.Add(new Separator());

        // Headings
        menu.Items.Add(headingItem);
        menu.Items.Add(new MenuItem
        {
            Header = "Link",
            Command = _mainVm.FormatLinkCommand,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.K, Avalonia.Input.KeyModifiers.Control)
        });
        menu.Items.Add(new MenuItem { Header = "Image", Command = _mainVm.FormatImageCommand });
        menu.Items.Add(new Separator());

        // Toggles
        menu.Items.Add(wordWrapItem);
        menu.Items.Add(showLineNumsItem);
        menu.Items.Add(highlightLineItem);
        menu.Items.Add(new Separator());

        // Edit
        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(selectAllItem);
        menu.Items.Add(new Separator());

        // File
        menu.Items.Add(new MenuItem
        {
            Header = "Save",
            Command = _mainVm.SaveFileCommand,
            InputGesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.S, Avalonia.Input.KeyModifiers.Control)
        });
        menu.Items.Add(new MenuItem
        {
            Header = "Save As\u2026",
            Command = _mainVm.SaveFileAsCommand,
            InputGesture = new Avalonia.Input.KeyGesture(
                Avalonia.Input.Key.S,
                Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift)
        });

        // Change Language (visible only when caret is on a ``` fence line)
        var changeLangItem = new MenuItem { Header = "Change Language..." };
        changeLangItem.Click += (_, _) =>
        {
            CodeLanguagePickerWindow.Show(this, "Change Language",
                lang => ChangeCodeLanguage(lang));
        };
        menu.Items.Add(changeLangItem);

        // Update checked state each time the menu opens
        menu.Opening += (_, _) =>
        {
            wordWrapItem.IsChecked      = _mainVm.WordWrap;
            showLineNumsItem.IsChecked  = _mainVm.ShowLineNumbers;
            highlightLineItem.IsChecked = _mainVm.HighlightCurrentLine;
            changeLangItem.IsVisible    = GetFenceLanguage() is not null;
        };

        // Attach to the TextArea (overrides AvaloniaEdit's built-in context menu)
        _editor.TextArea.ContextMenu = menu;
    }

    // ─── Editor theme colors ─────────────────────────────────────────────────

    private void ApplyEditorTheme(string themeName)
    {
        if (_editor is null) return;

        if (themeName == "GHS Light")
        {
            _editor.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A1A1A"));
            _editor.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F9F6F0"));
        }
        else if (themeName == "GHS Custom")
        {
            var settingsSvc = App.Services.GetService(typeof(SettingsService)) as SettingsService;
            var colors = settingsSvc?.Load().CustomThemeColors
                         ?? new Dictionary<string, string>();

            var bg = colors.TryGetValue("bg-editor", out var bgHex) ? bgHex : "#1E1E1E";
            var fg = colors.TryGetValue("text-primary", out var fgHex) ? fgHex : "#E8E8E8";

            _editor.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(bg));
            _editor.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(fg));
        }
        else // GHS Dark (default)
        {
            _editor.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8E8E8"));
            _editor.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E1E"));
        }

        _editor.TextArea.Foreground = _editor.Foreground;
    }

    private void ApplyMarkdownHighlighting()
    {
        if (_editor is null) return;
        var transformers = _editor.TextArea.TextView.LineTransformers;

        var existing = transformers
            .OfType<MarkdownColorizingTransformer>()
            .ToList();
        foreach (var t in existing)
            transformers.Remove(t);

        var isLight = _mainVm?.IsThemeLight == true;

        var themeService = App.Services.GetService(typeof(ThemeService)) as ThemeService;
        var customColors = themeService?.CurrentTheme == GhsTheme.Custom
            ? _mainVm?.CustomColors
            : null;

        transformers.Add(new MarkdownColorizingTransformer(isLight, customColors));
    }

    // ─── Editor font ──────────────────────────────────────────────────────────

    private void ApplyEditorFont(string fontFamily, double fontSize)
    {
        if (_editor is null) return;
        _editor.FontFamily = new Avalonia.Media.FontFamily(fontFamily);
        _editor.FontSize = fontSize;
    }

    // ─── Snippet mode key interception ──────────────────────────────────────

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // ── Editor formatting shortcuts ──────────────────────────────
        if (ctrl && !shift && _commandRegistry is not null)
        {
            string? cmdId = e.Key switch
            {
                Key.B        => "editor.bold",
                Key.I        => "editor.italic",
                Key.OemTilde => "editor.inlineCode",   // Ctrl+`
                Key.K        => "editor.link",
                Key.D1       => "editor.h1",
                Key.D2       => "editor.h2",
                Key.D3       => "editor.h3",
                Key.D4       => "editor.h4",
                Key.D5       => "editor.h5",
                Key.D6       => "editor.h6",
                _            => null
            };
            if (cmdId is not null)
            {
                _commandRegistry.Execute(cmdId);
                e.Handled = true;
                return;
            }
        }

        if (ctrl && shift && e.Key == Key.H && _commandRegistry is not null)
        {
            _commandRegistry.Execute("editor.hr");
            e.Handled = true;
            return;
        }

        // ── Snippet mode: Tab / Escape / Enter ───────────────────────
        if (_snippetModeController is null || !_snippetModeController.IsActive) return;

        if (e.Key == Key.Tab)
        {
            _snippetModeController.MoveNext(_editor!);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _snippetModeController.Exit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _snippetModeController.Exit();
            // Let Enter propagate normally (inserts newline)
        }
    }

    /// <summary>Called by SnippetStudioViewModel (via delegate) to insert a snippet into the editor.</summary>
    public void InsertSnippetIntoEditor(GhsMarkdown.Cross.Models.Snippet snippet)
    {
        if (_editor is null || _snippetInsertionService is null) return;
        _snippetInsertionService.InsertSnippet(snippet, _editor);
    }

    // ─── Editor command registration ─────────────────────────────────────────

    private void InsertCodeBlock(string language)
    {
        if (_editor is null) return;
        var ta     = _editor.TextArea;
        var line   = _editor.Document.GetLineByNumber(ta.Caret.Line);
        var insert = $"\n```{language}\n\n```\n";
        var offset = line.EndOffset;
        _editor.Document.Insert(offset, insert);
        // Position caret inside the block (after the opening fence + newline)
        ta.Caret.Offset = offset + 1 + language.Length + 4; // \n```lang\n
        _editor.Focus();
        _editor.TextArea.Focus();
    }

    private string? GetFenceLanguage()
    {
        if (_editor is null) return null;
        var lineNum  = _editor.TextArea.Caret.Line;
        var line     = _editor.Document.GetLineByNumber(lineNum);
        var lineText = _editor.Document.GetText(line.Offset, line.Length).TrimStart();
        if (!lineText.StartsWith("```")) return null;
        var lang = lineText.Substring(3).Trim();
        return lang; // may be empty string for plain fences
    }

    private void ChangeCodeLanguage(string newLanguage)
    {
        if (_editor is null) return;
        var lineNum  = _editor.TextArea.Caret.Line;
        var line     = _editor.Document.GetLineByNumber(lineNum);
        var lineText = _editor.Document.GetText(line.Offset, line.Length);
        if (!lineText.TrimStart().StartsWith("```")) return;
        // Replace entire line with new fence + language
        var indent = lineText.Length - lineText.TrimStart().Length;
        var newLine = lineText.Substring(0, indent) + "```" + newLanguage;
        _editor.Document.Replace(line.Offset, line.Length, newLine);
    }

    private void RegisterEditorCommands()
    {
        var registry = App.Services.GetService(typeof(CommandRegistry)) as CommandRegistry;
        if (registry is null || _editor is null) return;
        _commandRegistry = registry;

        void WrapSelection(string prefix, string suffix)
        {
            var ta = _editor!.TextArea;
            var sel = ta.Selection;
            if (sel.IsEmpty)
            {
                var offset = ta.Caret.Offset;
                _editor.Document.Insert(offset, prefix + suffix);
                ta.Caret.Offset = offset + prefix.Length;
            }
            else
            {
                var text = sel.GetText();
                var start = sel.SurroundingSegment.Offset;
                var length = sel.SurroundingSegment.Length;
                _editor.Document.Replace(start, length, prefix + text + suffix);
                ta.Caret.Offset = start + prefix.Length + text.Length + suffix.Length;
            }
        }

        void SetHeading(int level)
        {
            var ta = _editor!.TextArea;
            var line = _editor.Document.GetLineByNumber(ta.Caret.Line);
            var lineText = _editor.Document.GetText(line.Offset, line.Length);
            // Strip existing heading prefix
            var stripped = System.Text.RegularExpressions.Regex.Replace(lineText, @"^#{1,6}\s*", "");
            var prefix = new string('#', level) + " ";
            _editor.Document.Replace(line.Offset, line.Length, prefix + stripped);
        }

        registry.Register(new CommandDescriptor("editor.bold",       "Bold",                    "Editor", () => WrapSelection("**", "**"), "Ctrl+B"));
        registry.Register(new CommandDescriptor("editor.italic",     "Italic",                  "Editor", () => WrapSelection("*", "*"), "Ctrl+I"));
        registry.Register(new CommandDescriptor("editor.inlineCode", "Inline Code",             "Editor", () => WrapSelection("`", "`"), "Ctrl+`"));
        registry.Register(new CommandDescriptor("editor.link",       "Insert Link",             "Editor", () => WrapSelection("[", "](url)"), "Ctrl+K"));
        registry.Register(new CommandDescriptor("editor.hr",         "Insert Horizontal Rule",  "Editor", () =>
        {
            var ta = _editor!.TextArea;
            var line = _editor.Document.GetLineByNumber(ta.Caret.Line);
            _editor.Document.Insert(line.Offset + line.Length, "\n\n---\n");
        }, "Ctrl+Shift+H"));
        registry.Register(new CommandDescriptor("editor.h1", "Heading 1", "Editor", () => SetHeading(1), "Ctrl+1"));
        registry.Register(new CommandDescriptor("editor.h2", "Heading 2", "Editor", () => SetHeading(2), "Ctrl+2"));
        registry.Register(new CommandDescriptor("editor.h3", "Heading 3", "Editor", () => SetHeading(3), "Ctrl+3"));
        registry.Register(new CommandDescriptor("editor.h4", "Heading 4", "Editor", () => SetHeading(4), "Ctrl+4"));
        registry.Register(new CommandDescriptor("editor.h5", "Heading 5", "Editor", () => SetHeading(5), "Ctrl+5"));
        registry.Register(new CommandDescriptor("editor.h6", "Heading 6", "Editor", () => SetHeading(6), "Ctrl+6"));

        // New commands for formatting toolbar
        registry.Register(new CommandDescriptor("editor.strikethrough", "Strikethrough",
            "Editor", () => WrapSelection("~~", "~~"), "Ctrl+Shift+X"));
        registry.Register(new CommandDescriptor("editor.unorderedList", "Unordered List",
            "Editor", () =>
            {
                var ta = _editor!.TextArea;
                var line = _editor.Document.GetLineByNumber(ta.Caret.Line);
                var text = _editor.Document.GetText(line.Offset, line.Length);
                if (!text.StartsWith("- "))
                    _editor.Document.Insert(line.Offset, "- ");
            }, "Ctrl+Shift+U"));
        registry.Register(new CommandDescriptor("editor.orderedList", "Ordered List",
            "Editor", () =>
            {
                var ta = _editor!.TextArea;
                var line = _editor.Document.GetLineByNumber(ta.Caret.Line);
                var text = _editor.Document.GetText(line.Offset, line.Length);
                if (!text.StartsWith("1. "))
                    _editor.Document.Insert(line.Offset, "1. ");
            }, "Ctrl+Shift+O"));
        registry.Register(new CommandDescriptor("editor.table", "Insert Table",
            "Editor", () =>
            {
                var ta = _editor!.TextArea;
                var line = _editor.Document.GetLineByNumber(ta.Caret.Line);
                _editor.Document.Insert(line.EndOffset,
                    "\n| Column 1 | Column 2 | Column 3 |\n| --- | --- | --- |\n| Cell | Cell | Cell |\n");
            }, "Ctrl+Shift+G"));
        registry.Register(new CommandDescriptor("editor.image", "Insert Image",
            "Editor", () => WrapSelection("![", "](image-url)"), "Ctrl+Shift+I"));
        registry.Register(new CommandDescriptor("editor.codeBlock", "Insert Code Block",
            "Editor", () =>
            {
                CodeLanguagePickerWindow.Show(this, "Insert Code Block",
                    lang => InsertCodeBlock(lang));
            }));

        // App — About dialog. Version read from assembly, not hardcoded.
        var aboutVersion = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version;
        var aboutHint = aboutVersion is null
            ? "GHS Markdown"
            : $"GHS Markdown v{aboutVersion.Major}.{aboutVersion.Minor}.{aboutVersion.Build}";
        registry.Register(new CommandDescriptor(
            "app.about",
            "About GHS Markdown Editor",
            "App",
            () => new AboutWindow().Show(this),
            null,
            aboutHint));
    }

    // ─── NativeWebView ────────────────────────────────────────────────────────

    private void InitializeWebView()
    {
        _webView = this.FindControl<Avalonia.Controls.NativeWebView>("PreviewWebView");
        if (_webView is null) return;

        try
        {
            _webView.EnvironmentRequested += (_, args) =>
            {
                var prop = args.GetType().GetProperty("UserDataFolder");
                prop?.SetValue(args, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GHSMarkdownEditor", "WebView2Main"));
            };

            _webView.WebMessageReceived += OnWebViewMessage;

            _webView.NavigationCompleted += OnNavigationCompleted;

            _webView.AdapterCreated += (_, _) =>
            {
                _webViewReady = true;
                if (_previewVm is not null)
                    NavigateWebView(_previewVm.PreviewHtml);
            };

            _mainVm?.SetPrintAction(() =>
            {
                if (_webViewReady && _webView is not null)
                    _ = _webView.InvokeScript("window.print()");
            });

            _mainVm?.SetFocusModeAction(active =>
            {
                ApplyFocusModeStyles(active);
                if (_editor is not null)
                    _editor.TypewriterMode = active;
            });
            if (_mainVm?.IsFocusModeActive == true)
            {
                ApplyFocusModeStyles(true);
                if (_editor is not null)
                    _editor.TypewriterMode = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView init failed: {ex.Message}");
            // App continues without preview — editor still functional
        }
    }

    // ─── Focus Mode — prose dimming in preview ───────────────────────────────

    private void ApplyFocusModeStyles(bool active)
    {
        if (!_webViewReady || _webView is null) return;

        if (active)
        {
            _ = _webView.InvokeScript("""
                (function() {
                    var style = document.getElementById('ghs-focus-style');
                    if (!style) {
                        style = document.createElement('style');
                        style.id = 'ghs-focus-style';
                        document.head.appendChild(style);
                    }
                    style.textContent =
                        'body > *:not(.ghs-active) { opacity: 0.2; transition: opacity 0.3s ease; }' +
                        'body > .ghs-active { opacity: 1.0; transition: opacity 0.3s ease; }';
                })();
            """);
        }
        else
        {
            _ = _webView.InvokeScript("""
                (function() {
                    var style = document.getElementById('ghs-focus-style');
                    if (style) style.remove();
                })();
            """);
        }
    }

    // ─── Detached Preview ─────────────────────────────────────────────────────

    private void ToggleDetachedPreview(SettingsService settingsService)
    {
        if (_detachedWindow is not null)
        {
            // Already detached — bring to front
            _detachedWindow.Activate();
            return;
        }

        // Load saved position
        var s = settingsService.Load();

        // Switch main window to Edit-only mode
        if (_mainVm is not null)
        {
            _mainVm.CurrentViewMode = ViewMode.Edit;
            _mainVm.IsPreviewDetached = true;
        }

        // Create and show detached window
        _detachedWindow = new DetachedPreviewWindow(
            _previewVm!,
            settingsService,
            s.DetachedPreviewX,
            s.DetachedPreviewY,
            s.DetachedPreviewWidth,
            s.DetachedPreviewHeight);

        _detachedWindow.DetachedWindowClosed += (_, _) =>
        {
            _detachedWindow = null;
            // Return to Split mode when detached window closes
            Dispatcher.UIThread.Post(() =>
            {
                if (_mainVm is not null)
                {
                    _mainVm.IsPreviewDetached = false;
                    _mainVm.CurrentViewMode = ViewMode.Split;
                }
            });
        };

        _detachedWindow.Show();
    }

    private void NavigateWebView(string html)
    {
        if (!_webViewReady || _webView is null) return;

        // Inject scroll-restore script into HTML if we have a saved position
        if (_savedScrollY > 1 && _previewVm?.IsTimelinePreviewActive != true)
        {
            var y = _savedScrollY.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            var script = $"\n<script>window.addEventListener('load',function(){{window.scrollTo({{top:{y},behavior:'instant'}});}});</script>";
            html = html.Replace("</body>", script + "\n</body>");
        }

        _lastProgrammaticScrollFraction = 0;
        _lastActiveSelector = null; // DOM will be replaced
        _webView.NavigateToString(html);
    }

    private void OnNavigationCompleted(object? sender, EventArgs e)
    {
        _lastActiveSelector = null;
        if (_previewVm?.IsTimelinePreviewActive == true) return;
        InjectAllListeners();

        // Re-apply highlight for current caret line (skip during tab switch —
        // the tab-switch guard will re-apply after settling)
        if (_editor is not null && !_tabSwitchInProgress)
            ScheduleHighlightUpdate(_editor.TextArea.Caret.Line);

        // Re-inject focus mode styles if active (NavigateToString destroys injected styles)
        if (_mainVm?.IsFocusModeActive == true)
            ApplyFocusModeStyles(true);
    }

    // ─── JS injection (consolidated, all three listeners) ────────────────────

    private void InjectAllListeners()
    {
        if (!_webViewReady || _webView is null) return;

        const string js = """
            (function() {
              // 1. Scroll reporting with anchor
              window._ghsScrolling = false;
              window.addEventListener('scroll', function() {
                if (window._ghsScrolling) return;
                // Find the topmost element that is at least partially in the viewport
                var anchorLine = null;
                var els = document.querySelectorAll('[data-source-line]');
                for (var i = 0; i < els.length; i++) {
                  var rect = els[i].getBoundingClientRect();
                  if (rect.bottom > 0) {
                    anchorLine = parseInt(els[i].getAttribute('data-source-line'));
                    break;
                  }
                }
                try {
                  window.chrome.webview.postMessage(JSON.stringify({
                    type: 'scroll',
                    y: window.scrollY,
                    h: document.body.scrollHeight - window.innerHeight,
                    anchorLine: anchorLine
                  }));
                } catch(e) {}
              });

              // 2. Click-sync
              document.addEventListener('click', function(e) {
                var el = e.target.closest('[data-source-line]');
                if (!el) return;
                var line = el.getAttribute('data-source-line');
                try {
                  window.chrome.webview.postMessage(JSON.stringify({
                    type: 'click-sync',
                    sourceLine: parseInt(line)
                  }));
                } catch(ex) {}
              });

              // 3. Double-click inline-edit
              document.addEventListener('dblclick', function(e) {
                var el = e.target.closest('[data-source-line]');
                if (!el) return;
                var line    = el.getAttribute('data-source-line');
                var endLine = el.getAttribute('data-source-end');
                var tag     = el.tagName.toLowerCase();
                try {
                  window.chrome.webview.postMessage(JSON.stringify({
                    type:          'inline-edit',
                    sourceLine:    parseInt(line),
                    sourceEndLine: parseInt(endLine),
                    tag:           tag
                  }));
                } catch(ex) {}
              });

              // 4. Forward keyboard shortcuts to host (WebView captures OS-level input)
              document.addEventListener('keydown', function(e) {
                var ctrl = e.ctrlKey, shift = e.shiftKey, key = e.key;
                function send(id) {
                  e.preventDefault(); e.stopPropagation();
                  var sel = window.getSelection();
                  var selectedText = sel ? sel.toString() : '';
                  var anchorEl = sel && sel.anchorNode ? sel.anchorNode.parentElement : null;
                  var sourceLine = null;
                  if (anchorEl) {
                    var block = anchorEl.closest('[data-source-line]');
                    if (block) sourceLine = parseInt(block.getAttribute('data-source-line'));
                  }
                  try { window.chrome.webview.postMessage(
                    JSON.stringify({type:'shortcut',commandId:id,selectedText:selectedText,previewSourceLine:sourceLine})); } catch(ex) {}
                }
                if (ctrl && !shift) {
                  if (key==='n'||key==='N') return send('file.new');
                  if (key==='o'||key==='O') return send('file.open');
                  if (key==='s'||key==='S') return send('file.save');
                  if (key==='b'||key==='B') return send('editor.bold');
                  if (key==='i'||key==='I') return send('editor.italic');
                  if (key==='k'||key==='K') return send('editor.link');
                  if (key==='p'||key==='P') return send('palette.open');
                  if (key==='d'||key==='D') return send('view.detachPreview');
                  if (key==='1') return send('editor.h1');
                  if (key==='2') return send('editor.h2');
                  if (key==='3') return send('editor.h3');
                  if (key==='4') return send('editor.h4');
                  if (key==='5') return send('editor.h5');
                  if (key==='6') return send('editor.h6');
                }
                if (ctrl && shift) {
                  if (key==='S'||key==='s') return send('file.saveAs');
                  if (key==='E'||key==='e') return send('view.editOnly');
                  if (key==='T'||key==='t') return send('view.split');
                  if (key==='W'||key==='w') return send('view.previewOnly');
                  if (key==='H'||key==='h') return send('editor.hr');
                  if (key==='F'||key==='f') return send('view.focusMode');
                  if (key==='B'||key==='b') return send('view.formattingToolbar');
                  if (key==='P'||key==='p') return send('file.print');
                  if (key==='X'||key==='x') return send('editor.strikethrough');
                  if (key==='U'||key==='u') return send('editor.unorderedList');
                  if (key==='O'||key==='o') return send('editor.orderedList');
                  if (key==='G'||key==='g') return send('editor.table');
                  if (key==='I'||key==='i') return send('editor.image');
                }
              });
            })();
            """;

        _ = _webView.InvokeScript(js);
    }

    // ─── WebView message dispatcher ───────────────────────────────────────────

    private void OnWebViewMessage(object? sender, Avalonia.Controls.WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Body)) return;

        try
        {
            var msg = JsonSerializer.Deserialize<WebMessage>(e.Body);
            switch (msg?.Type)
            {
                case "scroll":
                    if (_tabSwitchInProgress) break;
                    _savedScrollY = msg.ScrollY;
                    HandleScrollSync(msg.ScrollY, msg.ScrollH, msg.AnchorLine);
                    break;
                case "click-sync":
                    HandleClickSync(msg.SourceLine);
                    break;
                case "inline-edit":
                    HandleInlineEdit(msg.SourceLine, msg.SourceEndLine, msg.Tag);
                    break;
                case "shortcut":
                    var cmdId        = msg.CommandId;
                    var selectedText = msg.SelectedText;
                    var previewLine  = msg.PreviewSourceLine;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (cmdId is null) return;

                        // For editor formatting commands with a preview selection:
                        // sync the editor to that source line and select the matching text
                        if (cmdId.StartsWith("editor.") &&
                            !string.IsNullOrEmpty(selectedText) &&
                            previewLine.HasValue &&
                            _editor is not null &&
                            _editorVm is not null)
                        {
                            var docText = _editorVm.DocumentText;
                            var lines   = docText.Split('\n');
                            var lineIdx = previewLine.Value - 1; // 0-based

                            // Search for selectedText within a window around the source line
                            var searchStart = Math.Max(0, lineIdx - 2);
                            var searchEnd   = Math.Min(lines.Length - 1, lineIdx + 5);

                            // Build offset of searchStart line
                            var lineOffset = 0;
                            for (int i = 0; i < searchStart; i++)
                                lineOffset += lines[i].Length + 1; // +1 for \n

                            // Build the text chunk to search in
                            var chunk = string.Join('\n', lines[searchStart..(searchEnd + 1)]);

                            // Find selectedText in chunk
                            var plainSelected = selectedText.Trim();
                            var idx = chunk.IndexOf(plainSelected, StringComparison.Ordinal);

                            if (idx >= 0)
                            {
                                var absoluteOffset = lineOffset + idx;

                                _editor.TextArea.Caret.Line   = previewLine.Value;
                                _editor.TextArea.Caret.Offset = absoluteOffset;
                                _editor.ScrollToLine(previewLine.Value);

                                // Select the found text
                                _editor.TextArea.Selection =
                                    AvaloniaEdit.Editing.Selection.Create(
                                        _editor.TextArea,
                                        absoluteOffset,
                                        absoluteOffset + plainSelected.Length);
                            }
                            else
                            {
                                // Fallback: just move caret to source line, no selection
                                _editor.TextArea.Caret.Line = previewLine.Value;
                                _editor.ScrollToLine(previewLine.Value);
                            }
                        }

                        // Focus editor so the formatting command has a target
                        _editor?.Focus();
                        _editor?.TextArea.Focus();

                        // Execute the formatting command
                        if (cmdId == "palette.open")
                            _mainVm?.CommandPalette.Open();
                        else
                            _commandRegistry?.Execute(cmdId);
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebMsg] Parse error: {ex.Message}");
        }
    }

    // ─── Scroll sync handler ──────────────────────────────────────────────────

    private void HandleScrollSync(double scrollY, double scrollH, int? anchorLine)
    {
        if (_tabSwitchInProgress) return;
        if (_mainVm?.CurrentViewMode != ViewMode.Split) return;
        if (_isSyncingScroll || _editor is null) return;
        if (_previewVm?.IsTimelinePreviewActive == true) return;
        if ((DateTime.UtcNow - _lastEditorSyncTime).TotalMilliseconds < ScrollSyncCooldownMs) return;

        if (anchorLine.HasValue && anchorLine.Value > 0)
        {
            // Anchor-based: skip if anchor hasn't changed
            if (anchorLine.Value == _lastSyncedAnchorLine) return;

            _isSyncingScroll = true;
            try
            {
                var lineNum = Math.Min(anchorLine.Value, _editor.Document.LineCount);
                var lineHeight = _editor.TextArea.TextView.DefaultLineHeight;
                var targetOffset = Math.Max(0, (lineNum - 1) * lineHeight);
                _editor.ScrollToVerticalOffset(targetOffset);
                _lastSyncedAnchorLine = anchorLine.Value;
                _lastProgrammaticScrollFraction = ComputeEditorScrollFraction();
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }
        else if (scrollH >= 1)
        {
            // Fallback: proportional (no anchors in document, e.g. plain paragraphs only)
            var previewFraction = Math.Clamp(scrollY / scrollH, 0.0, 1.0);
            if (Math.Abs(previewFraction - _lastProgrammaticScrollFraction) < 0.02) return;

            _isSyncingScroll = true;
            try
            {
                var docHeight = _editor.TextArea.TextView.DocumentHeight;
                var viewHeight = _editor.Bounds.Height;
                var maxEditorScroll = Math.Max(1, docHeight - viewHeight);
                _editor.ScrollToVerticalOffset(previewFraction * maxEditorScroll);
                _lastProgrammaticScrollFraction = previewFraction;
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }

        UpdateScrollSyncState();
    }

    // ─── Click-to-sync handler ────────────────────────────────────────────────

    private void HandleClickSync(int sourceLine)
    {
        var mode = _mainVm?.CurrentViewMode;
        if (mode == ViewMode.Edit) return; // Only in Split and Preview

        if (_editor is null || _sourceMappingService is null) return;

        var selector = _sourceMappingService.GetElementSelector(sourceLine);
        if (selector is null) return;

        // Suppress OnEditorScrollChanged from re-syncing the preview.
        // ScrollToLine fires ScrollOffsetChanged synchronously; resetting via
        // Dispatcher.Post(Background) ensures the flag is still true when the
        // event handler runs, then clears after the layout pass.
        _isSyncingScroll = true;
        _lastSyncedAnchorLine = sourceLine;

        _editor.TextArea.Caret.Line = sourceLine;
        _editor.ScrollToLine(sourceLine);

        Dispatcher.UIThread.Post(
            () => _isSyncingScroll = false,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    // ─── Inline edit handler ──────────────────────────────────────────────────

    private void HandleInlineEdit(int sourceLine, int sourceEndLine, string? tag)
    {
        var mode = _mainVm?.CurrentViewMode;
        if (mode == ViewMode.Edit) return; // Only in Split and Preview

        if (_editorVm is null || tag is null) return;

        // Extract the source lines
        var fullText = _editorVm.DocumentText;
        var lines    = fullText.Split('\n');

        var s = sourceLine    - 1;  // 0-based
        var e = sourceEndLine - 1;  // 0-based inclusive

        if (s < 0 || s >= lines.Length) return;
        e = Math.Min(e, lines.Length - 1);

        var blockMarkdown = string.Join('\n', lines[s..(e + 1)]);

        InlineEditWindow.TryOpen(this, tag, blockMarkdown, newText =>
        {
            var updated = ReplaceLines(_editorVm.DocumentText, sourceLine, sourceEndLine, newText);
            _editorVm.DocumentText = updated;
            _editor?.ScrollToLine(sourceLine);
        });
    }

    // ─── Active block highlight ───────────────────────────────────────────────

    private void ScheduleHighlightUpdate(int caretLine)
    {
        _highlightCts?.Cancel();
        _highlightCts = new CancellationTokenSource();
        var token = _highlightCts.Token;

        Task.Delay(80, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(() => UpdateActiveBlockHighlight(caretLine));
        }, TaskScheduler.Default);
    }

    private void UpdateActiveBlockHighlight(int caretLine)
    {
        if (_tabSwitchInProgress) return;
        if (!_webViewReady || _webView is null) return;
        if (_mainVm?.CurrentViewMode == ViewMode.Edit) return;
        if (_previewVm?.IsTimelinePreviewActive == true) return;
        if (_sourceMappingService is null) return;

        string? selector = null;
        int anchorLine = caretLine;
        for (int line = caretLine; line >= 1; line--)
        {
            selector = _sourceMappingService.GetElementSelector(line);
            if (selector is not null) { anchorLine = line; break; }
        }

        if (selector == _lastActiveSelector) return;
        _lastActiveSelector = selector;

        string js;
        if (selector is not null)
        {
            var escaped = selector.Replace("\\", "\\\\").Replace("'", "\\'");
            // Highlight AND bring into view. block:'nearest' only scrolls if the element
            // is outside the viewport — no jump if it is already visible.
            js = "(function() {" +
                 "  var prev = document.querySelector('.ghs-active');" +
                 "  if (prev) prev.classList.remove('ghs-active');" +
                 $"  var el = document.querySelector('{escaped}');" +
                 "  if (el) {" +
                 "    el.classList.add('ghs-active');" +
                 "    el.scrollIntoView({behavior:'instant', block:'nearest'});" +
                 "  }" +
                 "})();";

            // Stamp anchor so HandleScrollSync skips the echo when the preview
            // scroll event fires from the scrollIntoView call above.
            _lastSyncedAnchorLine = anchorLine;

            // Suppress HandleScrollSync for the echo scroll event duration.
            _isSyncingScroll = true;
            _ = _webView.InvokeScript(js);
            Dispatcher.UIThread.Post(
                () => _isSyncingScroll = false,
                Avalonia.Threading.DispatcherPriority.Background);
        }
        else
        {
            js = "(function() {" +
                 "  var prev = document.querySelector('.ghs-active');" +
                 "  if (prev) prev.classList.remove('ghs-active');" +
                 "})();";
            _ = _webView.InvokeScript(js);
        }
    }

    // ─── Editor → Preview scroll ──────────────────────────────────────────────

    private void OnEditorScrollChanged(object? sender, EventArgs e)
    {
        if (_tabSwitchInProgress) { return; }
        if (_isSyncingScroll || _editor is null) return;
        if (_previewVm?.IsTimelinePreviewActive == true) return;

        // Sync to detached preview (always proportional — anchor sync is main-window only)
        var fraction = ComputeEditorScrollFraction();
        _detachedWindow?.ScrollToFraction(fraction);

        if (_mainVm?.CurrentViewMode != ViewMode.Split) return;
        if (!_webViewReady || _webView is null) return;

        // Find first visible editor line, walk back to the nearest mapped block
        var firstLine = GetFirstVisibleLine();
        string? anchorSelector = null;
        int anchorLine = firstLine;

        for (int l = firstLine; l >= 1; l--)
        {
            anchorSelector = _sourceMappingService?.GetElementSelector(l);
            if (anchorSelector is not null) { anchorLine = l; break; }
        }

        if (anchorSelector is not null)
        {
            // Skip if the anchor block hasn't changed — avoids redundant InvokeScript calls
            if (anchorLine == _lastSyncedAnchorLine) return;
            _lastSyncedAnchorLine = anchorLine;

            _isSyncingScroll = true;
            try
            {
                _lastEditorSyncTime = DateTime.UtcNow;
                var escaped = anchorSelector.Replace("\\", "\\\\").Replace("'", "\\'");
                var script =
                    $"(function(){{" +
                    $"  window._ghsScrolling=true;" +
                    $"  var el=document.querySelector('{escaped}');" +
                    $"  if(el) el.scrollIntoView({{behavior:'instant',block:'start'}});" +
                    $"  setTimeout(function(){{window._ghsScrolling=false;}},200);" +
                    $"}})();";
                _ = _webView.InvokeScript(script);
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }
        else
        {
            // No anchor found (content before first mapped block) — proportional fallback
            if (Math.Abs(fraction - _lastProgrammaticScrollFraction) < 0.01) return;

            _isSyncingScroll = true;
            try
            {
                _lastProgrammaticScrollFraction = fraction;
                _lastEditorSyncTime = DateTime.UtcNow;
                var targetFractionStr = fraction.ToString("F6", CultureInfo.InvariantCulture);
                var script =
                    $"window._ghsScrolling=true;" +
                    $"window.scrollTo(0,(document.body.scrollHeight - window.innerHeight)*{targetFractionStr});" +
                    $"setTimeout(function(){{window._ghsScrolling=false;}},200);";
                _ = _webView.InvokeScript(script);
            }
            finally
            {
                _isSyncingScroll = false;
            }
        }

        UpdateScrollSyncState();
    }

    /// <summary>
    /// Returns the 1-based document line number of the first line visible in the editor viewport.
    /// Uses DefaultLineHeight as an estimate — sufficient for anchor lookup purposes.
    /// </summary>
    private int GetFirstVisibleLine()
    {
        if (_editor is null) return 1;
        var textView = _editor.TextArea.TextView;
        var scrollY = textView.ScrollOffset.Y;
        var lineHeight = textView.DefaultLineHeight;
        if (lineHeight <= 0) return 1;
        var estimated = Math.Max(1, (int)(scrollY / lineHeight) + 1);
        return Math.Min(estimated, _editor.Document.LineCount);
    }

    private double ComputeEditorScrollFraction()
    {
        if (_editor is null) return 0;
        var docHeight  = _editor.TextArea.TextView.DocumentHeight;
        var viewHeight = _editor.TextArea.TextView.Bounds.Height;
        var maxScroll  = Math.Max(1, docHeight - viewHeight);
        return Math.Clamp(_editor.TextArea.TextView.ScrollOffset.Y / maxScroll, 0.0, 1.0);
    }

    private void UpdateScrollSyncState()
    {
        if (_mainVm is null || _mainVm.GutterSyncState == GutterSyncState.Saved) return;

        var editorFraction = ComputeEditorScrollFraction();
        var diff = Math.Abs(editorFraction - _lastProgrammaticScrollFraction);
        _mainVm.GutterSyncState = diff < 0.02 ? GutterSyncState.Synced : GutterSyncState.Drifted;
    }

    // ─── Git diff gutter ────────────────────────────────────────────────────────

    private void RefreshGitDiff(string? filePath)
    {
        _gitDiffCts?.Cancel();
        _gitDiffCts = new CancellationTokenSource();
        var token = _gitDiffCts.Token;

        if (string.IsNullOrEmpty(filePath))
        {
            _gitDiffMargin?.UpdateDiff(new Dictionary<int, GitLineState>());
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var diff = await _gitDiffService.GetDiffAsync(filePath);
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    _gitDiffMargin?.UpdateDiff(diff);
                    _gitDiffMargin?.InvalidateMeasure();
                });
            }
            catch { }
        }, token);
    }

    // ─── Word count ───────────────────────────────────────────────────────────

    private void ScheduleWordCountUpdate(int cursorOffset, int line, int col)
    {
        _wordCountCts?.Cancel();
        _wordCountCts = new CancellationTokenSource();
        var token = _wordCountCts.Token;
        var text  = _editorVm?.DocumentText ?? string.Empty;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, token);
                if (token.IsCancellationRequested) return;
                var sectionCount = ComputeWordCount(text, cursorOffset);
                var totalCount   = CountWords(text);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_mainVm is not null)
                    {
                        _mainVm.GutterWordCount = sectionCount;
                        _lastTotalWordCount = totalCount;
                        _mainVm.StatusBarText = $"Ln {line} · Col {col} · {totalCount} words";
                    }
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private static int ComputeWordCount(string text, int cursorOffset)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var headings = FindHeadings(text);
        if (headings.Count == 0)
            return CountWords(text);

        // Find the heading block containing the cursor
        int sectionStart = -1;
        int sectionLevel = 0;

        foreach (var (offset, level) in headings)
        {
            if (offset <= cursorOffset)
            {
                sectionStart = offset;
                sectionLevel = level;
            }
            else break;
        }

        // Cursor is before the first heading — count whole document
        if (sectionStart < 0)
            return CountWords(text);

        // Find the next heading of equal or higher level
        int sectionEnd = text.Length;
        bool pastCursorSection = false;

        foreach (var (offset, level) in headings)
        {
            if (offset <= cursorOffset) { pastCursorSection = true; continue; }
            if (pastCursorSection && level <= sectionLevel)
            {
                sectionEnd = offset;
                break;
            }
        }

        return CountWords(text.Substring(sectionStart, sectionEnd - sectionStart));
    }

    private static List<(int Offset, int Level)> FindHeadings(string text)
    {
        var result = new List<(int, int)>();
        foreach (Match m in HeadingRegex().Matches(text))
            result.Add((m.Index, m.Groups[1].Length));
        return result;
    }

    private static int CountWords(string text) =>
        text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

    [GeneratedRegex(@"^(#{1,6})\s", RegexOptions.Multiline)]
    private static partial Regex HeadingRegex();

    // ─── Line replacement helper ──────────────────────────────────────────────

    private static string ReplaceLines(string fullText, int startLine, int endLine, string replacement)
    {
        var lines = fullText.Split('\n');
        // startLine and endLine are 1-based
        var before           = lines.Take(startLine - 1);
        var after            = lines.Skip(endLine);   // endLine is inclusive, so skip past it
        var replacementLines = replacement.Split('\n');
        return string.Join('\n', before.Concat(replacementLines).Concat(after));
    }

    // ─── Left panel slot ─────────────────────────────────────────────────────

    private void InitializeLeftPanelSlot()
    {
        var slotControl = this.FindControl<ContentControl>("LeftPanelSlot");
        if (slotControl is null || _mainVm is null) return;

        var leftSlot = _mainVm.LeftPanelSlot;
        slotControl.Content = leftSlot.ActiveOccupant;
        leftSlot.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(PanelSlotViewModel.ActiveOccupant))
                slotControl.Content = leftSlot.ActiveOccupant;
        };
    }

    private void InitializeRightPanelSlot()
    {
        var slotControl = this.FindControl<ContentControl>("RightPanelSlot");
        if (slotControl is null || _mainVm is null) return;

        var rightSlot = _mainVm.RightPanelSlot;
        slotControl.Content = rightSlot.ActiveOccupant;
        rightSlot.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(PanelSlotViewModel.ActiveOccupant))
                slotControl.Content = rightSlot.ActiveOccupant;
        };
    }

    // ─── Node click → editor/preview navigation (shared by topology + outline) ─

    private void OnNodeClicked(int sourceLine)
    {
        if (_editor is null) return;

        // Move editor caret and scroll to the heading line
        _editor.TextArea.Caret.Line = sourceLine;
        _editor.ScrollToLine(sourceLine);

        // Scroll preview to that heading (skip in Edit-only mode)
        if (_mainVm?.CurrentViewMode != ViewMode.Edit && _webViewReady && _webView is not null)
        {
            var selector = _sourceMappingService?.GetElementSelector(sourceLine);
            string js;
            if (selector is not null)
            {
                var escaped = selector.Replace("\\", "\\\\").Replace("'", "\\'");
                js = $"(function(){{ var el = document.querySelector('{escaped}'); if(el) el.scrollIntoView({{behavior:'smooth', block:'start'}}); }})();";
            }
            else if (sourceLine == 1)
            {
                js = "window.scrollTo({top: 0, behavior: 'smooth'});";
            }
            else
            {
                // Proportional scroll estimate fallback
                var totalLines = _editor.Document.LineCount;
                var fraction = totalLines > 1 ? (double)(sourceLine - 1) / (totalLines - 1) : 0;
                var fractionStr = fraction.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
                js = $"window.scrollTo({{top: (document.body.scrollHeight - window.innerHeight) * {fractionStr}, behavior: 'smooth'}});";
            }
            _ = _webView.InvokeScript(js);
        }
    }

    // ─── Export suggested filename ────────────────────────────────────────────

    private static string GetSuggestedExportName(FileService? fileService, EditorViewModel? editorVm)
    {
        var currentPath = fileService?.CurrentFilePath;
        if (!string.IsNullOrEmpty(currentPath))
            return Path.GetFileNameWithoutExtension(currentPath);

        var text = editorVm?.DocumentText ?? "";
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# "))
            {
                var heading = trimmed.Substring(2).Trim();
                foreach (var c in Path.GetInvalidFileNameChars())
                    heading = heading.Replace(c.ToString(), "");
                if (!string.IsNullOrEmpty(heading))
                    return heading;
            }
        }

        return "export";
    }

    // ─── Center grid layout ───────────────────────────────────────────────────

    private void InitializeCenterGrid()
    {
        _centerGrid  = this.FindControl<Grid>("CenterGrid");
        _gutterBorder = this.FindControl<Border>("GutterBorder");

        if (_gutterBorder is not null)
        {
            _gutterBorder.PointerPressed  += OnGutterPointerPressed;
            _gutterBorder.PointerMoved    += OnGutterPointerMoved;
            _gutterBorder.PointerReleased += OnGutterPointerReleased;
        }

        var rightDivider = this.FindControl<Border>("RightPanelDivider");
        if (rightDivider is not null)
        {
            rightDivider.PointerPressed  += OnRightPanelDividerPointerPressed;
            rightDivider.PointerMoved    += OnRightPanelDividerPointerMoved;
            rightDivider.PointerReleased += OnRightPanelDividerPointerReleased;
        }

        UpdateCenterLayout();

        // Listen for layout changes (window resize changes available width)
        if (_centerGrid is not null)
            _centerGrid.SizeChanged += (_, _) => UpdateCenterLayout();
    }

    private void UpdateCenterLayout()
    {
        if (_centerGrid is null || _mainVm is null) return;

        var viewMode = _mainVm.CurrentViewMode;

        if (viewMode == ViewMode.Split)
        {
            var ratio        = _mainVm.SplitRatio;
            var totalWidth   = _centerGrid.Bounds.Width;
            var gutterWidth  = 28.0;
            var available    = totalWidth - gutterWidth;

            if (available > 400)
            {
                var minRatio = 200.0 / available;
                var maxRatio = 1.0 - 200.0 / available;
                ratio = Math.Clamp(ratio, minRatio, maxRatio);
            }

            _centerGrid.ColumnDefinitions[0] = new ColumnDefinition(ratio,           GridUnitType.Star);
            _centerGrid.ColumnDefinitions[1] = new ColumnDefinition(gutterWidth,     GridUnitType.Pixel);
            _centerGrid.ColumnDefinitions[2] = new ColumnDefinition(1.0 - ratio,     GridUnitType.Star);
            if (_webView is not null) _webView.IsVisible = true;
        }
        else if (viewMode == ViewMode.Edit)
        {
            _centerGrid.ColumnDefinitions[0] = new ColumnDefinition(1, GridUnitType.Star);
            _centerGrid.ColumnDefinitions[1] = new ColumnDefinition(0, GridUnitType.Pixel);
            _centerGrid.ColumnDefinitions[2] = new ColumnDefinition(0, GridUnitType.Pixel);
            // NativeWebView is an OS-level window — must be hidden AND have 0px column to fully remove
            if (_webView is not null) _webView.IsVisible = false;
        }
        else // Preview
        {
            _centerGrid.ColumnDefinitions[0] = new ColumnDefinition(0, GridUnitType.Pixel);
            _centerGrid.ColumnDefinitions[1] = new ColumnDefinition(0, GridUnitType.Pixel);
            _centerGrid.ColumnDefinitions[2] = new ColumnDefinition(1, GridUnitType.Star);
            if (_webView is not null) _webView.IsVisible = true;
        }
    }

    /// <summary>
    /// BL-32: force an Edit-mode column layout while the Command Palette is
    /// open. This removes the NativeWebView from screen space so the palette
    /// card isn't clipped by the native HWND. The current view mode isn't
    /// modified — when the palette closes, UpdateCenterLayout() re-reads
    /// <see cref="MainWindowViewModel.CurrentViewMode"/> and restores the
    /// original split/preview/edit layout.
    /// </summary>
    private void CollapsePreviewForPalette()
    {
        if (_centerGrid is null) return;

        _centerGrid.ColumnDefinitions[0] = new ColumnDefinition(1, GridUnitType.Star);
        _centerGrid.ColumnDefinitions[1] = new ColumnDefinition(0, GridUnitType.Pixel);
        _centerGrid.ColumnDefinitions[2] = new ColumnDefinition(0, GridUnitType.Pixel);
        if (_webView is not null) _webView.IsVisible = false;
    }

    // ─── Gutter drag handlers ─────────────────────────────────────────────────

    private void OnGutterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_centerGrid is null || _mainVm is null) return;
        _isDraggingGutter = true;
        _dragStartX       = e.GetPosition(_centerGrid).X;
        _dragStartRatio   = _mainVm.SplitRatio;
        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnGutterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingGutter || _centerGrid is null || _mainVm is null) return;

        var gutterWidth = 28.0;
        var available   = _centerGrid.Bounds.Width - gutterWidth;
        if (available <= 0) return;

        var delta    = e.GetPosition(_centerGrid).X - _dragStartX;
        var newRatio = _dragStartRatio + delta / available;

        var minRatio = Math.Max(0.05, 200.0 / available);
        var maxRatio = Math.Min(0.95, 1.0 - 200.0 / available);
        newRatio = Math.Clamp(newRatio, minRatio, maxRatio);

        _mainVm.SplitRatio = newRatio;
        UpdateCenterLayout();
        e.Handled = true;
    }

    private void OnGutterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingGutter) return;
        _isDraggingGutter = false;
        e.Pointer.Capture(null);
        _mainVm?.PersistSplitRatio();
        e.Handled = true;
    }

    // ─── Right panel drag handlers ───────────────────────────────────────────

    private void OnRightPanelDividerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mainVm is null || !_mainVm.IsRightPanelOpen) return;

        _isDraggingRightPanel     = true;
        _rightPanelDragStartX     = e.GetPosition(this).X;
        _rightPanelDragStartWidth = _mainVm.RightPanelWidth;

        // Suppress the width transition so drag is responsive
        var border = this.FindControl<Border>("RightPanelBorder");
        if (border is not null)
        {
            _rightPanelTransitions = border.Transitions;
            border.Transitions = null;
        }

        e.Pointer.Capture(sender as IInputElement);
        e.Handled = true;
    }

    private void OnRightPanelDividerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingRightPanel || _mainVm is null) return;

        // Panel is on the right; dragging the left edge leftward makes it wider.
        var delta    = e.GetPosition(this).X - _rightPanelDragStartX;
        var newWidth = _rightPanelDragStartWidth - delta;

        // Clamp: minimum 150px, maximum half the window width
        var maxWidth = Math.Max(150, Bounds.Width / 2);
        newWidth = Math.Clamp(newWidth, 150, maxWidth);

        _mainVm.RightPanelWidth = newWidth;
        e.Handled = true;
    }

    private void OnRightPanelDividerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingRightPanel) return;
        _isDraggingRightPanel = false;

        // Restore transition
        var border = this.FindControl<Border>("RightPanelBorder");
        if (border is not null)
            border.Transitions = _rightPanelTransitions;

        e.Pointer.Capture(null);
        _mainVm?.PersistRightPanelWidth();
        e.Handled = true;
    }

    // ─── Draft restore dialog ────────────────────────────────────────────────

    private async Task<bool> ShowDraftRestoreDialog(string formattedDate)
    {
        var dlg = new Window
        {
            Title = "Restore Draft",
            Width = 380, Height = 160,
            CanResize = false,
            WindowDecorations = Avalonia.Controls.WindowDecorations.None,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x18, 0x18, 0x18)),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        bool result = false;
        var restoreBtn = new Button
        {
            Content = "Restore",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x4A, 0x9E, 0xFF)),
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Margin = new Avalonia.Thickness(0, 0, 8, 0)
        };
        var discardBtn = new Button
        {
            Content = "Discard",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88)),
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        restoreBtn.Click += (_, _) => { result = true; dlg.Close(); };
        discardBtn.Click += (_, _) => dlg.Close();
        dlg.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = $"A draft from {formattedDate} is newer than the saved file.\n\nRestore draft?",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
                    FontSize = 13,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Avalonia.Thickness(0, 0, 0, 16)
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { discardBtn, restoreBtn }
                }
            }
        };
        await dlg.ShowDialog<object?>(this);
        return result;
    }

    // ─── WebMessage record ────────────────────────────────────────────────────

    private record WebMessage(
        [property: JsonPropertyName("type")]              string?  Type,
        [property: JsonPropertyName("commandId")]         string?  CommandId,
        [property: JsonPropertyName("sourceLine")]        int      SourceLine,
        [property: JsonPropertyName("sourceEndLine")]     int      SourceEndLine,
        [property: JsonPropertyName("tag")]               string?  Tag,
        [property: JsonPropertyName("y")]                 double   ScrollY,
        [property: JsonPropertyName("h")]                 double   ScrollH,
        [property: JsonPropertyName("selectedText")]      string?  SelectedText,
        [property: JsonPropertyName("previewSourceLine")] int?     PreviewSourceLine,
        [property: JsonPropertyName("anchorLine")]        int?     AnchorLine
    );
}
