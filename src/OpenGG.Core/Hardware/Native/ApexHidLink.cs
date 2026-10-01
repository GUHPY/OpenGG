using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using OneRGB.Application.Devices.Keyboard;
using OneRGB.Hardware.Lighting;
using OneRGB.Windows;

namespace OneRGB.Hardware.Native;

/// <summary>Um canal para RGB, ajustes e flash; ACK armado antes de enviar o comando.</summary>
public sealed class ApexHidLink : IDisposable
{
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly Func<IReadOnlyList<HidInterfaceInfo>?> _interfaces;
    private HidWriter? _writer;
    private HidChannel? _channel;
    private HidInterfaceInfo? _device;
    private bool _lightingUsed;

    public ApexHidLink(Func<IReadOnlyList<HidInterfaceInfo>?> interfaces) =>
        _interfaces = interfaces ?? throw new ArgumentNullException(nameof(interfaces));

    /// <summary>Intervalo mínimo entre comandos de perfil, ajustável para receptores mais lentos.</summary>
    public TimeSpan ProfileReportInterval { get; set; } = TimeSpan.FromMilliseconds(31);

    /// <summary>A failed transaction or explicit ownership drop invalidates the loaded live map.</summary>
    public event Action? Faulted;

    public Task SendFeatureAsync(byte[] report, CancellationToken cancellationToken) =>
        SendFeaturesAsync([report], cancellationToken);

