using Dzl.Tray.ViewModels;
using GongSolutions.Wpf.DragDrop;

namespace Dzl.Tray;

/// <summary>
/// gong-wpf-dragdrop handler for the Mods DataGrid. The grid's ItemsSource is the filtered
/// <c>ModsView</c> (an <see cref="System.ComponentModel.ICollectionView"/>), so gong's default
/// move-within-the-source-collection behaviour can't be relied on. Instead we resolve the
/// dragged + target rows back to their indices in the underlying <c>Mods</c> collection and
/// ask the VM to <see cref="MainViewModel.MoveMod"/> there (which renumbers "#" and persists).
///
/// Reorder always operates against the full underlying list: dropping next to a visible row
/// places the dragged mod immediately after that row in the real load order, even when a
/// filter hides some rows. This keeps the persisted order unambiguous while a filter is active.
/// </summary>
public sealed class ModsDropHandler : IDropTarget
{
    private readonly MainViewModel _vm;

    public ModsDropHandler(MainViewModel vm) => _vm = vm;

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is ModRowVm && dropInfo.TargetItem is ModRowVm)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = System.Windows.DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not ModRowVm dragged) return;

        var mods = _vm.Mods;
        var from = mods.IndexOf(dragged);
        if (from < 0) return;

        // dropInfo.InsertIndex is into the (possibly filtered) view. Resolve it to a target
        // row in the underlying collection: the row currently before the insertion point.
        int to;
        if (dropInfo.TargetItem is ModRowVm target)
        {
            to = mods.IndexOf(target);
            if (to < 0) return;
        }
        else
        {
            // Dropped past the last visible row → move to the end of the underlying list.
            to = mods.Count - 1;
        }

        // ObservableCollection.Move removes at `from` then re-inserts at `to`, giving the
        // intuitive "land on the target row" result whether dragging up or down a flat list.
        _vm.MoveMod(from, to);
    }
}
