using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.ViewModels;

namespace FreeCleaner;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            grid.SelectedItem is ScannedFileItem item &&
            DataContext is MainViewModel vm)
        {
            vm.OpenFileLocationCommand.Execute(item);
        }
    }

    private void DiskDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            grid.SelectedItem is LargeOrDuplicateFileItem item &&
            DataContext is MainViewModel vm)
        {
            vm.OpenDiskFileLocationCommand.Execute(item);
        }
    }
}
