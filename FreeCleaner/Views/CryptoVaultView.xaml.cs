using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FreeCleaner.ViewModels;

namespace FreeCleaner.Views;

public partial class CryptoVaultView : UserControl
{
    private bool _isSyncingPassword;
    private bool _isPasswordRevealed;
    private MainViewModel? _subscribedVm;

    public CryptoVaultView()
    {
        InitializeComponent();
        DataContextChanged += CryptoVaultView_DataContextChanged;
        Unloaded += CryptoVaultView_Unloaded;
    }

    private void CryptoVaultView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.PropertyChanged -= ViewModel_PropertyChanged;

        _subscribedVm = e.NewValue as MainViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged += ViewModel_PropertyChanged;
            SyncPasswordBoxFromViewModel(_subscribedVm.VaultPasswordInput);
        }
    }

    private void CryptoVaultView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= ViewModel_PropertyChanged;
            _subscribedVm = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.VaultPasswordInput) && sender is MainViewModel vm)
            SyncPasswordBoxFromViewModel(vm.VaultPasswordInput);
    }

    private void SyncPasswordBoxFromViewModel(string password)
    {
        if (_isSyncingPassword)
            return;

        _isSyncingPassword = true;
        try
        {
            if (!string.Equals(VaultPasswordBox.Password, password, StringComparison.Ordinal))
                VaultPasswordBox.Password = password ?? string.Empty;
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private void VaultPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPassword || DataContext is not MainViewModel vm)
            return;

        _isSyncingPassword = true;
        try
        {
            vm.VaultPasswordInput = VaultPasswordBox.Password;
        }
        finally
        {
            _isSyncingPassword = false;
        }
    }

    private void ToggleVaultPasswordVisibility_Click(object sender, RoutedEventArgs e)
    {
        _isPasswordRevealed = !_isPasswordRevealed;
        if (_isPasswordRevealed)
        {
            VaultPasswordBox.Visibility = Visibility.Collapsed;
            VaultPasswordPlainBox.Visibility = Visibility.Visible;
            VaultPasswordPlainBox.Focus();
            VaultPasswordPlainBox.CaretIndex = VaultPasswordPlainBox.Text.Length;
        }
        else
        {
            SyncPasswordBoxFromViewModel(DataContext is MainViewModel vm ? vm.VaultPasswordInput : VaultPasswordPlainBox.Text);
            VaultPasswordPlainBox.Visibility = Visibility.Collapsed;
            VaultPasswordBox.Visibility = Visibility.Visible;
            VaultPasswordBox.Focus();
        }
    }
}
