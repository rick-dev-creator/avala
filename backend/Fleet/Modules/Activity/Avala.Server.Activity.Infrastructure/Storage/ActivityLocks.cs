using System.Buffers.Binary;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal static class ActivityLocks
{
    public const int Ingestion = 1_096_041_593;

    public static int Of(Guid instance)
    {
        Span<byte> bytes = stackalloc byte[16];
        instance.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes[..4])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[4..8])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[8..12])
            ^ BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]);
    }
}
