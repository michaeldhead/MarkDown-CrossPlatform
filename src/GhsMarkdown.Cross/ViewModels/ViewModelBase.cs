using CommunityToolkit.Mvvm.ComponentModel;

namespace GhsMarkdown.Cross.ViewModels;

/// <summary>
/// Base class for all panel occupant ViewModels. Extends ObservableObject
/// and serves as the type constraint for PanelSlotViewModel.ActiveOccupant.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
