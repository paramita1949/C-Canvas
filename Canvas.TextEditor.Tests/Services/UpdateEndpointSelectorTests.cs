using System;
using ImageColorChanger.Services;

namespace Canvas.TextEditor.Tests.Services
{
    public sealed class UpdateEndpointSelectorTests
    {
        private const string A = "https://a.upgrade.test/raw";
        private const string B = "https://b.upgrade.test/raw";
        private const string C = "https://c.upgrade.test/raw";

        [Fact]
        public void Order_WithoutHealthData_KeepsConfiguredOrder()
        {
            var selector = new UpdateEndpointSelector(() => DateTimeOffset.UtcNow);

            Assert.Equal(new[] { A, B, C }, selector.Order(new[] { A, B, C }));
        }

        [Fact]
        public void Order_PutsRecentlySuccessfulAddressFirst()
        {
            var selector = new UpdateEndpointSelector(() => DateTimeOffset.UtcNow);
            selector.MarkSuccess(C);

            Assert.Equal(new[] { C, A, B }, selector.Order(new[] { A, B, C }));
            Assert.Equal(C, selector.Preferred);
        }

        [Fact]
        public void Order_PushesFailedAddressToTheEndDuringCooldown()
        {
            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            var selector = new UpdateEndpointSelector(() => now, TimeSpan.FromMinutes(10));

            selector.MarkFailure(A);

            Assert.True(selector.IsCoolingDown(A));
            Assert.Equal(new[] { B, C, A }, selector.Order(new[] { A, B, C }));
        }

        [Fact]
        public void Order_RestoresConfiguredPositionAfterCooldownExpires()
        {
            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            var selector = new UpdateEndpointSelector(() => now, TimeSpan.FromMinutes(10));
            selector.MarkFailure(A);

            now = now.AddMinutes(11);

            Assert.False(selector.IsCoolingDown(A));
            Assert.Equal(new[] { A, B, C }, selector.Order(new[] { A, B, C }));
        }

        [Fact]
        public void MarkFailure_ClearsPreferredSoFailingAddressIsNotRetriedFirst()
        {
            var selector = new UpdateEndpointSelector(() => DateTimeOffset.UtcNow);
            selector.MarkSuccess(B);
            Assert.Equal(new[] { B, A, C }, selector.Order(new[] { A, B, C }));

            selector.MarkFailure(B);

            Assert.Equal(string.Empty, selector.Preferred);
            Assert.Equal(new[] { A, C, B }, selector.Order(new[] { A, B, C }));
        }

        [Fact]
        public void MarkSuccess_RemovesCooldownAndPromotesAddress()
        {
            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            var selector = new UpdateEndpointSelector(() => now, TimeSpan.FromMinutes(10));
            selector.MarkFailure(B);

            selector.MarkSuccess(B);

            Assert.False(selector.IsCoolingDown(B));
            Assert.Equal(new[] { B, A, C }, selector.Order(new[] { A, B, C }));
        }

        [Fact]
        public void Order_AllAddressesCoolingDown_StillReturnsEveryAddress()
        {
            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            var selector = new UpdateEndpointSelector(() => now, TimeSpan.FromMinutes(10));
            selector.MarkFailure(A);
            selector.MarkFailure(B);
            selector.MarkFailure(C);

            var ordered = selector.Order(new[] { A, B, C });

            Assert.Equal(new[] { A, B, C }, ordered);
        }

        [Fact]
        public void Order_NullOrEmptyInput_ReturnsEmptyAndNeverThrows()
        {
            var selector = new UpdateEndpointSelector(() => DateTimeOffset.UtcNow);

            Assert.Empty(selector.Order(null));
            Assert.Empty(selector.Order(new[] { " ", "" }));
        }

        [Fact]
        public void Reset_ClearsPreferredAndCooldown()
        {
            var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
            var selector = new UpdateEndpointSelector(() => now, TimeSpan.FromMinutes(10));
            selector.MarkSuccess(B);
            selector.MarkFailure(A);

            selector.Reset();

            Assert.Equal(string.Empty, selector.Preferred);
            Assert.False(selector.IsCoolingDown(A));
            Assert.Equal(new[] { A, B, C }, selector.Order(new[] { A, B, C }));
        }
    }
}
