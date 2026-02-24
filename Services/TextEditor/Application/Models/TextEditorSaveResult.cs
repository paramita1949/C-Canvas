using System;

namespace ImageColorChanger.Services.TextEditor.Application.Models
{
    public sealed class TextEditorSaveResult
    {
        private TextEditorSaveResult(
            bool succeeded,
            SaveTrigger trigger,
            bool textElementsSaved,
            bool additionalStateSaved,
            bool thumbnailSaved,
            string thumbnailPath,
            Exception exception)
        {
            Succeeded = succeeded;
            Trigger = trigger;
            TextElementsSaved = textElementsSaved;
            AdditionalStateSaved = additionalStateSaved;
            ThumbnailSaved = thumbnailSaved;
            ThumbnailPath = thumbnailPath;
            Exception = exception;
        }

        public bool Succeeded { get; }

        public SaveTrigger Trigger { get; }

        public bool TextElementsSaved { get; }

        public bool AdditionalStateSaved { get; }

        public bool ThumbnailSaved { get; }

        public string ThumbnailPath { get; }

        public Exception Exception { get; }

        public static TextEditorSaveResult Success(
            SaveTrigger trigger,
            bool textElementsSaved,
            bool additionalStateSaved,
            bool thumbnailSaved,
            string thumbnailPath)
        {
            return new TextEditorSaveResult(
                succeeded: true,
                trigger: trigger,
                textElementsSaved: textElementsSaved,
                additionalStateSaved: additionalStateSaved,
                thumbnailSaved: thumbnailSaved,
                thumbnailPath: thumbnailPath,
                exception: null);
        }

        public static TextEditorSaveResult Failure(
            SaveTrigger trigger,
            Exception exception,
            bool textElementsSaved,
            bool additionalStateSaved,
            bool thumbnailSaved,
            string thumbnailPath)
        {
            return new TextEditorSaveResult(
                succeeded: false,
                trigger: trigger,
                textElementsSaved: textElementsSaved,
                additionalStateSaved: additionalStateSaved,
                thumbnailSaved: thumbnailSaved,
                thumbnailPath: thumbnailPath,
                exception: exception);
        }
    }
}
