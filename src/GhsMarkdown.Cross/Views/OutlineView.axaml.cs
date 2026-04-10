using Avalonia.Controls;
using GhsMarkdown.Cross.ViewModels;

namespace GhsMarkdown.Cross.Views;

public partial class OutlineView : UserControl
{
    public OutlineView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is OutlineViewModel vm)
        {
            vm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(OutlineViewModel.ActiveNode))
                    ScrollActiveNodeIntoView();
            };
        }
    }

    private void ScrollActiveNodeIntoView()
    {
        if (DataContext is not OutlineViewModel vm || vm.ActiveNode is null) return;

        var itemsControl = this.FindControl<ItemsControl>("OutlineItemsControl");
        if (itemsControl is null) return;

        var index = vm.Nodes.IndexOf(vm.ActiveNode);
        if (index < 0) return;

        var container = itemsControl.ContainerFromIndex(index);
        if (container is Control control)
        {
            control.BringIntoView();
        }
    }
}
