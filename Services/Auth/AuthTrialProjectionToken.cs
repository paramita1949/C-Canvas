using System;
using System.Security.Cryptography;
using System.Text;

namespace ImageColorChanger.Services.Auth
{
    internal sealed class AuthTrialProjectionToken
    {
        private const string SecretSalt1 = "CanvasCast_Trial_Projection_Key_2024";
        private const string SecretSalt2 = "AntiCrack_Protection_Layer_SHA256";

        public string Generate(long startTick, int durationSeconds, int maxDurationSeconds, string machineName, string userName)
        {
            try
            {
                int validDuration = Math.Min(durationSeconds, maxDurationSeconds);
                var data = $"{SecretSalt1}:{startTick}:{validDuration}:{machineName}:{userName}:{SecretSalt2}";

                using (var sha256 = SHA256.Create())
                {
                    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                    return Convert.ToBase64String(hashBytes);
                }
            }
            catch
            {
                return null;
            }
        }

        public bool Validate(
            string currentToken,
            long startTick,
            int durationSeconds,
            int maxDurationSeconds,
            string machineName,
            string userName)
        {
            if (string.IsNullOrEmpty(currentToken))
            {
                return false;
            }

            var expectedToken = Generate(startTick, durationSeconds, maxDurationSeconds, machineName, userName);
            return string.Equals(currentToken, expectedToken, StringComparison.Ordinal);
        }
    }
}
