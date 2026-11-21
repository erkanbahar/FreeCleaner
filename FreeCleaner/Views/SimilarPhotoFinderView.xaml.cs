using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FreeCleaner.Models;

namespace FreeCleaner.Views;

public partial class SimilarPhotoFinderView : UserControl
{
    public SimilarPhotoFinderView()
    {
        InitializeComponent();
    }

    private void SimilarGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: SimilarPhotoItem photo } && File.Exists(photo.FullPath))
        {
            LoadNonLockingPreview(photo);
        }
        else
        {
            PreviewImageControl.Source = null;
            PreviewFileNameText.Text = "-";
            PreviewDetailsText.Text = string.Empty;
        }
    }

    private void LoadNonLockingPreview(SimilarPhotoItem photo)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(photo.FullPath);
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 520;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            PreviewImageControl.Source = bmp;
            PreviewFileNameText.Text = photo.FileName;
            PreviewDetailsText.Text = $"{photo.RoleBadge}\n{photo.ResolutionText} • {photo.FormattedSize} • {photo.SimilarityText}";
        }
        catch
        {
            PreviewImageControl.Source = null;
            PreviewFileNameText.Text = photo.FileName;
            PreviewDetailsText.Text = "Önizleme oluşturulamadı.";
        }
    }
}
