using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Crush80FirmwareInstaller;

public partial class MainWindow : Window
{
    private readonly string _applicationDirectory = AppContext.BaseDirectory;
    private LoadedCatalog? _loadedCatalog;
    private FirmwareRelease? _selectedRelease;
    private DeviceTarget? _selectedTarget;
    private FirmwareImage? _firmwareImage;
    private HidDeviceInfo? _deviceInfo;
    private CancellationTokenSource? _flashCancellation;
    private bool _busy;
    private int _selectionGeneration;

    // SignalRGB state
    private string _pluginsDirectory = SignalRgbManager.GetDefaultPluginsDirectory();
    private readonly ObservableCollection<SignalRgbPluginViewModel> _pluginViewModels = [];
    private readonly ObservableCollection<InstalledPluginFileInfo> _installedFiles = [];

    public MainWindow()
    {
        InitializeComponent();
        PluginsItemsControl.ItemsSource = _pluginViewModels;
        InstalledFilesItemsControl.ItemsSource = _installedFiles;
        Loaded += async (_, _) => await LoadCatalogAsync();
    }

    #region Navigation & General Actions

    private void NavTab_Checked(object sender, RoutedEventArgs e)
    {
        if (FirmwarePage == null || SignalRgbPage == null)
        {
            return;
        }

        if (FirmwareNavTab.IsChecked == true)
        {
            FirmwarePage.Visibility = Visibility.Visible;
            SignalRgbPage.Visibility = Visibility.Collapsed;
            FooterNoticeTextBlock.Text = "Recovery: if a firmware update fails, reconnect by USB and flash a valid firmware image again.";
        }
        else if (SignalRgbNavTab.IsChecked == true)
        {
            FirmwarePage.Visibility = Visibility.Collapsed;
            SignalRgbPage.Visibility = Visibility.Visible;
            FooterNoticeTextBlock.Text = "SignalRGB: custom plugins require the patched firmware for hardware VIA hue control.";
            RefreshSignalRgbStatus();
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", _applicationDirectory) { UseShellExecute = true });
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    #endregion

    #region Catalog & Firmware Flasher Page

    private async Task LoadCatalogAsync()
    {
        if (_busy)
        {
            return;
        }

        try
        {
            _loadedCatalog = CatalogLoader.Load(_applicationDirectory);
            ProductNameTextBlock.Text = _loadedCatalog.Catalog.ProductName;
            SupportMessageTextBlock.Text = _loadedCatalog.Catalog.SupportMessage;
            Title = _loadedCatalog.Catalog.ProductName;

            var releases = _loadedCatalog.Catalog.Firmware
                .OrderByDescending(release => release.Recommended)
                .ThenByDescending(release => release.Version)
                .ToList();
            FirmwareComboBox.ItemsSource = releases;
            FirmwareComboBox.SelectedItem = releases.FirstOrDefault();
            AppendLog($"Loaded {releases.Count} firmware release(s) and {_loadedCatalog.Catalog.Plugins.Count} SignalRGB plugin(s) from {CatalogLoader.FileName}.");

            if (releases.Count == 0)
            {
                ClearFirmwareDetails("No firmware releases are configured.");
            }

            LoadSignalRgbPlugins();
        }
        catch (Exception exception)
        {
            _loadedCatalog = null;
            FirmwareComboBox.ItemsSource = null;
            ClearFirmwareDetails(exception.Message);
            SetDeviceMissing("Catalog unavailable", "Fix firmware-catalog.json, then reload it.");
            AppendLog($"Catalog error: {exception.Message}");
            AppendSignalRgbLog($"Catalog error: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Unable to load firmware catalog", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await Task.CompletedTask;
    }

    private async void FirmwareComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadedCatalog is null || FirmwareComboBox.SelectedItem is not FirmwareRelease release)
        {
            return;
        }

        var generation = ++_selectionGeneration;
        _selectedRelease = release;
        _selectedTarget = _loadedCatalog.TargetFor(release);
        _firmwareImage = null;
        _deviceInfo = null;
        FirmwareDescriptionTextBlock.Text = release.Description;
        FileTextBlock.Text = release.File;
        FormatTextBlock.Text = "Loading…";
        SizeTextBlock.Text = "Loading…";
        IntegrityTextBlock.Text = "Checking…";
        UpdateFlashAvailability();

        try
        {
            var path = _loadedCatalog.ResolveFirmwarePath(release);
            var image = await Task.Run(() => FirmwareImage.Load(path, release.Sha256));
            if (generation != _selectionGeneration)
            {
                return;
            }

            _firmwareImage = image;
            FormatTextBlock.Text = image.Format == FirmwareFormat.OtaImage ? "2 MB OTA image" : "Raw firmware";
            SizeTextBlock.Text = $"{image.FirmwareSize:N0} bytes, {image.ChunkCount:N0} chunks";
            IntegrityTextBlock.Text = image.CrcValid
                ? $"CRC valid, SHA-256 {image.Sha256[..12]}…"
                : $"CRC mismatch (computed 0x{image.ComputedCrc:X8})";
            IntegrityTextBlock.Foreground = image.CrcValid
                ? new SolidColorBrush(Color.FromRgb(21, 128, 61))
                : new SolidColorBrush(Color.FromRgb(185, 28, 28));
            FileTextBlock.Text = path;
            AppendLog($"Validated {release.DisplayName}: {image.FirmwareSize:N0} bytes, CRC {(image.CrcValid ? "OK" : "FAILED")}.");
        }
        catch (Exception exception)
        {
            if (generation != _selectionGeneration)
            {
                return;
            }

            ClearFirmwareDetails(exception.Message);
            AppendLog($"Firmware error: {exception.Message}");
        }

        await RefreshDeviceAsync();
    }

    private async Task RefreshDeviceAsync()
    {
        if (_selectedTarget is null || _busy)
        {
            return;
        }

        var target = _selectedTarget;
        SetDeviceSearching();
        try
        {
            var device = await Task.Run(() => HidDeviceEnumerator.Find(target));
            if (!ReferenceEquals(target, _selectedTarget))
            {
                return;
            }

            _deviceInfo = device;
            if (device is null)
            {
                SetDeviceMissing("Keyboard not found", $"Expected VID {target.VendorId}, PID {target.ProductId}, usage {target.UsagePage}.");
                AppendLog($"OTA interface not found for {target.Name}.");
            }
            else
            {
                DeviceStatusBorder.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
                DeviceStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
                DeviceStatusTextBlock.Text = "Keyboard ready";
                DeviceDetailsTextBlock.Text = $"{target.Name}\nHID reports: input {device.InputReportLength} bytes, output {device.OutputReportLength} bytes";
                AppendLog($"Found OTA interface: VID 0x{device.VendorId:X4}, PID 0x{device.ProductId:X4}, usage 0x{device.UsagePage:X4}.");
            }
        }
        catch (Exception exception)
        {
            _deviceInfo = null;
            SetDeviceMissing("Device scan failed", exception.Message);
            AppendLog($"Device scan error: {exception.Message}");
        }

        UpdateFlashAvailability();
    }

    private async void FlashButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRelease is null || _selectedTarget is null || _firmwareImage is null || _deviceInfo is null ||
            !_firmwareImage.CrcValid || !ConfirmationCheckBox.IsChecked.GetValueOrDefault())
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Install {_selectedRelease.DisplayName} on {_selectedTarget.Name}?\n\nDo not disconnect the USB cable until the update finishes.",
            "Confirm firmware update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        FlashProgressBar.Value = 0;
        ProgressPercentTextBlock.Text = "0%";
        _flashCancellation = new CancellationTokenSource();
        AppendLog($"Starting {_selectedRelease.DisplayName} update.");

        var progress = new Progress<FlashProgress>(value =>
        {
            FlashProgressBar.Value = value.Percent;
            ProgressPercentTextBlock.Text = $"{value.Percent}%";
            ProgressStatusTextBlock.Text = value.PacketsSent == 0
                ? value.Status
                : $"{value.Status}  {value.PacketsSent}/{value.TotalPackets} packets, {value.KilobytesPerSecond:F1} KB/s";
        });

        try
        {
            var result = await new OtaFlasher().FlashAsync(
                _deviceInfo,
                _selectedTarget,
                _firmwareImage,
                progress,
                _flashCancellation.Token);

            ProgressStatusTextBlock.Text = result.Message;
            AppendLog(result.Message);
            if (result.Success)
            {
                FlashProgressBar.Value = 100;
                ProgressPercentTextBlock.Text = "100%";
                MessageBox.Show(this, result.Message + "\n\nIf the keyboard does not respond, unplug and reconnect it.", "Update complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this, result.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            ProgressStatusTextBlock.Text = "Update cancelled. Reconnect the keyboard before retrying.";
            AppendLog("Update cancelled by user.");
        }
        catch (Exception exception)
        {
            ProgressStatusTextBlock.Text = "Update failed.";
            AppendLog($"Update error: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _flashCancellation?.Dispose();
            _flashCancellation = null;
            SetBusy(false);
            _deviceInfo = null;
            await RefreshDeviceAsync();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDeviceAsync();

    private void ConfirmationCheckBox_Click(object sender, RoutedEventArgs e) => UpdateFlashAvailability();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => _flashCancellation?.Cancel();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_busy)
        {
            return;
        }

        var result = MessageBox.Show(this, "A firmware update is active. Cancel it and close the installer?", "Update in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        _flashCancellation?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        FirmwareComboBox.IsEnabled = !busy;
        ReloadButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        OpenFolderButton.IsEnabled = !busy;
        ConfirmationCheckBox.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateFlashAvailability();
    }

    private void UpdateFlashAvailability()
    {
        FlashButton.IsEnabled = !_busy &&
                                _selectedRelease is not null &&
                                _selectedTarget is not null &&
                                _firmwareImage is { CrcValid: true } &&
                                _deviceInfo is not null &&
                                ConfirmationCheckBox.IsChecked.GetValueOrDefault();
    }

    private void ClearFirmwareDetails(string message)
    {
        _firmwareImage = null;
        FormatTextBlock.Text = "Unavailable";
        SizeTextBlock.Text = "Unavailable";
        IntegrityTextBlock.Text = message;
        IntegrityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        UpdateFlashAvailability();
    }

    private void SetDeviceSearching()
    {
        _deviceInfo = null;
        DeviceStatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 247, 237));
        DeviceStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 215, 170));
        DeviceStatusTextBlock.Text = "Searching for keyboard…";
        DeviceDetailsTextBlock.Text = "Checking Windows HID interfaces.";
        UpdateFlashAvailability();
    }

