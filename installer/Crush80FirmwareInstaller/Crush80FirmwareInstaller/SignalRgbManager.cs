using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Crush80FirmwareInstaller;

public enum SignalRgbInstallState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
    MissingSource
}

public sealed class SignalRgbPluginViewModel : INotifyPropertyChanged
{
    private SignalRgbInstallState _state = SignalRgbInstallState.NotInstalled;
    private string _statusText = "Not installed";
    private string? _installedSha256;
    private DateTime? _installedDate;
    private long? _installedFileSize;
    private bool _isBusy;

    public required SignalRgbPluginInfo Plugin { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }

    public SignalRgbInstallState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInstalled));
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public string? InstalledSha256
    {
        get => _installedSha256;
        set
        {
            if (_installedSha256 != value)
            {
                _installedSha256 = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? InstalledDate
    {
        get => _installedDate;
        set
        {
            if (_installedDate != value)
            {
                _installedDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InstalledDateFormatted));
            }
        }
    }

    public string InstalledDateFormatted =>
        InstalledDate.HasValue ? InstalledDate.Value.ToString("yyyy-MM-dd HH:mm") : "—";

    public long? InstalledFileSize
    {
        get => _installedFileSize;
        set
        {
            if (_installedFileSize != value)
            {
                _installedFileSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InstalledFileSizeFormatted));
            }
        }
    }

    public string InstalledFileSizeFormatted =>
        InstalledFileSize.HasValue ? $"{InstalledFileSize.Value:N0} bytes" : "—";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
            }
        }
    }

    public bool IsInstalled => State is SignalRgbInstallState.Installed or SignalRgbInstallState.UpdateAvailable;
    public bool CanInstall => !_isBusy && State != SignalRgbInstallState.MissingSource;
    public bool CanUninstall => !_isBusy && IsInstalled;

    public string ActionText => State switch
    {
        SignalRgbInstallState.Installed => "Reinstall",
        SignalRgbInstallState.UpdateAvailable => "Update",
        SignalRgbInstallState.MissingSource => "Source missing",
        _ => "Install"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class InstalledPluginFileInfo
{
    public required string FileName { get; init; }
    public required string FullPath { get; init; }
    public required long FileSize { get; init; }
    public required DateTime LastModified { get; init; }
    public required string Sha256 { get; init; }
    public string? MatchedPluginName { get; init; }
    public bool IsManagedByInstaller { get; init; }
}

public static class SignalRgbManager
{
    public static string GetDefaultPluginsDirectory()
    {
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(myDocs, "WhirlwindFX", "Plugins");
    }

    public static string GetVortxEngineDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "VortxEngine");
    }

    public static bool IsSignalRgbRunning(out int processCount)
    {
        var processNames = new[] { "SignalRGB", "Signal", "SignalRgbLauncher" };
        var total = 0;
        foreach (var name in processNames)
        {
            try
            {
                total += Process.GetProcessesByName(name).Length;
            }
            catch
            {
                // Ignore process query issues
            }
        }

        processCount = total;
        return total > 0;
    }

    public static string? FindSignalRgbExecutable()
    {
        var vortx = GetVortxEngineDirectory();
        if (!Directory.Exists(vortx))
        {
            return null;
        }

        var launcher = Path.Combine(vortx, "SignalRgbLauncher.exe");
        if (File.Exists(launcher))
        {
            return launcher;
        }

        var directApp = Path.Combine(vortx, "Signal.exe");
        if (File.Exists(directApp))
        {
            return directApp;
        }

        try
        {
            var appDirs = Directory.GetDirectories(vortx, "app-*", SearchOption.TopDirectoryOnly);
            foreach (var appDir in appDirs.OrderByDescending(d => d))
            {
                var candidate = Path.Combine(appDir, "Signal-x64", "Signal.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Ignore directory scanning errors
        }

        return null;
    }

    public static string CalculateSha256(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    public static (SignalRgbInstallState state, string statusText, DateTime? date, long? size, string? sha256) InspectPlugin(
        SignalRgbPluginInfo plugin,
        string sourcePath,
        string targetDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            return (SignalRgbInstallState.MissingSource, "Source file not found", null, null, null);
        }

        var destPath = Path.Combine(targetDirectory, plugin.EffectiveDestinationFile);
        if (!File.Exists(destPath))
        {
            return (SignalRgbInstallState.NotInstalled, "Not installed", null, null, null);
        }

        try
        {
            var fileInfo = new FileInfo(destPath);
            var destHash = CalculateSha256(destPath);
            var sourceHash = CalculateSha256(sourcePath);

            var isMatch = string.Equals(destHash, sourceHash, StringComparison.OrdinalIgnoreCase);
            if (isMatch)
            {
                return (SignalRgbInstallState.Installed, "Installed (Up to date)", fileInfo.LastWriteTime, fileInfo.Length, destHash);
            }

            return (SignalRgbInstallState.UpdateAvailable, "Installed (Modified / Update available)", fileInfo.LastWriteTime, fileInfo.Length, destHash);
        }
        catch (Exception ex)
        {
            return (SignalRgbInstallState.Installed, $"Installed (Read error: {ex.Message})", null, null, null);
        }
    }

    public static bool InstallPlugin(string sourcePath, string destinationPath, out string message)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                message = $"Source plugin file not found: {sourcePath}";
                return false;
            }

            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destinationPath, overwrite: true);
            message = $"Successfully installed {Path.GetFileName(destinationPath)}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to install {Path.GetFileName(destinationPath)}: {ex.Message}";
            return false;
        }
    }

    public static bool UninstallPlugin(string destinationPath, out string message)
    {
        try
        {
            if (!File.Exists(destinationPath))
            {
                message = $"Plugin file does not exist: {Path.GetFileName(destinationPath)}";
                return true;
            }

            File.Delete(destinationPath);
            message = $"Successfully removed {Path.GetFileName(destinationPath)}.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Failed to remove {Path.GetFileName(destinationPath)}: {ex.Message}";
            return false;
        }
    }

    public static List<InstalledPluginFileInfo> ScanInstalledPlugins(
        string targetDirectory,
        IReadOnlyList<SignalRgbPluginInfo>? knownPlugins)
    {
        var list = new List<InstalledPluginFileInfo>();
        if (!Directory.Exists(targetDirectory))
        {
            return list;
        }

        try
        {
            var files = Directory.GetFiles(targetDirectory, "*.js", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var hash = CalculateSha256(file);
                var matched = knownPlugins?.FirstOrDefault(p =>
                    string.Equals(p.EffectiveDestinationFile, fileInfo.Name, StringComparison.OrdinalIgnoreCase));

                list.Add(new InstalledPluginFileInfo
                {
                    FileName = fileInfo.Name,
                    FullPath = file,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime,
                    Sha256 = hash,
                    MatchedPluginName = matched?.Name,
                    IsManagedByInstaller = matched != null
                });
            }
        }
        catch
        {
            // Ignore scan errors
        }

        return list.OrderByDescending(f => f.IsManagedByInstaller).ThenBy(f => f.FileName).ToList();
    }

    public static bool RestartSignalRgb(out string message)
    {
        try
        {
            var processNames = new[] { "SignalRGB", "Signal", "SignalRgbLauncher" };
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch
                    {
                        // Ignore process kill issues
                    }
                }
            }

            var exePath = FindSignalRgbExecutable();
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                message = "SignalRGB restarted successfully.";
                return true;
            }

            message = "SignalRGB processes stopped. Please launch SignalRGB from your Start Menu.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Unable to restart SignalRGB: {ex.Message}";
            return false;
        }
    }
}
