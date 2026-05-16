namespace ImageColorChanger.Services.Ai
{
    public sealed class DeepSeekBalanceSnapshot
    {
        public bool IsAvailable { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal TotalBalance { get; set; }
    }
}
