using System.Windows.Controls;
using System.Windows.Input;
using FreeCleaner.Models;
using FreeCleaner.ViewModels;

namespace FreeCleaner.Views;

public partial class CleanerView : UserControl
{
    public CleanerView()
    {
        InitializeComponent();
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ScannedFileItem item } &&
            DataContext is MainViewModel vm)
        {
            vm.OpenFileLocationCommand.Execute(item);
        }
    }
}
