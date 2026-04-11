using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class AiAssistViewModel : ViewModelBase
{
    private readonly AiAssistService  _aiService;
    private readonly EditorViewModel  _editorVm;
    private readonly SettingsService  _settingsService;

    public string PanelName => "AI Assist";

    [ObservableProperty] private string  _prompt          = "";
    [ObservableProperty] private string  _response        = "";
    [ObservableProperty] private bool    _isStreaming      = false;
    [ObservableProperty] private bool    _includeDocument  = true;
    [ObservableProperty] private string  _statusMessage    = "";
    [ObservableProperty] private bool    _isConfigured     = false;

    private CancellationTokenSource? _cts;

    public AiAssistViewModel(
        AiAssistService aiService,
        EditorViewModel editorVm,
        SettingsService settingsService)
    {
        _aiService       = aiService;
        _editorVm        = editorVm;
        _settingsService = settingsService;
        IsConfigured     = _aiService.IsConfigured;
    }

    /// <summary>Called when settings change (API key updated).</summary>
    public void RefreshConfiguration()
    {
        IsConfigured = _aiService.IsConfigured;
        StatusMessage = IsConfigured
            ? "API key configured."
            : "API key not configured. Add it in Settings.";
    }

    public Task<bool> TestConnectionAsync() =>
        _aiService.TestConnectionAsync();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(Prompt)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        IsStreaming   = true;
        Response      = "";
        StatusMessage = "Thinking...";

        var context = IncludeDocument ? _editorVm.DocumentText : null;

        try
        {
            await foreach (var chunk in _aiService.SendAsync(Prompt, context, _cts.Token))
            {
                Dispatcher.UIThread.Post(() => Response += chunk);
            }
            StatusMessage = "Done.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (Exception ex)
        {
            Response      += $"\n\n[Error: {ex.Message}]";
            StatusMessage  = "Error.";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    private bool CanSend() => !IsStreaming && IsConfigured;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ClearResponse()
    {
        Response      = "";
        StatusMessage = "";
    }

    [RelayCommand]
    private async Task CopyResponse()
    {
        if (string.IsNullOrEmpty(Response)) return;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is not null)
        {
            var clipboard = TopLevel.GetTopLevel(desktop.MainWindow)?.Clipboard;
            if (clipboard is not null)
                await Avalonia.Input.Platform.ClipboardExtensions.SetValueAsync(
                    clipboard, Avalonia.Input.DataFormat.Text, Response);
        }
    }

    [RelayCommand]
    private void InsertResponse()
    {
        if (string.IsNullOrEmpty(Response)) return;
        var current = _editorVm.DocumentText ?? "";
        _editorVm.DocumentText = current + "\n\n" + Response;
    }

    [RelayCommand]
    private void UseSelection(string? selectedText)
    {
        if (!string.IsNullOrEmpty(selectedText))
            Prompt = selectedText;
    }
}
