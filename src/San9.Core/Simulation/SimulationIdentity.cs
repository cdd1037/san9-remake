using System.Buffers.Binary;

namespace San9.Core.Simulation;

/// <summary>Session-local identities encode their owner and sequence without serializing a payload.</summary>
public static class SimulationIdentity
{
    public static Guid Command(long sequence, int issuerId) => Encode(1, sequence, issuerId);

    internal static Guid Task(long sequence) => Encode(2, sequence, 0);

    public static Guid Request(long sequence) => Encode(3, sequence, 0);

    internal static bool TryGetTaskSequence(Guid id, out long sequence)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        sequence = BinaryPrimitives.ReadInt64BigEndian(bytes[8..]);
        return BinaryPrimitives.ReadInt32BigEndian(bytes) == 2 &&
            BinaryPrimitives.ReadInt32BigEndian(bytes[4..]) == 0;
    }

    private static Guid Encode(int kind, long sequence, int subject)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt32BigEndian(bytes, kind);
        BinaryPrimitives.WriteInt32BigEndian(bytes[4..], subject);
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], sequence);
        return new Guid(bytes, bigEndian: true);
    }
}
