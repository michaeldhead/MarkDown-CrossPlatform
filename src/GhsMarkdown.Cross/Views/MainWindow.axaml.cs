using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.Services;
using GhsMarkdown.Cross.ViewModels;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace GhsMarkdown.Cross.Views;

public partial class MainWindow : Window
{
    // ─── Controls ─────────────────────────────────────────────────────────────
    private TextEditor?                          _editor;
    private Avalonia.Controls.NativeWebView?     _webView;
    private Grid?                                _centerGrid;
    private Border?                              _gutterBorder;

    // ─── ViewModels / Services ────────────────────────────────────────────────
    private PreviewViewModel?           _previewVm;
    private EditorViewModel?            _editorVm;
    private MainWindowViewModel?        _mainVm;
    private SourceMappingService?       _sourceMappingService;
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

    // ─── Scroll sync ──────────────────────────────────────────────────────────
    private bool   _isSyncingScroll;
    private double _lastProgrammaticScrollFraction = 0;
    private DateTime _lastEditorSyncTime = DateTime.MinValue;
    private const int ScrollSyncCooldownMs = 300;

    // ─── Word count ───────────────────────────────────────────────────────────
    private CancellationTokenSource? _wordCountCts;

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
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        var mdFile = files
            .Select(f => f.Path.LocalPath)
            .FirstOrDefault(p => p.EndsWith(".md", StringComparison.OrdinalIgnoreCase));

