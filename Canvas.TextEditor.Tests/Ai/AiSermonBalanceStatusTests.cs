using ImageColorChanger.Services.Ai;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonBalanceStatusTests
    {
        [Fact]
        public void FormatBalanceStatus_ShowsAccountBalanceAndSessionCost()
        {
            var start = new DeepSeekBalanceSnapshot { IsAvailable = true, Currency = "CNY", TotalBalance = 110.25m };
            var current = new DeepSeekBalanceSnapshot { IsAvailable = true, Currency = "CNY", TotalBalance = 109.80m };

            string status = AiSermonConversationCoordinator.FormatBalanceStatus(start, current);

            Assert.Equal("余额：109.80，消耗：0.45", status);
        }

        [Fact]
        public void FormatBalanceStatus_HandlesMissingBaseline()
        {
            var current = new DeepSeekBalanceSnapshot { IsAvailable = true, Currency = "CNY", TotalBalance = 109.80m };

            string status = AiSermonConversationCoordinator.FormatBalanceStatus(null, current);

            Assert.Equal("余额：109.80，消耗：待计算", status);
        }
    }
}
