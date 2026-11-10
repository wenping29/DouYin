using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DouYing.Server;

public sealed class AuthService(string secret)
{
    private const int KeyLength = 64;

    public string HashPassword(string password)
    {
        var salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var hash = Scrypt.Derive(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), KeyLength);
        return $"scrypt${salt}${Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public bool VerifyPassword(string password, string? stored)
    {
        var parts = (stored ?? "").Split('$');
        if (parts.Length != 3 || parts[0] != "scrypt" || parts[1].Length == 0)
            return false;
        try
        {
            var expected = Convert.FromHexString(parts[2]);
            var actual = Scrypt.Derive(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(parts[1]), KeyLength);
            return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string CreateToken(long userId)
    {
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            uid = userId,
            exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
        }));
        var unsigned = $"{header}.{payload}";
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes(unsigned));
        return $"{unsigned}.{Base64Url(signature)}";
    }

    public long? ReadToken(string? authorization)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            return null;
        var parts = authorization[7..].Split('.');
        if (parts.Length != 3)
            return null;
        try
        {
            var unsigned = $"{parts[0]}.{parts[1]}";
            var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.ASCII.GetBytes(unsigned));
            var supplied = FromBase64Url(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
                return null;
            using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
            var root = payload.RootElement;
            if (!root.TryGetProperty("uid", out var uid) || !uid.TryGetInt64(out var userId) ||
                !root.TryGetProperty("exp", out var expiry) || expiry.GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return null;
            return userId;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        value += new string('=', (4 - value.Length % 4) % 4);
        return Convert.FromBase64String(value);
    }

    private static class Scrypt
    {
        private const int N = 1 << 14;
        private const int R = 8;
        private const int BlockBytes = 128 * R;

        public static byte[] Derive(byte[] password, byte[] salt, int length)
        {
            var b = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, BlockBytes);
            for (var i = 0; i < b.Length / BlockBytes; i++)
                Romix(b.AsSpan(i * BlockBytes, BlockBytes));
            return Rfc2898DeriveBytes.Pbkdf2(password, b, 1, HashAlgorithmName.SHA256, length);
        }

        private static void Romix(Span<byte> block)
        {
            var x = new uint[BlockBytes / sizeof(uint)];
            for (var i = 0; i < x.Length; i++)
                x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(i * 4, 4));
            var v = new uint[N * x.Length];
            var y = new uint[x.Length];
            for (var i = 0; i < N; i++)
            {
                Array.Copy(x, 0, v, i * x.Length, x.Length);
                BlockMix(x, y);
                (x, y) = (y, x);
            }
            for (var i = 0; i < N; i++)
            {
                var j = (int)(x[^16] | ((ulong)x[^15] << 32)) & (N - 1);
                var start = j * x.Length;
                for (var k = 0; k < x.Length; k++)
                    x[k] ^= v[start + k];
                BlockMix(x, y);
                (x, y) = (y, x);
            }
            for (var i = 0; i < x.Length; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(i * 4, 4), x[i]);
        }

        private static void BlockMix(uint[] input, uint[] output)
        {
            Span<uint> x = stackalloc uint[16];
            for (var i = 0; i < 16; i++)
                x[i] = input[(2 * R - 1) * 16 + i];
            for (var i = 0; i < 2 * R; i++)
            {
                for (var j = 0; j < 16; j++)
                    x[j] ^= input[i * 16 + j];
                Salsa208(x);
                var target = (i / 2 + (i % 2) * R) * 16;
                for (var j = 0; j < 16; j++)
                    output[target + j] = x[j];
            }
        }

        private static void Salsa208(Span<uint> block)
        {
            Span<uint> x = stackalloc uint[16];
            block.CopyTo(x);
            unchecked
            {
                for (var i = 0; i < 8; i += 2)
                {
                    x[4] ^= Rotate(x[0] + x[12], 7); x[8] ^= Rotate(x[4] + x[0], 9);
                    x[12] ^= Rotate(x[8] + x[4], 13); x[0] ^= Rotate(x[12] + x[8], 18);
                    x[9] ^= Rotate(x[5] + x[1], 7); x[13] ^= Rotate(x[9] + x[5], 9);
                    x[1] ^= Rotate(x[13] + x[9], 13); x[5] ^= Rotate(x[1] + x[13], 18);
                    x[14] ^= Rotate(x[10] + x[6], 7); x[2] ^= Rotate(x[14] + x[10], 9);
                    x[6] ^= Rotate(x[2] + x[14], 13); x[10] ^= Rotate(x[6] + x[2], 18);
                    x[3] ^= Rotate(x[15] + x[11], 7); x[7] ^= Rotate(x[3] + x[15], 9);
                    x[11] ^= Rotate(x[7] + x[3], 13); x[15] ^= Rotate(x[11] + x[7], 18);
                    x[1] ^= Rotate(x[0] + x[3], 7); x[2] ^= Rotate(x[1] + x[0], 9);
                    x[3] ^= Rotate(x[2] + x[1], 13); x[0] ^= Rotate(x[3] + x[2], 18);
                    x[6] ^= Rotate(x[5] + x[4], 7); x[7] ^= Rotate(x[6] + x[5], 9);
                    x[4] ^= Rotate(x[7] + x[6], 13); x[5] ^= Rotate(x[4] + x[7], 18);
                    x[11] ^= Rotate(x[10] + x[9], 7); x[8] ^= Rotate(x[11] + x[10], 9);
                    x[9] ^= Rotate(x[8] + x[11], 13); x[10] ^= Rotate(x[9] + x[8], 18);
                    x[12] ^= Rotate(x[15] + x[14], 7); x[13] ^= Rotate(x[12] + x[15], 9);
                    x[14] ^= Rotate(x[13] + x[12], 13); x[15] ^= Rotate(x[14] + x[13], 18);
                }
                for (var i = 0; i < 16; i++)
                    block[i] += x[i];
            }
        }

        private static uint Rotate(uint value, int bits) => (value << bits) | (value >> (32 - bits));
    }
}
