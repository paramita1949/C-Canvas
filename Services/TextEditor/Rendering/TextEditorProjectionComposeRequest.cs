using System;
using System.Windows;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorProjectionComposeRequest
    {
        public bool IsProjectionActive { get; init; }

        public bool AnimationEnabled { get; init; }

        public double AnimationOpacity { get; init; } = 0.1;

        public int AnimationDurationMs { get; init; } = 800;

        public Func<UIElement> GetProjectionContainer { get; init; }

        public Action UpdateProjectionContent { get; init; }

        public Action ShowProjectionNotActiveHint { get; init; }
    }
}