        if (mdFile is not null)
        {
            var fileService = App.Services.GetService(typeof(FileService)) as FileService;
            if (fileService is not null)
                await fileService.OpenFile(mdFile);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        _mainVm = vm;

        _editorVm             = App.Services.GetService(typeof(EditorViewModel))              as EditorViewModel;
        _previewVm            = App.Services.GetService(typeof(PreviewViewModel))             as PreviewViewModel;
        _sourceMappingService = App.Services.GetService(typeof(SourceMappingService))         as SourceMappingService;
        _topologyVm           = App.Services.GetService(typeof(TopologyViewModel))            as TopologyViewModel;
        _outlineVm            = App.Services.GetService(typeof(OutlineViewModel))             as OutlineViewModel;
        _snippetModeController   = App.Services.GetService(typeof(SnippetModeController))    as SnippetModeController;
        _snippetInsertionService = App.Services.GetService(typeof(SnippetInsertionService))  as SnippetInsertionService;

        InitializeEditor();
        InitializeWebView();
        InitializeCenterGrid();

        // Apply editor theme colors and subscribe to theme changes
        var themeService = App.Services.GetService(typeof(ThemeService)) as ThemeService;
        if (themeService is not null && _editor is not null)
        {
            ApplyEditorTheme(themeService.CurrentThemeName);
            themeService.ThemeChanged += (_, _) => ApplyEditorTheme(themeService.CurrentThemeName);
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

        // Exit snippet mode on file change (TextAnchors become invalid on document replace)
        var fileService = App.Services.GetService(typeof(FileService)) as FileService;
        if (fileService is not null)
        {
            fileService.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(FileService.CurrentFilePath))
                    _snippetModeController?.Exit();
            };

            // Draft restore prompt
            var editorVmRef = _editorVm;
            fileService.DraftFound += async (_, args) =>
            {
                var formatted = args.DraftTimestamp.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
                var restore = await ShowDraftRestoreDialog(formatted);
                if (restore)
                {
                    var draftContent = await File.ReadAllTextAsync(args.DraftPath);
                    await fileService.OpenFileSkipDraftCheck(args.FilePath);
                    if (editorVmRef is not null)
                        editorVmRef.DocumentText = draftContent;
                }
                else
                {
                    fileService.DeleteDraft(args.FilePath);
                    await fileService.OpenFileSkipDraftCheck(args.FilePath);
                }
            };
        }

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
        {
            _previewVm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(PreviewViewModel.PreviewHtml))
                    NavigateWebView(_previewVm.PreviewHtml);
            };
        }

        // Observe view mode to update center layout
        _mainVm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(MainWindowViewModel.CurrentViewMode))
                UpdateCenterLayout();
        };
    }

    // ─── AvaloniaEdit ─────────────────────────────────────────────────────────

    private void InitializeEditor()
    {
        _editor = this.FindControl<TextEditor>("MarkdownEditor");
        if (_editor is null) return;

        var uri    = new Uri("avares://GhsMarkdown.Cross/Assets/MarkdownSyntax.xshd");
        var stream = AssetLoader.Open(uri);
        using var reader = XmlReader.Create(stream);
        var xshd = HighlightingLoader.LoadXshd(reader);
        _editor.SyntaxHighlighting = HighlightingLoader.Load(xshd, HighlightingManager.Instance);

        _editor.Options.ShowTabs             = false;
        _editor.Options.IndentationSize      = 2;
        _editor.Options.ConvertTabsToSpaces  = true;
        _editor.Options.HighlightCurrentLine = true;

        // Push text changes to EditorViewModel
        _editor.Document.TextChanged += (_, _) =>
        {
            if (_editorVm is not null && _editor.Document.Text != _editorVm.DocumentText)
                _editorVm.DocumentText = _editor.Document.Text;
        };

        // Pull initial text from ViewModel
        if (_editorVm is not null && !string.IsNullOrEmpty(_editorVm.DocumentText))
            _editor.Document.Text = _editorVm.DocumentText;

        // Keep editor in sync when ViewModel text changes externally (Open, New)
        if (_editorVm is not null)
        {
            _editorVm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(EditorViewModel.DocumentText)
                    && _editor.Document.Text != _editorVm.DocumentText)
                {
                    _editor.Document.Text = _editorVm.DocumentText;
                }
            };
        }

        RegisterEditorCommands();

        // Snippet mode: intercept Tab/Escape/Enter before AvaloniaEdit handles them
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Scroll sync — subscribe to editor scroll events
        _editor.TextArea.TextView.ScrollOffsetChanged += OnEditorScrollChanged;

        // Caret changes: word count + active block highlight + topology tracking
        _editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            var offset = _editor.TextArea.Caret.Offset;
            var line   = _editor.TextArea.Caret.Line;
            ScheduleWordCountUpdate(offset);
            ScheduleHighlightUpdate(line);

            // Feed caret line to EditorViewModel for TopologyViewModel subscription
            if (_editorVm is not null)
                _editorVm.CaretLine = line;
        };
    }

    // ─── Editor theme colors ─────────────────────────────────────────────────

    private void ApplyEditorTheme(string themeName)
    {
        if (_editor is null) return;

        if (themeName == "GHS Ink")
        {
            _editor.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A1A1A"));
            _editor.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F9F6F0"));
        }
        else
        {
            _editor.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E8E8E8"));
            _editor.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#141414"));
        }

        _editor.TextArea.Foreground = _editor.Foreground;
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
    }

    // ─── NativeWebView ────────────────────────────────────────────────────────

    private void InitializeWebView()
    {
        _webView = this.FindControl<Avalonia.Controls.NativeWebView>("PreviewWebView");
        if (_webView is null) return;

        _webView.WebMessageReceived += OnWebViewMessage;

        _webView.NavigationCompleted += OnNavigationCompleted;

        _webView.AdapterCreated += (_, _) =>
        {
            _webViewReady = true;
            if (_previewVm is not null)
                NavigateWebView(_previewVm.PreviewHtml);
        };
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

        // Re-apply highlight for current caret line
        if (_editor is not null)
            ScheduleHighlightUpdate(_editor.TextArea.Caret.Line);
    }

    // ─── JS injection (consolidated, all three listeners) ────────────────────

    private void InjectAllListeners()
    {
        if (!_webViewReady || _webView is null) return;

        const string js = """
            (function() {
              // 1. Scroll reporting
              window._ghsScrolling = false;
              window.addEventListener('scroll', function() {
                if (window._ghsScrolling) return;
                try {
                  window.chrome.webview.postMessage(JSON.stringify({
                    type: 'scroll',
                    y: window.scrollY,
                    h: document.body.scrollHeight - window.innerHeight
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
                    _savedScrollY = msg.ScrollY;
                    HandleScrollSync(msg.ScrollY, msg.ScrollH);
                    break;
                case "click-sync":
                    HandleClickSync(msg.SourceLine);
                    break;
                case "inline-edit":
                    HandleInlineEdit(msg.SourceLine, msg.SourceEndLine, msg.Tag);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebMsg] Parse error: {ex.Message}");
        }
    }

    // ─── Scroll sync handler ──────────────────────────────────────────────────

    private void HandleScrollSync(double scrollY, double scrollH)
    {
        if (_mainVm?.CurrentViewMode != ViewMode.Split) return;
        if (_isSyncingScroll || _editor is null) return;
        if (_previewVm?.IsTimelinePreviewActive == true) return;

        // Cooldown: skip if we recently synced from editor to prevent echo
        if ((DateTime.UtcNow - _lastEditorSyncTime).TotalMilliseconds < ScrollSyncCooldownMs)
            return;

        if (scrollH < 1) return;

        var previewFraction = Math.Clamp(scrollY / scrollH, 0.0, 1.0);

        // Only act if preview moved away from where we last set it (user manually scrolled)
        if (Math.Abs(previewFraction - _lastProgrammaticScrollFraction) < 0.02) return;

        _isSyncingScroll = true;
        try
        {
            var docHeight  = _editor.TextArea.TextView.DocumentHeight;
            var viewHeight = _editor.Bounds.Height;
            var maxEditorScroll = Math.Max(1, docHeight - viewHeight);
            _editor.ScrollToVerticalOffset(previewFraction * maxEditorScroll);
            _lastProgrammaticScrollFraction = previewFraction;
        }
        finally
        {
            _isSyncingScroll = false;
        }

        UpdateScrollSyncState();
    }

    // ─── Click-to-sync handler ────────────────────────────────────────────────

    private void HandleClickSync(int sourceLine)
    {
        var mode = _mainVm?.CurrentViewMode;
        if (mode == ViewMode.Edit) return; // Only in Split and Preview

        if (_editor is null || _sourceMappingService is null) return;

        // Confirm mapping exists
        var selector = _sourceMappingService.GetElementSelector(sourceLine);
        if (selector is null) return;

        _editor.TextArea.Caret.Line = sourceLine;
        _editor.ScrollToLine(sourceLine);
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
        if (!_webViewReady || _webView is null) return;
        if (_mainVm?.CurrentViewMode == ViewMode.Edit) return; // Highlight only in Split/Preview
        if (_previewVm?.IsTimelinePreviewActive == true) return; // Suppress during timeline
        if (_sourceMappingService is null) return;

        // Find selector for current line; walk backwards if no block starts here
        string? selector = null;
        for (int line = caretLine; line >= 1; line--)
        {
            selector = _sourceMappingService.GetElementSelector(line);
            if (selector is not null) break;
        }

        if (selector == _lastActiveSelector) return; // Nothing changed
        _lastActiveSelector = selector;

        string js;
        if (selector is not null)
        {
            // Escape the selector for embedding in a JS single-quoted string
            var escaped = selector.Replace("\\", "\\\\").Replace("'", "\\'");
            js = "(function() {" +
                 "  var prev = document.querySelector('.ghs-active');" +
                 "  if (prev) prev.classList.remove('ghs-active');" +
                 $"  var el = document.querySelector('{escaped}');" +
                 "  if (el) el.classList.add('ghs-active');" +
                 "})();";
        }
        else
        {
            js = "(function() {" +
                 "  var prev = document.querySelector('.ghs-active');" +
                 "  if (prev) prev.classList.remove('ghs-active');" +
                 "})();";
        }

        _ = _webView.InvokeScript(js);
    }

    // ─── Editor → Preview scroll ──────────────────────────────────────────────

    private void OnEditorScrollChanged(object? sender, EventArgs e)
    {
        if (_isSyncingScroll || _mainVm?.CurrentViewMode != ViewMode.Split) return;
        if (!_webViewReady || _webView is null || _editor is null) return;
        if (_previewVm?.IsTimelinePreviewActive == true) return; // Suppress during timeline

        _isSyncingScroll = true;
        try
        {
            var fraction = ComputeEditorScrollFraction();
            _lastProgrammaticScrollFraction = fraction;
            _lastEditorSyncTime = DateTime.UtcNow;

            var targetFractionStr = fraction.ToString("F6", CultureInfo.InvariantCulture);
            var script = $"window._ghsScrolling=true; window.scrollTo(0, (document.body.scrollHeight - window.innerHeight) * {targetFractionStr}); setTimeout(function(){{window._ghsScrolling=false;}}, 200);";
            _ = _webView.InvokeScript(script);
        }
        finally
        {
            _isSyncingScroll = false;
        }

        UpdateScrollSyncState();
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

    // ─── Word count ───────────────────────────────────────────────────────────

    private void ScheduleWordCountUpdate(int cursorOffset)
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
                var count = ComputeWordCount(text, cursorOffset);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_mainVm is not null) _mainVm.GutterWordCount = count;
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
        [property: JsonPropertyName("type")]          string?  Type,
        [property: JsonPropertyName("sourceLine")]    int      SourceLine,
        [property: JsonPropertyName("sourceEndLine")] int      SourceEndLine,
        [property: JsonPropertyName("tag")]           string?  Tag,
        [property: JsonPropertyName("y")]             double   ScrollY,
        [property: JsonPropertyName("h")]             double   ScrollH
    );
}
