using System;
using System.Security.Cryptography;

namespace RestaurantPos.Domain.Common;

public static class UuidV7
{
    public static Guid NewGuid()
    {
        // UUIDv7 specification (RFC 9562):
        // 48-bit timestamp (ms since epoch) + 12-bit random + 2-bit variant + 62-bit random
        byte[] bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);

        long unixTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 48-bit timestamp in big-endian
        bytes[0] = (byte)((unixTimestampMs >> 40) & 0xFF);
        bytes[1] = (byte)((unixTimestampMs >> 32) & 0xFF);
        bytes[2] = (byte)((unixTimestampMs >> 24) & 0xFF);
        bytes[3] = (byte)((unixTimestampMs >> 16) & 0xFF);
        bytes[4] = (byte)((unixTimestampMs >> 8) & 0xFF);
        bytes[5] = (byte)(unixTimestampMs & 0xFF);

        // Version 7 (0b0111 in high 4 bits of byte 6)
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);

        // Variant (0b10 in high 2 bits of byte 8)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Handle .NET Guid endianness layout when initializing from byte array
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes, 0, 4);
            Array.Reverse(bytes, 4, 2);
            Array.Reverse(bytes, 6, 2);
        }

        return new Guid(bytes);
    }
}
