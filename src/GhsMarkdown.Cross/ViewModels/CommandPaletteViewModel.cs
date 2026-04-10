using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly CommandRegistry _registry;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public ObservableCollection<CommandDescriptor> Results { get; } = new();

    private string _searchText = string.Empty;
    private CancellationTokenSource? _searchCts;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ScheduleSearch(value);
        }
    }

    public CommandDescriptor? SelectedCommand =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public CommandPaletteViewModel(CommandRegistry registry)
    {
        _registry = registry;
    }

    public void Open()
    {
        SearchText = string.Empty;
        LoadResults(string.Empty);
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
        _searchCts?.Cancel();
        SearchText = string.Empty;
        Results.Clear();
        SelectedIndex = -1;
    }

    public void ExecuteSelected()
    {
        var cmd = SelectedCommand;
        if (cmd is null) return;
        var id = cmd.Id;
        Close();
        _registry.Execute(id);
    }

    public void MoveUp()
    {
        if (SelectedIndex > 0)
            SelectedIndex--;
    }

    public void MoveDown()
    {
        if (SelectedIndex < Results.Count - 1)
            SelectedIndex++;
    }

    private void ScheduleSearch(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80, token);
                if (token.IsCancellationRequested) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                        LoadResults(query);
                });
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void LoadResults(string query)
    {
        Results.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            // Show recent, then fill with all commands
            var recent = _registry.GetRecent(10).ToList();
            var recentIds = new HashSet<string>(recent.Select(r => r.Id));
            foreach (var cmd in recent)
                Results.Add(cmd);

            if (Results.Count < 10)
            {
                foreach (var cmd in _registry.Search(""))
                {
                    if (!recentIds.Contains(cmd.Id))
                    {
                        Results.Add(cmd);
                        if (Results.Count >= 10) break;
                    }
                }
            }
        }
        else
        {
            foreach (var cmd in _registry.Search(query))
                Results.Add(cmd);
        }

        SelectedIndex = Results.Count > 0 ? 0 : -1;
        OnPropertyChanged(nameof(SelectedCommand));
    }
}
