using System.Buffers.Binary;
using System.Diagnostics;

namespace Crush80FirmwareInstaller;

public static class OtaProtocol
{
    public const int ChunkDataSize = 16;
    public const int ChunksPerPacket = 3;

    public static byte[] CreateStartPacket(int reportLength, byte reportId)
    {
        EnsureReportLength(reportLength);
        var packet = FilledPacket(reportLength, reportId);
        packet[1] = 0x02;
        packet[2] = 0x02;
        packet[3] = 0x00;
        packet[4] = 0x01;
        packet[5] = 0xFF;
        return packet;
    }

    public static byte[] CreateDataPacket(int reportLength, byte reportId, FirmwareImage image, int firstChunk, out int chunksWritten)
    {
        EnsureReportLength(reportLength);
        if (firstChunk < 0 || firstChunk >= image.ChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(firstChunk));
        }

        var packet = FilledPacket(reportLength, reportId);
        packet[1] = 0x02;
        packet[2] = 0x00;
        packet[3] = 0x00;
        chunksWritten = Math.Min(ChunksPerPacket, image.ChunkCount - firstChunk);
        Span<byte> crcInput = stackalloc byte[18];

        for (var chunkInPacket = 0; chunkInPacket < chunksWritten; chunkInPacket++)
        {
            var chunkIndex = firstChunk + chunkInPacket;
            BinaryPrimitives.WriteUInt16LittleEndian(crcInput, checked((ushort)chunkIndex));
            image.SendBuffer.AsSpan(chunkIndex * ChunkDataSize, ChunkDataSize).CopyTo(crcInput[2..]);

            var packetOffset = 4 + chunkInPacket * 20;
            crcInput.CopyTo(packet.AsSpan(packetOffset, 18));
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(packetOffset + 18, 2), ComputeCrc16(crcInput));
        }

        packet[2] = checked((byte)(chunksWritten * 20));
        return packet;
    }

    public static byte[] CreateEndPacket(int reportLength, byte reportId, int chunkCount)
    {
        EnsureReportLength(reportLength);
        if (chunkCount <= 0 || chunkCount > ushort.MaxValue + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkCount));
        }

        var packet = FilledPacket(reportLength, reportId);
        packet[1] = 0x02;
        packet[2] = 0x06;
        packet[3] = 0x00;
        packet[4] = 0x02;
        packet[5] = 0xFF;

        var lastChunkIndex = checked((ushort)(chunkCount - 1));
        var complement = unchecked((ushort)(0 - lastChunkIndex));
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), lastChunkIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(8, 2), complement);
        return packet;
    }

    public static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = ushort.MaxValue;
        foreach (var value in data)
        {
            var current = value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)(((crc ^ current) & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
                current >>= 1;
            }
        }

        return crc;
    }

    public static bool TryReadFinalResult(ReadOnlySpan<byte> response, byte reportId, out byte result)
    {
        if (response.Length >= 7 && response[0] == reportId && response[1] == 0x02 &&
            response[2] == 0x03 && response[3] == 0x00 && response[4] == 0x06 && response[5] == 0xFF)
        {
            result = response[6];
            return true;
        }

        result = 0;
        return false;
    }

    private static byte[] FilledPacket(int reportLength, byte reportId)
    {
        var packet = new byte[reportLength];
        Array.Fill(packet, byte.MaxValue);
        packet[0] = reportId;
        return packet;
    }

    private static void EnsureReportLength(int reportLength)
    {
        if (reportLength < 64)
        {
            throw new InvalidOperationException($"The OTA protocol requires a 64-byte output report; this device reports {reportLength} bytes.");
        }
    }
}

public sealed record FlashProgress(int Percent, int PacketsSent, int TotalPackets, double KilobytesPerSecond, string Status);
public sealed record FlashResult(bool Success, string Message);

public sealed class OtaFlasher
{
    public async Task<FlashResult> FlashAsync(
        HidDeviceInfo deviceInfo,
        DeviceTarget target,
        FirmwareImage image,
        IProgress<FlashProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var device = HidDevice.Open(deviceInfo, target.ReportId);
        var reportLength = deviceInfo.OutputReportLength;
        var totalPackets = (image.ChunkCount + OtaProtocol.ChunksPerPacket - 1) / OtaProtocol.ChunksPerPacket;

        progress?.Report(new FlashProgress(0, 0, totalPackets, 0, "Starting OTA session…"));
        await device.WriteAsync(OtaProtocol.CreateStartPacket(reportLength, target.ReportId), cancellationToken);
        if (await device.ReadResponseAsync(TimeSpan.FromSeconds(target.StartTimeoutSeconds), cancellationToken) is null)
        {
            return new FlashResult(false, "The keyboard did not acknowledge the OTA start command. Confirm it is connected by USB.");
        }

        var stopwatch = Stopwatch.StartNew();
        var packetNumber = 0;
        for (var chunk = 0; chunk < image.ChunkCount;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packet = OtaProtocol.CreateDataPacket(reportLength, target.ReportId, image, chunk, out var chunksWritten);
            var firstChunk = chunk;
            chunk += chunksWritten;

            await device.WriteAsync(packet, cancellationToken);
            packetNumber++;
            if (await device.ReadResponseAsync(TimeSpan.FromSeconds(target.PacketTimeoutSeconds), cancellationToken) is null)
            {
                return new FlashResult(false, $"No acknowledgement for packet {packetNumber} (chunks {firstChunk}–{chunk - 1}).");
            }

            var bytesSent = Math.Min(chunk * OtaProtocol.ChunkDataSize, image.FirmwareSize);
            var percent = Math.Min(100, bytesSent * 100 / image.FirmwareSize);
            var rate = stopwatch.Elapsed.TotalSeconds > 0
                ? bytesSent / 1024d / stopwatch.Elapsed.TotalSeconds
                : 0;
            progress?.Report(new FlashProgress(percent, packetNumber, totalPackets, rate, "Transferring firmware…"));
        }

        progress?.Report(new FlashProgress(100, packetNumber, totalPackets, 0, "Finalizing update…"));
        await device.WriteAsync(OtaProtocol.CreateEndPacket(reportLength, target.ReportId, image.ChunkCount), cancellationToken);
        var finalResponse = await device.ReadResponseAsync(TimeSpan.FromSeconds(target.FinalTimeoutSeconds), cancellationToken);
        if (finalResponse is null)
        {
            return new FlashResult(true, "Firmware sent successfully. The keyboard disconnected to reboot.");
        }

        if (OtaProtocol.TryReadFinalResult(finalResponse, target.ReportId, out var result))
        {
            return result == 0
                ? new FlashResult(true, "Firmware update completed successfully.")
                : new FlashResult(false, $"The keyboard rejected the update with OTA error code {result}.");
        }

        return new FlashResult(true, "Firmware sent successfully. The keyboard returned an unrecognized final response and may already be rebooting.");
    }
}
