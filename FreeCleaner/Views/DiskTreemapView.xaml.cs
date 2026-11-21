using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.ViewModels;

namespace FreeCleaner.Views;

public partial class DiskTreemapView : UserControl
{
    public DiskTreemapView()
    {
        InitializeComponent();
    }

    private void TreemapSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.NewSize.Width > 32 && e.NewSize.Height > 32)
        {
            vm.UpdateTreemapViewport(e.NewSize.Width, e.NewSize.Height);
        }
    }

    private void TreemapPathTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter &&
            DataContext is MainViewModel vm &&
            vm.AnalyzeTreemapCommand.CanExecute(null))
        {
            vm.AnalyzeTreemapCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TreemapTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TreemapNodeItem node } &&
            DataContext is MainViewModel vm)
        {
            if (node.IsDirectory && vm.DrillDownTreemapNodeCommand.CanExecute(node))
                vm.DrillDownTreemapNodeCommand.Execute(node);
            else if (vm.OpenTreemapNodeInExplorerCommand.CanExecute(node))
                vm.OpenTreemapNodeInExplorerCommand.Execute(node);
        }
    }

    private void DrillDownMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuItem(sender) is TreemapNodeItem node &&
            DataContext is MainViewModel vm &&
            vm.DrillDownTreemapNodeCommand.CanExecute(node))
        {
            vm.DrillDownTreemapNodeCommand.Execute(node);
        }
    }

    private void OpenExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuItem(sender) is TreemapNodeItem node &&
            DataContext is MainViewModel vm &&
            vm.OpenTreemapNodeInExplorerCommand.CanExecute(node))
        {
            vm.OpenTreemapNodeInExplorerCommand.Execute(node);
        }
    }

    private void RecycleTreemapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetNodeFromMenuItem(sender) is TreemapNodeItem node &&
            DataContext is MainViewModel vm &&
            vm.MoveTreemapNodeToRecycleBinCommand.CanExecute(node))
        {
            vm.MoveTreemapNodeToRecycleBinCommand.Execute(node);
        }
    }

    private static TreemapNodeItem? GetNodeFromMenuItem(object sender)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement target } })
            return target.DataContext as TreemapNodeItem;
        return null;
    }
}
