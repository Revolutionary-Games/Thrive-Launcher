namespace Scripts;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   Allows some modification operations on PE files (Windows executables)
/// </summary>
public sealed class PEModifier : IDisposable
{
    private const int OffsetToPEHeaderLocation = 60;
    private const int OffsetFromPEHeaderToOptionalHeader = 24;
    private const int OffsetInOptionalHeaderToSubsystem = 68;

    private const int SubsystemNone = 1;
    private const int SubsystemGUI = 2;
    private const int SubsystemConsole = 3;

    private static readonly byte[] ExecutableMagic = { (byte)'M', (byte)'Z' };
    private static readonly byte[] PEMagic = { (byte)'P', (byte)'E', 0, 0 };

    private static readonly byte[] Optional32BitMagic = { 0x0b, 0x1 };
    private static readonly byte[] Optional64BitMagic = { 0x0b, 0x2 };

    private readonly FileStream file;

    private readonly byte[] smallReadBuffer = new byte[4];

    public PEModifier(string executable)
    {
        file = File.Open(executable, FileMode.Open, FileAccess.ReadWrite);
    }

    public async Task SetExecutableToGUIMode(CancellationToken cancellationToken)
    {
        await CheckMagic(cancellationToken);

        file.Position = OffsetToPEHeaderLocation;
        var peOffset = await ReadDWord(cancellationToken);

        file.Position = peOffset;
        await CheckPEMagic(cancellationToken);

        file.Position = peOffset + OffsetFromPEHeaderToOptionalHeader;
        await CheckOptionalHeaderMagic(cancellationToken);

        var subsystemOffset = peOffset + OffsetFromPEHeaderToOptionalHeader + OffsetInOptionalHeaderToSubsystem;
        file.Position = subsystemOffset;

        var subsystem = file.ReadByte();

        switch (subsystem)
        {
            case SubsystemNone:
            case SubsystemConsole:
            {
                // Switch subsystem
                subsystem = SubsystemGUI;
                file.Position = subsystemOffset;
                file.WriteByte((byte)subsystem);
                break;
            }

            case SubsystemGUI:
                // No need to switch
                break;
            default:
                throw new InvalidOperationException("Unknown subsystem type in executable, not changing it");
        }
    }

    public void Dispose()
    {
        file.Dispose();
    }

    private async Task CheckMagic(CancellationToken cancellationToken)
    {
        file.Position = 0;

        var magic = new byte[2];

        if (await file.ReadAsync(magic, cancellationToken) != magic.Length)
            throw new IOException("Failed to read magic");

        if (!magic.SequenceEqual(ExecutableMagic))
            throw new ArgumentException("File has incorrect magic bytes");

        if (!BitConverter.IsLittleEndian)
        {
            throw new InvalidOperationException(
                "Current platform is not little endian, this code is not going to work without changes");
        }
    }

    private async Task CheckPEMagic(CancellationToken cancellationToken)
    {
        var magic = new byte[4];

        if (await file.ReadAsync(magic, cancellationToken) != magic.Length)
            throw new IOException("Failed to read PE magic");

        if (!magic.SequenceEqual(PEMagic))
            throw new ArgumentException("File has incorrect PE header magic bytes");
    }

    private async Task CheckOptionalHeaderMagic(CancellationToken cancellationToken)
    {
        var magic = new byte[2];

        if (await file.ReadAsync(magic, cancellationToken) != magic.Length)
            throw new IOException("Failed to read optional PE header magic");

        if (!magic.SequenceEqual(Optional32BitMagic) && !magic.SequenceEqual(Optional64BitMagic))
            throw new ArgumentException("File has incorrect optional PE header magic bytes");
    }

    private async Task<int> ReadDWord(CancellationToken cancellationToken)
    {
        if (await file.ReadAsync(smallReadBuffer, cancellationToken) != smallReadBuffer.Length)
            throw new IOException("Failed to read wanted number of bytes");

        return BitConverter.ToInt32(smallReadBuffer);
    }
}
