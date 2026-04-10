using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class SnippetStudioView : UserControl
{
    public SnippetStudioView()
    {
        InitializeComponent();

        // Wire click and double-click on snippet rows
        AddHandler(Button.ClickEvent, OnSnippetRowClick, RoutingStrategies.Bubble);
        AddHandler(DoubleTappedEvent, OnSnippetRowDoubleTapped, RoutingStrategies.Bubble);
    }

    private void OnSnippetRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SnippetStudioViewModel vm) return;
        if (e.Source is not Control source) return;

        var current = source as Control;
        while (current is not null)
        {
            if (current.DataContext is SnippetViewModel snippetVm)
            {
                vm.SelectedSnippet = snippetVm;

                // Update visual selection on all rows
                UpdateSelectionVisuals(vm);
                break;
            }
            current = current.Parent as Control;
        }
    }

    private void OnSnippetRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SnippetStudioViewModel vm) return;
        if (e.Source is not Control source) return;

        var current = source as Control;
        while (current is not null)
        {
            if (current.DataContext is SnippetViewModel snippetVm)
            {
                vm.SelectedSnippet = snippetVm;
                vm.InsertSnippetCommand.Execute(null);
                break;
            }
            current = current.Parent as Control;
        }
    }

    private void UpdateSelectionVisuals(SnippetStudioViewModel vm)
    {
        // Walk all snippet row buttons and toggle .selected class
        UpdateSelectionInVisualTree(this, vm);
    }

    private static void UpdateSelectionInVisualTree(Control parent, SnippetStudioViewModel vm)
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Button btn && btn.Classes.Contains("snippet-row"))
            {
                if (btn.DataContext is SnippetViewModel svm && svm == vm.SelectedSnippet)
                    btn.Classes.Add("selected");
                else
                    btn.Classes.Remove("selected");
            }
            if (child is Control ctrl)
                UpdateSelectionInVisualTree(ctrl, vm);
        }
    }
}