    public Task SendFeaturesAsync(IReadOnlyList<byte[]> reports, CancellationToken cancellationToken, Action? checkOwnership = null) => UseAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(reports);
        foreach (var report in reports)
        {
            ArgumentNullException.ThrowIfNull(report);
            if (report.Length < 2) { throw new ArgumentException("Incomplete HID report.", nameof(reports)); }
            checkOwnership?.Invoke();
            if (report[1] is 0x61 or 0x21 or 0x40)
            {
                _writer!.SetFeature(report);
                _lightingUsed = true;
            }
            else
            {
                EnsureReceiver();
                await AckAsync(report, true, cancellationToken).ConfigureAwait(false);
            }
        }
        return true;
    }, cancellationToken);

    public Task SendOutputAsync(byte[] report, CancellationToken cancellationToken) => UseAsync(async () =>
    {
        EnsureReceiver();
        await AckAsync(report, false, cancellationToken).ConfigureAwait(false);
        return true;
    }, cancellationToken);

    /// <summary>Lê a cópia do receptor. Não infere o slot ativo e não grava nada.</summary>
    public Task<byte[]> ReadProfileAsync(int slot, CancellationToken cancellationToken) => UseAsync(async () =>
    {
        EnsureReceiver();
        return await ReadCoreAsync(slot, cancellationToken).ConfigureAwait(false);
    }, cancellationToken);

    /// <summary>Known read-only connection/battery queries. Reply byte 2 is data, not an ACK status.</summary>
    public Task<byte[]> ReadTelemetryAsync(byte opcode, CancellationToken cancellationToken) => UseAsync(async () =>
    {
        if (opcode is not (0xBC or 0xD2)) { throw new ArgumentOutOfRangeException(nameof(opcode)); }
        EnsureReceiver();
        await ReleaseLightingAsync(cancellationToken).ConfigureAwait(false);
        return await _channel!.RequestAsync([0,opcode], r => r.Length > 2 && r[0] == 0 && r[1] == opcode,
            cancellationToken, timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }, cancellationToken);

    /// <summary>Backup antes de apagar; teclado e receptor; ACK por bloco e releitura exata.</summary>
    public Task<string> WriteProfileAsync(int slot, byte[] profile, string backupDirectory, Action checkOwnership, CancellationToken cancellationToken)
    {
        ApexProfile.Validate(profile);
        ArgumentNullException.ThrowIfNull(checkOwnership);
        var snapshot = (byte[])profile.Clone();
        return UseAsync(async () =>
        {
            EnsureReceiver();
            checkOwnership();
            var original = await ReadCoreAsync(slot, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(backupDirectory);
            var backup = Path.Combine(backupDirectory, $"slot-{slot}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss-fffffff}.bin");
            await File.WriteAllBytesAsync(backup, original, cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (ns, file, erase, write) in new (byte, byte, byte, byte)[]
                    { (3, (byte)slot, 0x42, 0x43), (1, (byte)(10 + slot), 0x02, 0x03) })
                {
                    checkOwnership();
                    await AckAsync([0, erase, ns, file], false, cancellationToken).ConfigureAwait(false);
                    for (var offset = 0; offset < ApexProfile.Length; offset += 512)
                    {
                        checkOwnership();
                        var report = FileReport(write, ns, file, offset);
                        snapshot.AsSpan(offset, 512).CopyTo(report.AsSpan(10));
                        await AckAsync(report, true, cancellationToken).ConfigureAwait(false);
                    }
                    checkOwnership();
                    await AckAsync([0, 0xE6, (byte)(slot - 1)], false, cancellationToken).ConfigureAwait(false);
                }
                var actual = await ReadCoreAsync(slot, cancellationToken).ConfigureAwait(false);
                if (!actual.AsSpan().SequenceEqual(snapshot)) { throw new IOException("Receiver readback differs from the sent profile."); }
                return backup;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                throw new IOException($"Writing stopped and may be incomplete. Original backup: {backup}. No automatic retry occurred.", ex);
            }
        }, cancellationToken);
    }

    public Task LoadProfileAsync(int slot, CancellationToken cancellationToken) => UseAsync(async () =>
    {
        EnsureReceiver();
        CheckSlot(slot);
        await AckAsync([0, 0x53, (byte)(slot - 1)], false, cancellationToken).ConfigureAwait(false);
        await AckAsync([0, 0x68, 0], false, cancellationToken).ConfigureAwait(false);
        return true;
    }, cancellationToken);

    /// <summary>Solta só a iluminação temporária. 0x41 é reset; 0x4B limpa OLED: não são enviados aqui.</summary>
    public void ClearTemporary()
    {
        if (!_io.Wait(TimeSpan.FromSeconds(2))) { return; }
        try
        {
            if (_lightingUsed && _writer is not null && _device?.Identity.ProductId is 0x1644 or 0x1646)
            {
                try { _writer.WriteOutput([0, _device.Identity.ProductId == 0x1644 ? (byte)0x62 : (byte)0x22]); }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException) { Faulted?.Invoke(); }
            }
            DropCore();
        }
        finally { _io.Release(); }
    }

    public void Drop()
    {
        if (!_io.Wait(TimeSpan.FromSeconds(2))) { return; }
        try { Faulted?.Invoke(); DropCore(); }
        finally { _io.Release(); }
    }

    public void Dispose()
    {
        ClearTemporary();
        _io.Dispose();
    }

    private async Task<T> UseAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Open();
            return await operation().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or TimeoutException or OperationCanceledException or InvalidOperationException)
        {
            Faulted?.Invoke();
            DropCore();
            throw;
        }
        finally { _io.Release(); }
    }

    private void Open()
    {
        if (_writer is not null) { return; }
        var device = ApexLighting.Find(_interfaces() ?? []) ?? throw new InvalidOperationException("Apex Pro TKL Gen 3 not found.");
        _writer = HidWriter.Open(device.DevicePath, device.OutputReportLength, device.FeatureReportLength);
        try
        {
            _channel = HidChannel.Open([device]);
            _device = device;
        }
        catch
        {
            _writer.Dispose();
            _writer = null;
            throw;
        }
    }

    private void EnsureReceiver()
    {
        if (_device?.Identity is not { VendorId: 0x1038, ProductId: 0x1644, InterfaceNumber: 3, UsagePage: 0xFFC0, Usage: 1 }
            || _device.FeatureReportLength != 642 || _device.OutputReportLength != 65)
        {
            throw new InvalidOperationException("Profiles and advanced settings are verified only on receiver 1038:1644 / MI_03 / FFC0:0001. Nothing was sent.");
        }
    }

    private async Task AckAsync(byte[] report, bool feature, CancellationToken cancellationToken)
    {
        await ReleaseLightingAsync(cancellationToken).ConfigureAwait(false);
        var start = Stopwatch.GetTimestamp();
        var reply = await _channel!.RequestAsync(report,
            r => r.Length > 2 && r[0] == 0 && r[1] == report[1], cancellationToken,
            () =>
            {
                if (feature) { _writer!.SetFeature(report); }
                else { _writer!.WriteOutput(report); }
                return Task.CompletedTask;
            }, TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        if (reply[2] != 0) { throw new IOException($"The keyboard rejected command 0x{report[1]:X2}: status 0x{reply[2]:X2}."); }
        var remaining = ProfileReportInterval - Stopwatch.GetElapsedTime(start);
        if (remaining > TimeSpan.Zero) { await Task.Delay(remaining, cancellationToken).ConfigureAwait(false); }
    }

    private async Task ReleaseLightingAsync(CancellationToken cancellationToken)
    {
        if (_lightingUsed)
        {
            // O receptor ignora também 0x6F/0x76/0x77 por feature enquanto recebe RGB temporário.
            // 0x62 libera esse modo sem ACK; o mesmo gate impede um quadro RGB antes do comando seguinte.
            _writer!.WriteOutput([0, ApexProtocol.ClearLighting]);
            _lightingUsed = false;
            await Task.Delay(ProfileReportInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<byte[]> ReadCoreAsync(int slot, CancellationToken cancellationToken)
    {
        CheckSlot(slot);
        var profile = new byte[ApexProfile.Length];
        for (var offset = 0; offset < profile.Length; offset += 512)
        {
            await AckAsync(FileReport(0x83, 1, (byte)(10 + slot), offset), true, cancellationToken).ConfigureAwait(false);
            var reply = _writer!.GetFeature(0);
            if (reply.Length != 642 || reply[0] != 0 || reply[1] != 0x83 || reply[2] != 0)
            {
                throw new IOException($"Invalid profile read response: {Convert.ToHexString(reply.AsSpan(0, Math.Min(10, reply.Length)))} ({reply.Length} bytes).");
            }
            reply.AsSpan(3, 512).CopyTo(profile.AsSpan(offset));
        }
        ApexProfile.Validate(profile);
        return profile;
    }

    private static byte[] FileReport(byte opcode, byte ns, byte file, int offset)
    {
        var report = new byte[642];
        report[1] = opcode;
        report[2] = ns;
        report[3] = file;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(4), 512);
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(6), (uint)offset);
        return report;
    }

    private static void CheckSlot(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 5);
    }

    private void DropCore()
    {
        _channel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _channel = null;
        _writer?.Dispose();
        _writer = null;
        _device = null;
        _lightingUsed = false;
    }
}
