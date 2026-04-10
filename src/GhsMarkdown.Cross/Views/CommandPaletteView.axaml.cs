using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GhsMarkdown.Cross.Services;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        // Focus TextBox when palette opens
        vm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && vm.IsOpen)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var input = this.FindControl<TextBox>("SearchInput");
                    input?.Focus();
                }, DispatcherPriority.Loaded);
            }
            if (pe.PropertyName == nameof(CommandPaletteViewModel.SelectedIndex))
                UpdateSelectedVisuals();
            if (pe.PropertyName == nameof(CommandPaletteViewModel.Results) ||
                pe.PropertyName == nameof(CommandPaletteViewModel.SelectedIndex))
                UpdateEmptyState();
        };

        // Observe Results collection changes for empty state + selection
        vm.Results.CollectionChanged += (_, _) =>
        {
            UpdateEmptyState();
            UpdateSelectedVisuals();
        };

        // Backdrop click → close
        var backdrop = this.FindControl<Avalonia.Controls.Shapes.Rectangle>("Backdrop");
        if (backdrop is not null)
        {
            backdrop.PointerPressed += (_, args) =>
            {
                vm.Close();
                args.Handled = true;
            };
        }

        // Card keyboard handling via tunnel
        var card = this.FindControl<Border>("PaletteCard");
        if (card is not null)
        {
            card.AddHandler(KeyDownEvent, OnCardKeyDown, RoutingStrategies.Tunnel);
        }

        // Row click handling on the results list
        var resultsList = this.FindControl<ItemsControl>("ResultsList");
        if (resultsList is not null)
        {
            resultsList.AddHandler(Button.ClickEvent, OnResultRowClick, RoutingStrategies.Bubble);
        }
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        switch (e.Key)
        {
            case Key.Escape:
                vm.Close();
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveUp();
                e.Handled = true;
                break;
            case Key.Down:
                vm.MoveDown();
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnResultRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;
        if (e.Source is not Control source) return;

        // Walk up to find the Button with a CommandDescriptor DataContext
        var current = source;
        while (current is not null)
        {
            if (current.DataContext is CommandDescriptor cmd)
            {
                var idx = vm.Results.IndexOf(cmd);
                if (idx >= 0)
                {
                    vm.SelectedIndex = idx;
                    vm.ExecuteSelected();
                }
                break;
            }
            current = current.Parent as Control;
        }
    }

    private void UpdateEmptyState()
    {
        var empty = this.FindControl<TextBlock>("EmptyState");
        if (empty is null || DataContext is not CommandPaletteViewModel vm) return;
        empty.IsVisible = vm.Results.Count == 0 && !string.IsNullOrEmpty(vm.SearchText);
    }

    private void UpdateSelectedVisuals()
    {
        var resultsList = this.FindControl<ItemsControl>("ResultsList");
        if (resultsList is null || DataContext is not CommandPaletteViewModel vm) return;

        for (int i = 0; i < vm.Results.Count; i++)
        {
            var container = resultsList.ContainerFromIndex(i);
            if (container is null) continue;

            // Find the Button inside the container
            var button = FindDescendant<Button>(container);
            if (button is null) continue;

            if (i == vm.SelectedIndex)
                button.Classes.Add("selected");
            else
                button.Classes.Remove("selected");
        }

        // Auto-scroll selected into view
        if (vm.SelectedIndex >= 0)
        {
            var selectedContainer = resultsList.ContainerFromIndex(vm.SelectedIndex);
            if (selectedContainer is Control ctrl)
                ctrl.BringIntoView();
        }
    }

    private static T? FindDescendant<T>(Control parent) where T : Control
    {
        if (parent is T result) return result;

        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control ctrl)
            {
                var found = FindDescendant<T>(ctrl);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
