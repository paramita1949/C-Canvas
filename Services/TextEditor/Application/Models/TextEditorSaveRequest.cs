using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Services.TextEditor.Models;

namespace ImageColorChanger.Services.TextEditor.Application.Models
{
    public sealed class TextEditorSaveRequest
    {
        public SaveTrigger Trigger { get; init; } = SaveTrigger.Unknown;

        public IReadOnlyCollection<TextBoxSnapshot> Snapshots { get; init; } = Array.Empty<TextBoxSnapshot>();

        public Func<CancellationToken, Task> PersistAdditionalStateAsync { get; init; }

        public Func<CancellationToken, Task<string>> SaveThumbnailAsync { get; init; }
    }
}
