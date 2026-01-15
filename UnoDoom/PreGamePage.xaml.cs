using UnoDoom.Game;
using UnoDoom.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace UnoDoom;

/// <summary>
/// Converter for boolean to visibility (true = Visible, false = Collapsed).
/// </summary>
public class BoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter to invert boolean to visibility (true = Collapsed, false = Visible).
/// </summary>
public class InverseBoolToVisibilityConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Pre-game page for WAD file selection.
/// </summary>
public sealed partial class PreGamePage : Page
{
    private readonly WadManager _wadManager = new();
    private List<WadInfo> _wads = new();
    private WadInfo? _selectedWad;

    public PreGamePage()
    {
        this.InitializeComponent();
        this.Loaded += PreGamePage_Loaded;
    }

    private async void PreGamePage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadWadListAsync();
    }

    private async Task LoadWadListAsync()
    {
        ShowLoading("Discovering WAD files...");

        try
        {
#if __WASM__ || __ANDROID__ || __IOS__
            // Prepare bundled assets first on mobile/WASM
            await ConfigUtilities.PrepareAssetsAsync();
#endif

            _wads = await _wadManager.GetAvailableWadsAsync();
            WadListView.ItemsSource = _wads;

            // Auto-select first WAD if available
            if (_wads.Count > 0)
            {
                WadListView.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading WADs: {ex}");
        }
        finally
        {
            HideLoading();
        }
    }

    private void WadListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedWad = WadListView.SelectedItem as WadInfo;
        StartGameButton.IsEnabled = _selectedWad != null;
    }

    private async void AddWadButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".wad");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

#if WINDOWS
            // WinUI 3 requires window handle for file picker
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
#endif

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                ShowLoading($"Copying {file.Name}...");

                try
                {
                    await _wadManager.CopyWadToLocalStorageAsync(file);
                    await LoadWadListAsync(); // Refresh list
                }
                catch (Exception ex)
                {
                    await ShowErrorDialogAsync($"Failed to add WAD file: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialogAsync($"Failed to open file picker: {ex.Message}");
        }
        finally
        {
            HideLoading();
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is WadInfo wadInfo)
        {
            // Don't allow deleting bundled WADs
            if (wadInfo.IsBundled)
            {
                return;
            }

            // Confirm deletion
            var dialog = new ContentDialog
            {
                Title = "Delete WAD File",
                Content = $"Are you sure you want to delete '{wadInfo.FileName}'?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ShowLoading($"Deleting {wadInfo.FileName}...");

                var deleted = await _wadManager.DeleteUserWadAsync(wadInfo.FileName);
                if (deleted)
                {
                    await LoadWadListAsync(); // Refresh list
                }
                else
                {
                    await ShowErrorDialogAsync($"Failed to delete '{wadInfo.FileName}'.");
                }

                HideLoading();
            }
        }
    }

    private void StartGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWad != null)
        {
            Frame.Navigate(typeof(GamePage), _selectedWad.FullPath);
        }
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void ShowLoading(string message)
    {
        LoadingText.Text = message;
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }
}
