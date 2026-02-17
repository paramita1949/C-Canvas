using System;
using System.Security.Cryptography;
using System.Text;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthTokenGuard
    {
        internal readonly struct TokenBundle
        {
            public TokenBundle(string token1, string token2, long checksum)
            {
                Token1 = token1;
                Token2 = token2;
                Checksum = checksum;
            }

            public string Token1 { get; }
            public string Token2 { get; }
            public long Checksum { get; }
        }

        public TokenBundle Generate(string username, string token, DateTime? expiresAt)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(token))
            {
                return new TokenBundle(null, null, 0);
            }

            string token1;
            using (var sha256 = SHA256.Create())
            {
                var bytes1 = Encoding.UTF8.GetBytes($"{username}:{token}:TOKEN1");
                var hash1 = sha256.ComputeHash(bytes1);
                token1 = Convert.ToBase64String(hash1);
            }

            string token2;
            using (var sha256 = SHA256.Create())
            {
                var bytes2 = Encoding.UTF8.GetBytes($"{username}:{expiresAt?.Ticks}:TOKEN2");
                var hash2 = sha256.ComputeHash(bytes2);
                token2 = Convert.ToBase64String(hash2);
            }

            long checksum = token1.GetHashCode() ^ token2.GetHashCode();
            return new TokenBundle(token1, token2, checksum);
        }

        public bool Validate(
            string username,
            string token,
            DateTime? expiresAt,
            string token1,
            string token2,
            long checksum)
        {
            if (string.IsNullOrEmpty(token1) || string.IsNullOrEmpty(token2))
            {
                return false;
            }

            long expectedChecksum = token1.GetHashCode() ^ token2.GetHashCode();
            if (checksum != expectedChecksum)
            {
                return false;
            }

            var expected = Generate(username, token, expiresAt);
            return string.Equals(token1, expected.Token1, StringComparison.Ordinal) &&
                   string.Equals(token2, expected.Token2, StringComparison.Ordinal);
        }
    }
}
