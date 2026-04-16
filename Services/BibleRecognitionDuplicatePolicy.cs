namespace ImageColorChanger.Services
{
    internal static class BibleRecognitionDuplicatePolicy
    {
        internal static bool IsDuplicateReference(BibleSpeechReference previous, BibleSpeechReference current)
        {
            return previous.BookId == current.BookId &&
                   previous.Chapter == current.Chapter &&
                   previous.StartVerse == current.StartVerse &&
                   previous.EndVerse == current.EndVerse;
        }
    }
}
