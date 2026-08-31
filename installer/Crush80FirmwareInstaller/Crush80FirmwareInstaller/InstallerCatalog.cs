using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Crush80FirmwareInstaller;

public sealed class InstallerCatalog
{
    public string ProductName { get; init; } = "Firmware Installer";
    public string SupportMessage { get; init; } = "Keep the keyboard connected by USB during the update.";
    public List<DeviceTarget> Devices { get; init; } = [];
    public List<FirmwareRelease> Firmware { get; init; } = [];
    public List<SignalRgbPluginInfo> Plugins { get; init; } = [];
}

public sealed class DeviceTarget
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string VendorId { get; init; }
    public required string ProductId { get; init; }
    public required string UsagePage { get; init; }
    public byte ReportId { get; init; } = 5;
    public int StartTimeoutSeconds { get; init; } = 5;
    public int PacketTimeoutSeconds { get; init; } = 10;
    public int FinalTimeoutSeconds { get; init; } = 10;

    public ushort ParsedVendorId => ParseHex(VendorId, nameof(VendorId));
    public ushort ParsedProductId => ParseHex(ProductId, nameof(ProductId));
    public ushort ParsedUsagePage => ParseHex(UsagePage, nameof(UsagePage));

    private static ushort ParseHex(string value, string propertyName)
    {
        var normalized = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        if (!ushort.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException($"{propertyName} value '{value}' is not a valid 16-bit hexadecimal number.");
        }

        return result;
    }
}

public sealed class FirmwareRelease
{
    public required string Id { get; init; }
    public required string TargetId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string File { get; init; }
    public string Description { get; init; } = "";
    public string? Sha256 { get; init; }
    public bool Recommended { get; init; }

    public string DisplayName => Recommended ? $"{Name} {Version} (Recommended)" : $"{Name} {Version}";
}

public sealed class SignalRgbPluginInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string File { get; init; }
    public string DestinationFile { get; init; } = "";
    public string TargetMode { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Sha256 { get; init; }
    public bool Recommended { get; init; }

    public string EffectiveDestinationFile =>
        string.IsNullOrWhiteSpace(DestinationFile) ? Path.GetFileName(File) : DestinationFile;

    public string DisplayName => Recommended ? $"{Name} (Recommended)" : Name;
}

public sealed record LoadedCatalog(InstallerCatalog Catalog, string BaseDirectory)
{
    public DeviceTarget TargetFor(FirmwareRelease release) =>
        Catalog.Devices.SingleOrDefault(device => string.Equals(device.Id, release.TargetId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"Firmware '{release.Id}' refers to unknown target '{release.TargetId}'.");

    public string ResolveFirmwarePath(FirmwareRelease release) => ResolvePath(release.File);

    public string ResolvePluginPath(SignalRgbPluginInfo plugin) => ResolvePath(plugin.File);

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath))
        {
            return Path.GetFullPath(relativeOrAbsolutePath);
        }

        var candidate = Path.GetFullPath(Path.Combine(BaseDirectory, relativeOrAbsolutePath));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var current = new DirectoryInfo(BaseDirectory);
        while (current != null)
        {
            var testPath = Path.Combine(current.FullName, relativeOrAbsolutePath);
            if (File.Exists(testPath))
            {
                return Path.GetFullPath(testPath);
            }
            current = current.Parent;
        }

        return candidate;
    }
}

public static class CatalogLoader
{
    public const string FileName = "firmware-catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static LoadedCatalog Load(string applicationDirectory)
    {
        var path = Path.Combine(applicationDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file '{FileName}' was not found beside the installer.", path);
        }

        var catalog = JsonSerializer.Deserialize<InstallerCatalog>(File.ReadAllText(path), JsonOptions)
                      ?? throw new InvalidDataException($"Configuration file '{FileName}' is empty.");

        Validate(catalog);
        return new LoadedCatalog(catalog, Path.GetDirectoryName(path)!);
    }

    private static void Validate(InstallerCatalog catalog)
    {
        if (catalog.Devices.Count == 0)
        {
            throw new InvalidDataException("The catalog must define at least one device target.");
        }

        if (catalog.Firmware.Count == 0)
        {
            throw new InvalidDataException("The catalog must define at least one firmware release.");
        }

        EnsureUnique(catalog.Devices.Select(device => device.Id), "device target");
        EnsureUnique(catalog.Firmware.Select(release => release.Id), "firmware release");

        foreach (var device in catalog.Devices)
        {
            _ = device.ParsedVendorId;
            _ = device.ParsedProductId;
            _ = device.ParsedUsagePage;
            if (device.ReportId == 0)
            {
                throw new InvalidDataException($"Device target '{device.Id}' has an invalid report ID.");
            }

            if (device.StartTimeoutSeconds <= 0 || device.PacketTimeoutSeconds <= 0 || device.FinalTimeoutSeconds <= 0)
            {
                throw new InvalidDataException($"Device target '{device.Id}' has an invalid timeout.");
            }
        }

        var targetIds = catalog.Devices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var release in catalog.Firmware)
        {
            if (!targetIds.Contains(release.TargetId))
            {
                throw new InvalidDataException($"Firmware '{release.Id}' refers to unknown target '{release.TargetId}'.");
            }

            if (string.IsNullOrWhiteSpace(release.File))
            {
                throw new InvalidDataException($"Firmware '{release.Id}' does not specify a file.");
            }
        }
        EnsureUnique(catalog.Plugins.Select(plugin => plugin.Id), "plugin");
        foreach (var plugin in catalog.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.File))
            {
                throw new InvalidDataException($"Plugin '{plugin.Id}' does not specify a file.");
            }
        }
    }
    private static void EnsureUnique(IEnumerable<string> ids, string itemType)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                throw new InvalidDataException($"Every {itemType} must have a unique, non-empty ID.");
            }
        }
    }
}