    private void SetDeviceMissing(string title, string details)
    {
        DeviceStatusBorder.Background = new SolidColorBrush(Color.FromRgb(254, 242, 242));
        DeviceStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
        DeviceStatusTextBlock.Text = title;
        DeviceDetailsTextBlock.Text = details;
        UpdateFlashAvailability();
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    #endregion

    #region SignalRGB Plugin Management Page

    private void LoadSignalRgbPlugins()
    {
        _pluginViewModels.Clear();
        if (_loadedCatalog == null)
        {
            return;
        }

        foreach (var plugin in _loadedCatalog.Catalog.Plugins.OrderByDescending(p => p.Recommended).ThenBy(p => p.Name))
        {
            var sourcePath = _loadedCatalog.ResolvePluginPath(plugin);
            var destPath = Path.Combine(_pluginsDirectory, plugin.EffectiveDestinationFile);

            var vm = new SignalRgbPluginViewModel
            {
                Plugin = plugin,
                SourcePath = sourcePath,
                DestinationPath = destPath
            };

            _pluginViewModels.Add(vm);
        }

        RefreshSignalRgbStatus();
    }

    private void RefreshSignalRgbStatus()
    {
        PluginsFolderTextBox.Text = _pluginsDirectory;

        // Folder status
        var folderExists = Directory.Exists(_pluginsDirectory);
        if (folderExists)
        {
            PluginsFolderStatusBorder.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
            PluginsFolderStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
            PluginsFolderStatusTextBlock.Text = "Directory Ready";
            PluginsFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
        }
        else
        {
            PluginsFolderStatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 251, 235));
            PluginsFolderStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 240, 138));
            PluginsFolderStatusTextBlock.Text = "Will create on install";
            PluginsFolderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(180, 83, 9));
        }

        // Process status
        var isRunning = SignalRgbManager.IsSignalRgbRunning(out var procCount);
        if (isRunning)
        {
            SignalRgbProcessStatusBorder.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
            SignalRgbProcessStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
            SignalRgbProcessStatusTextBlock.Text = $"Running ({procCount} process{(procCount > 1 ? "es" : "")})";
            SignalRgbProcessStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
        }
        else
        {
            SignalRgbProcessStatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            SignalRgbProcessStatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            SignalRgbProcessStatusTextBlock.Text = "Not running";
            SignalRgbProcessStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }

        // Refresh each plugin's installation state
        foreach (var vm in _pluginViewModels)
        {
            var (state, statusText, date, size, sha256) = SignalRgbManager.InspectPlugin(vm.Plugin, vm.SourcePath, _pluginsDirectory);
            vm.State = state;
            vm.StatusText = statusText;
            vm.InstalledDate = date;
            vm.InstalledFileSize = size;
            vm.InstalledSha256 = sha256;
        }

        // Scan installed files in directory
        _installedFiles.Clear();
        var scannedFiles = SignalRgbManager.ScanInstalledPlugins(_pluginsDirectory, _loadedCatalog?.Catalog.Plugins);
        foreach (var file in scannedFiles)
        {
            _installedFiles.Add(file);
        }

        InstalledCountTextBlock.Text = $"{_installedFiles.Count} plugin file{(_installedFiles.Count == 1 ? "" : "s")}";
    }

    private void PluginInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SignalRgbPluginViewModel vm })
        {
            return;
        }

        vm.IsBusy = true;
        try
        {
            var success = SignalRgbManager.InstallPlugin(vm.SourcePath, vm.DestinationPath, out var message);
            AppendSignalRgbLog(message);
            if (!success)
            {
                MessageBox.Show(this, message, "Plugin Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                if (SignalRgbManager.IsSignalRgbRunning(out _))
                {
                    AppendSignalRgbLog("Note: SignalRGB is running. Click 'Restart SignalRGB' to apply new plugin changes.");
                }
            }
        }
        finally
        {
            vm.IsBusy = false;
            RefreshSignalRgbStatus();
        }
    }

    private void PluginUninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SignalRgbPluginViewModel vm })
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Remove plugin {vm.Plugin.EffectiveDestinationFile} from SignalRGB?",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        vm.IsBusy = true;
        try
        {
            var success = SignalRgbManager.UninstallPlugin(vm.DestinationPath, out var message);
            AppendSignalRgbLog(message);
            if (!success)
            {
                MessageBox.Show(this, message, "Plugin Removal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            vm.IsBusy = false;
            RefreshSignalRgbStatus();
        }
    }

    private void InstallRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        var recommended = _pluginViewModels.Where(p => p.Plugin.Recommended).ToList();
        if (recommended.Count == 0)
        {
            MessageBox.Show(this, "No recommended plugins found in catalog.", "Install Recommended", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var installedCount = 0;
        foreach (var vm in recommended)
        {
            vm.IsBusy = true;
            try
            {
                if (SignalRgbManager.InstallPlugin(vm.SourcePath, vm.DestinationPath, out var message))
                {
                    installedCount++;
                    AppendSignalRgbLog(message);
                }
                else
                {
                    AppendSignalRgbLog($"Error: {message}");
                }
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        RefreshSignalRgbStatus();
        var notice = $"Installed {installedCount} recommended plugin(s).";
        if (SignalRgbManager.IsSignalRgbRunning(out _))
        {
            notice += "\n\nSignalRGB is running. Click 'Restart SignalRGB' or restart SignalRGB manually to load the plugins.";
        }

        MessageBox.Show(this, notice, "Recommended Plugins Installed", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UninstallAllButton_Click(object sender, RoutedEventArgs e)
    {
        var installed = _pluginViewModels.Where(p => p.IsInstalled).ToList();
        if (installed.Count == 0)
        {
            MessageBox.Show(this, "No managed Crush 80 plugins are currently installed.", "Uninstall Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Remove all {installed.Count} Wobkey Crush 80 plugins from the SignalRGB directory?",
            "Confirm Uninstall All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var vm in installed)
        {
            vm.IsBusy = true;
            try
            {
                if (SignalRgbManager.UninstallPlugin(vm.DestinationPath, out var message))
                {
                    AppendSignalRgbLog(message);
                }
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        RefreshSignalRgbStatus();
        MessageBox.Show(this, "All managed Wobkey Crush 80 plugins have been removed.", "Uninstall Complete", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteInstalledFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledPluginFileInfo fileInfo })
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Delete plugin file '{fileInfo.FileName}' from SignalRGB directory?",
            "Confirm Delete File",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (SignalRgbManager.UninstallPlugin(fileInfo.FullPath, out var message))
        {
            AppendSignalRgbLog(message);
        }
        else
        {
            MessageBox.Show(this, message, "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshSignalRgbStatus();
    }

    private void BrowsePluginFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select SignalRGB Plugins Directory",
            InitialDirectory = Directory.Exists(_pluginsDirectory) ? _pluginsDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _pluginsDirectory = dialog.FolderName;
            AppendSignalRgbLog($"Selected custom plugins directory: {_pluginsDirectory}");
            LoadSignalRgbPlugins();
        }
    }

    private void ResetPluginFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _pluginsDirectory = SignalRgbManager.GetDefaultPluginsDirectory();
        AppendSignalRgbLog($"Reset plugins directory to default: {_pluginsDirectory}");
        LoadSignalRgbPlugins();
    }

    private void OpenPluginsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
            }

            Process.Start(new ProcessStartInfo("explorer.exe", _pluginsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestartSignalRgbButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            this,
            "This will close active SignalRGB processes and relaunch SignalRGB.\n\nContinue?",
            "Restart SignalRGB",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (SignalRgbManager.RestartSignalRgb(out var message))
        {
            AppendSignalRgbLog(message);
            MessageBox.Show(this, message, "Restart SignalRGB", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            AppendSignalRgbLog($"Restart failed: {message}");
            MessageBox.Show(this, message, "Restart SignalRGB Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        RefreshSignalRgbStatus();
    }

    private void RefreshSignalRgbButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSignalRgbStatus();
        AppendSignalRgbLog("Refreshed SignalRGB plugin and process status.");
    }

    private void AppendSignalRgbLog(string message)
    {
        SignalRgbLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        SignalRgbLogTextBox.ScrollToEnd();
    }

    #endregion
}
