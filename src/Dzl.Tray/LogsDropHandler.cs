using GongSolutions.Wpf.DragDrop;
using Dzl.Tray.ViewModels;

namespace Dzl.Tray;

/// <summary>
/// gong-wpf-dragdrop handler for the Logs page panes. Unlike the Mods grid there is no
/// filtered view, so dragged + target items map directly onto the <c>LogPanes</c> collection.
/// The reorder mutates the ObservableCollection so every view mode (grid/list/tabs/focus
/// dropdown) reflects the new order immediately.
/// </summary>
public sealed class LogsDropHandler : IDropTarget
{
    private readonly MainViewModel _vm;

    public LogsDropHandler(MainViewModel vm) => _vm = vm;

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is LogPaneVm && dropInfo.TargetItem is LogPaneVm)
        {
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
            dropInfo.Effects = System.Windows.DragDropEffects.Move;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not LogPaneVm dragged) return;

        var panes = _vm.LogPanes;
        var from = panes.IndexOf(dragged);
        if (from < 0) return;

        int to;
        if (dropInfo.TargetItem is LogPaneVm target)
        {
            to = panes.IndexOf(target);
            if (to < 0) return;
        }
        else
        {
            to = panes.Count - 1;
        }
        if (to == from) return;
        panes.Move(from, to);
    }
}
