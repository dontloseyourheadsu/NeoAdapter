using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NeoAdapter.Frontend.ViewModels;

namespace NeoAdapter.Frontend.Views;

public partial class IntegrationsView : UserControl
{
    public IntegrationsView()
    {
        InitializeComponent();
    }

    private async void OnBrowseCsvClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || !topLevel.StorageProvider.CanOpen)
        {
            viewModel.ErrorMessage = "File browsing is unavailable on this platform. Enter the path manually.";
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select CSV file",
            FileTypeFilter =
            [
                new FilePickerFileType("CSV")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });

        var selected = files.FirstOrDefault();
        if (selected is null)
        {
            return;
        }

        var path = selected.TryGetLocalPath() ?? selected.Name;
        viewModel.SetCsvPath(path);
    }
}
