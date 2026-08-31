using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCatalogAsync();
    }

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
            AppendLog($"Loaded {releases.Count} firmware release(s) from {CatalogLoader.FileName}.");

            if (releases.Count == 0)
            {
                ClearFirmwareDetails("No firmware releases are configured.");
            }
        }
        catch (Exception exception)
        {
            _loadedCatalog = null;
            FirmwareComboBox.ItemsSource = null;
            ClearFirmwareDetails(exception.Message);
            SetDeviceMissing("Catalog unavailable", "Fix firmware-catalog.json, then reload it.");
            AppendLog($"Catalog error: {exception.Message}");
            MessageBox.Show(this, exception.Message, "Unable to load firmware catalog", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await Task.CompletedTask;
    }

    private async void FirmwareComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
            _flashCancellation.Dispose();
            _flashCancellation = null;
            SetBusy(false);
            _deviceInfo = null;
            await RefreshDeviceAsync();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshDeviceAsync();

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync();

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", _applicationDirectory) { UseShellExecute = true });
    }

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
}