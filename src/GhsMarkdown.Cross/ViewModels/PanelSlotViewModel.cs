using CommunityToolkit.Mvvm.ComponentModel;

namespace GhsMarkdown.Cross.ViewModels;

/// <summary>
/// Manages the currently displayed occupant in a panel slot.
/// The left panel ContentControl binds to ActiveOccupant; Avalonia resolves
/// the correct View via DataTemplate selectors registered in App.axaml.
/// </summary>
public partial class PanelSlotViewModel : ObservableObject
{
    [ObservableProperty]
    private ViewModelBase? _activeOccupant;

    public void SwapOccupant(ViewModelBase occupant)
    {
        ActiveOccupant = occupant;
    }
}
