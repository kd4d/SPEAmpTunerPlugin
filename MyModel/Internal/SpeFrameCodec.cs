#nullable enable

using System;
using System.Collections.Generic;

namespace SPEAmpTunerPlugin.MyModel.Internal
{
    /// <summary>
    /// SPE Expert binary serial framing. Layout matches the pattern described in the
    /// SPE Application Programmer's Guide / EXPERT Protocol documents (sync, length, payload, checksum).
    /// <para>
    /// Frame: <c>[0xFE][0xFE][0xFE][LEN][PAYLOAD × LEN bytes][CHK]</c> where <c>CHK</c> is XOR of
    /// <c>LEN</c> and every payload byte. Align opcode and payload field offsets with the official PDF
    /// if your amplifier firmware uses a different layout.
    /// </para>
    /// </summary>
    internal static class SpeFrameCodec
    {
        public const byte Sync1 = 0xFE;
        public const byte Sync2 = 0xFE;
        public const byte Sync3 = 0xFE;

        public static int FindSyncIndex(IReadOnlyList<byte> buffer)
        {
            for (int i = 0; i + 2 < buffer.Count; i++)
            {
                if (buffer[i] == Sync1 && buffer[i + 1] == Sync2 && buffer[i + 2] == Sync3)
                    return i;
            }
            return -1;
        }

        /// <summary>CHK = XOR of LEN and all payload bytes.</summary>
        public static byte ComputeChecksum(byte lenByte, ReadOnlySpan<byte> payload)
        {
            byte c = lenByte;
            for (int i = 0; i < payload.Length; i++)
                c ^= payload[i];
            return c;
        }

        public static byte[] BuildFrame(ReadOnlySpan<byte> payload)
        {
            if (payload.Length > 255)
                throw new ArgumentException("Payload length must fit in one byte.", nameof(payload));

            byte len = (byte)payload.Length;
            var frame = new byte[4 + payload.Length + 1];
            frame[0] = Sync1;
            frame[1] = Sync2;
            frame[2] = Sync3;
            frame[3] = len;
            payload.CopyTo(frame.AsSpan(4));
            frame[4 + payload.Length] = ComputeChecksum(len, payload);
            return frame;
        }

        /// <summary>
        /// If a complete valid frame is available, removes it from <paramref name="buffer"/> and returns true.
        /// </summary>
        public static bool TryExtractFrame(List<byte> buffer, out byte[] payload)
        {
            payload = Array.Empty<byte>();
            while (buffer.Count >= 5)
            {
                int sync = FindSyncIndex(buffer);
                if (sync < 0)
                {
                    buffer.Clear();
                    return false;
                }

                if (sync > 0)
                    buffer.RemoveRange(0, sync);

                if (buffer.Count < 4)
                    return false;

                int len = buffer[3];
                int total = 4 + len + 1;
                if (buffer.Count < total)
                    return false;

                var span = buffer.GetRange(4, len).ToArray();
                byte expected = buffer[4 + len];
                byte actual = ComputeChecksum((byte)len, span);
                if (actual != expected)
                {
                    buffer.RemoveAt(0);
                    continue;
                }

                buffer.RemoveRange(0, total);
                payload = span;
                return true;
            }

            return false;
        }
    }
}
