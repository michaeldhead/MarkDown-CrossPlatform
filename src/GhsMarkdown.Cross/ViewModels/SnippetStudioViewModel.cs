using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GhsMarkdown.Cross.Models;
using GhsMarkdown.Cross.Services;

namespace GhsMarkdown.Cross.ViewModels;

public partial class SnippetCategoryViewModel : ObservableObject
{
    public string CategoryName { get; }
    public ObservableCollection<SnippetViewModel> Snippets { get; }

    public SnippetCategoryViewModel(string categoryName, IEnumerable<SnippetViewModel> snippets)
    {
        CategoryName = categoryName;
        Snippets = new ObservableCollection<SnippetViewModel>(snippets);
    }
}

public partial class SnippetViewModel : ObservableObject
{
    public Snippet Model { get; }
    public string Title => Model.Title;
    public string Body => Model.Body;

    public SnippetViewModel(Snippet model)
    {
        Model = model;
    }
}

public partial class SnippetStudioViewModel : ViewModelBase
{
    private readonly SnippetService _snippetService;
    private readonly CommandRegistry _commandRegistry;

    public ObservableCollection<SnippetCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private SnippetViewModel? _selectedSnippet;

    // Delegate wired from code-behind to insert snippet into editor
    public Action<Snippet>? InsertSnippetAction { get; set; }

    // Delegate wired from code-behind to show edit dialog (async to avoid UI deadlock)
    public Func<Snippet?, Task<Snippet?>>? ShowEditDialogFunc { get; set; }

    // Delegate wired from code-behind to show confirm dialog (async to avoid UI deadlock)
    public Func<string, Task<bool>>? ShowConfirmFunc { get; set; }

    public SnippetStudioViewModel(SnippetService snippetService, CommandRegistry commandRegistry)
    {
        _snippetService = snippetService;
        _commandRegistry = commandRegistry;

        _snippetService.SnippetsChanged += (_, _) =>
        {
            RebuildCategories();
            RegisterSnippetCommands();
        };

        RebuildCategories();
        RegisterSnippetCommands();
    }

    [RelayCommand]
    private void InsertSnippet()
    {
        if (SelectedSnippet is null || InsertSnippetAction is null) return;
        InsertSnippetAction(SelectedSnippet.Model);
    }

    [RelayCommand]
    private async Task AddSnippet()
    {
        if (ShowEditDialogFunc is null) return;
        var result = await ShowEditDialogFunc(null);
        if (result is not null)
            _snippetService.Add(result);
    }

    [RelayCommand]
    private async Task EditSnippet()
    {
        if (SelectedSnippet is null || ShowEditDialogFunc is null) return;
        var result = await ShowEditDialogFunc(SelectedSnippet.Model);
        if (result is not null)
            _snippetService.Update(result);
    }

    [RelayCommand]
    private async Task DeleteSnippet()
    {
        if (SelectedSnippet is null || ShowConfirmFunc is null) return;
        var confirmed = await ShowConfirmFunc($"Delete snippet \"{SelectedSnippet.Title}\"?");
        if (confirmed)
            _snippetService.Delete(SelectedSnippet.Model.Id);
    }

    private void RebuildCategories()
    {
        var selectedId = SelectedSnippet?.Model.Id;
        Categories.Clear();
        SnippetViewModel? restoredSelection = null;

        var groups = _snippetService.Snippets
            .GroupBy(s => s.Category)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var snippetVms = group
                .OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
                .Select(s => new SnippetViewModel(s))
                .ToList();

            Categories.Add(new SnippetCategoryViewModel(group.Key, snippetVms));

            if (selectedId is not null)
                restoredSelection ??= snippetVms.FirstOrDefault(vm => vm.Model.Id == selectedId);
        }

        SelectedSnippet = restoredSelection;
    }

    private void RegisterSnippetCommands()
    {
        _commandRegistry.Register(new CommandDescriptor(
            "snippets.open", "Open Snippet Studio", "Navigation",
            () => { /* wired externally */ }));

        foreach (var snippet in _snippetService.Snippets)
        {
            var s = snippet; // capture
            var idSafe = s.Title.Replace(" ", "").Replace("(", "").Replace(")", "");
            _commandRegistry.Register(new CommandDescriptor(
                $"snippet.insert.{idSafe.ToLowerInvariant()}",
                $"Insert Snippet: {s.Title}",
                "Actions",
                () => InsertSnippetAction?.Invoke(s)));
        }
    }
}
