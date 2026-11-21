using System.Windows.Controls;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.ViewModels;

namespace FreeCleaner.Views;

public partial class DiskAnalyzerView : UserControl
{
    public DiskAnalyzerView()
    {
        InitializeComponent();
    }

    private void DiskDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: LargeOrDuplicateFileItem item } &&
            DataContext is MainViewModel vm)
        {
            vm.OpenDiskFileLocationCommand.Execute(item);
        }
    }
}
