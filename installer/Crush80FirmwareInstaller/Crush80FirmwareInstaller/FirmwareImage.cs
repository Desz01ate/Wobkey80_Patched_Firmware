using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;

namespace Crush80FirmwareInstaller;

public enum FirmwareFormat
{
    RawFirmware,
    OtaImage
}

public sealed record FirmwareImage(
    string Path,
    FirmwareFormat Format,
    byte[] SendBuffer,
    int FirmwareSize,
    uint StoredCrc,
    uint ComputedCrc,
    bool CrcValid,
    string Sha256)
{
    public int ChunkCount => SendBuffer.Length / OtaProtocol.ChunkDataSize;

    public static FirmwareImage Load(string path, string? expectedSha256)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The configured firmware file was not found.", path);
        }

        var raw = File.ReadAllBytes(path);
        if (raw.Length < 28)
        {
            throw new InvalidDataException("The firmware file is too small to contain a valid header.");
        }

        FirmwareFormat format;
        ReadOnlySpan<byte> firmware;
        int firmwareSize;

        if (raw.Length > 1_000_000)
        {
            format = FirmwareFormat.OtaImage;
            if (raw.Length < 256)
            {
                throw new InvalidDataException("The OTA image is missing its 256-byte wrapper header.");
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(48, 4));
            if (size == 0 || size > raw.Length - 256 || size > int.MaxValue)
            {
                throw new InvalidDataException($"Invalid firmware size in OTA header: {size} bytes (file is {raw.Length} bytes).");
            }

            firmwareSize = (int)size;
            firmware = raw.AsSpan(256, firmwareSize);
        }
        else
        {
            format = FirmwareFormat.RawFirmware;
            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(24, 4));
            firmwareSize = declaredSize is > 0 and <= int.MaxValue && declaredSize <= raw.Length
                ? (int)declaredSize
                : raw.Length;
            firmware = raw.AsSpan(0, firmwareSize);
        }

        var sha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(NormalizeHash(expectedSha256), sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 mismatch. Expected {expectedSha256}, got {sha256}.");
        }

        var storedCrc = firmwareSize >= 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(firmware[^4..])
            : 0;
        var computedCrc = Crc32.Compute(firmware);
        var crcValid = computedCrc == uint.MaxValue;

        var chunkCount = (firmwareSize + OtaProtocol.ChunkDataSize - 1) / OtaProtocol.ChunkDataSize;
        var sendBuffer = new byte[chunkCount * OtaProtocol.ChunkDataSize];
        Array.Fill(sendBuffer, byte.MaxValue);
        firmware.CopyTo(sendBuffer);

        return new FirmwareImage(path, format, sendBuffer, firmwareSize, storedCrc, computedCrc, crcValid, sha256);
    }

    private static string NormalizeHash(string hash) =>
        new(hash.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());
}

internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ Table[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
