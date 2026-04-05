namespace ImageColorChanger.Services.LiveCaption
{
    internal readonly struct LiveCaptionAsrText
    {
        public LiveCaptionAsrText(string text, bool isFinal)
        {
            Text = text ?? string.Empty;
            IsFinal = isFinal;
        }

        public string Text { get; }

        public bool IsFinal { get; }
    }
}

