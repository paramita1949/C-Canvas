using System;
using System.Reflection;
using ImageColorChanger.Services;
using Xunit;

namespace ImageColorChanger.CanvasTextEditor.Tests.Services
{
    public sealed class AuthServiceRemainingDaysTests
    {
        [Fact]
        public void RemainingDays_Should_RecalculateFromExpiresAt_WhenCachedValueIsStale()
        {
            var authService = AuthService.Instance;
            var expiresAt = DateTime.Now.AddDays(6.1);

            var snapshot = AuthServiceReflectionSnapshot.Capture(authService);
            try
            {
                AuthServiceReflectionSnapshot.SetField(authService, "_expiresAt", expiresAt);
                AuthServiceReflectionSnapshot.SetField(authService, "_remainingDays", 8);
                AuthServiceReflectionSnapshot.SetField(authService, "_lastServerTime", null);
                AuthServiceReflectionSnapshot.SetField(authService, "_lastLocalTime", null);
                AuthServiceReflectionSnapshot.SetField(authService, "_lastTickCount", 0L);

                Assert.Equal(7, authService.RemainingDays);
            }
            finally
            {
                snapshot.Restore(authService);
            }
        }

        private sealed class AuthServiceReflectionSnapshot
        {
            private readonly object _expiresAt;
            private readonly object _remainingDays;
            private readonly object _lastServerTime;
            private readonly object _lastLocalTime;
            private readonly object _lastTickCount;

            private AuthServiceReflectionSnapshot(
                object expiresAt,
                object remainingDays,
                object lastServerTime,
                object lastLocalTime,
                object lastTickCount)
            {
                _expiresAt = expiresAt;
                _remainingDays = remainingDays;
                _lastServerTime = lastServerTime;
                _lastLocalTime = lastLocalTime;
                _lastTickCount = lastTickCount;
            }

            public static AuthServiceReflectionSnapshot Capture(AuthService authService)
            {
                return new AuthServiceReflectionSnapshot(
                    GetField(authService, "_expiresAt"),
                    GetField(authService, "_remainingDays"),
                    GetField(authService, "_lastServerTime"),
                    GetField(authService, "_lastLocalTime"),
                    GetField(authService, "_lastTickCount"));
            }

            public void Restore(AuthService authService)
            {
                SetField(authService, "_expiresAt", _expiresAt);
                SetField(authService, "_remainingDays", _remainingDays);
                SetField(authService, "_lastServerTime", _lastServerTime);
                SetField(authService, "_lastLocalTime", _lastLocalTime);
                SetField(authService, "_lastTickCount", _lastTickCount);
            }

            public static object GetField(AuthService authService, string fieldName)
            {
                return GetFieldInfo(fieldName).GetValue(authService);
            }

            public static void SetField(AuthService authService, string fieldName, object value)
            {
                GetFieldInfo(fieldName).SetValue(authService, value);
            }

            private static FieldInfo GetFieldInfo(string fieldName)
            {
                var field = typeof(AuthService).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(field);
                return field;
            }
        }
    }
}
