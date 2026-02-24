using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ImageColorChanger.Services.TextEditor.Rendering
{
    public sealed class TextEditorProjectionComposer : ITextEditorProjectionComposer
    {
        public void Compose(TextEditorProjectionComposeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (!request.IsProjectionActive)
            {
                request.ShowProjectionNotActiveHint?.Invoke();
                return;
            }

            if (!request.AnimationEnabled)
            {
                request.UpdateProjectionContent?.Invoke();
                return;
            }

            var projectionContainer = request.GetProjectionContainer?.Invoke();
            if (projectionContainer == null)
            {
                request.UpdateProjectionContent?.Invoke();
                return;
            }

            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = request.AnimationOpacity,
                Duration = TimeSpan.FromMilliseconds(Math.Max(0, request.AnimationDurationMs / 2)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            fadeOutAnimation.Completed += (_, _) =>
            {
                request.UpdateProjectionContent?.Invoke();
                FadeInProjection(projectionContainer, request.AnimationOpacity, request.AnimationEnabled, request.AnimationDurationMs);
            };

            projectionContainer.BeginAnimation(UIElement.OpacityProperty, fadeOutAnimation);
        }

        private static void FadeInProjection(UIElement projectionContainer, double animationOpacity, bool animationEnabled, int animationDurationMs)
        {
            if (projectionContainer == null)
            {
                return;
            }

            if (!animationEnabled)
            {
                projectionContainer.Opacity = 1.0;
                projectionContainer.BeginAnimation(UIElement.OpacityProperty, null);
                return;
            }

            projectionContainer.Opacity = animationOpacity;

            var fadeInAnimation = new DoubleAnimation
            {
                From = animationOpacity,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(Math.Max(0, animationDurationMs / 2)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            projectionContainer.BeginAnimation(UIElement.OpacityProperty, fadeInAnimation);
        }
    }
}
