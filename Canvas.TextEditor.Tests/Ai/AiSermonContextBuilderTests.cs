using System.Collections.Generic;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.Ai;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonContextBuilderTests
    {
        [Fact]
        public void Build_MultipleSlides_CreatesCompactRuntimeContext()
        {
            var project = new TextProject
            {
                Id = 7,
                Name = "主日信息",
                CanvasWidth = 1920,
                CanvasHeight = 1080
            };
            var slides = new List<Slide>
            {
                new()
                {
                    Id = 2,
                    SortOrder = 1,
                    Title = "回应",
                    Elements = new List<TextElement>
                    {
                        new() { Id = 10, ZIndex = 1, Content = "罗马书八章二十八节 万事都互相效力" }
                    }
                },
                new()
                {
                    Id = 1,
                    SortOrder = 0,
                    Title = "主题",
                    Elements = new List<TextElement>
                    {
                        new() { Id = 11, ZIndex = 1, Content = "今天主题：在患难中依靠神" }
                    }
                }
            };

            var envelope = AiSermonContextBuilder.Build(project, slides);

            Assert.Contains("幻灯片数量：2", envelope.ContextText);
            Assert.Contains("1. 主题", envelope.ContextText);
            Assert.Contains("2. 回应", envelope.ContextText);
            Assert.Contains("稳定上下文摘要", envelope.RuntimeContextText);
            Assert.Contains("幻灯片索引：", envelope.RuntimeContextText);
            Assert.Contains("罗马书八章二十八节", envelope.RuntimeContextText);
            Assert.True(envelope.RuntimeContextText.Length <= 4000);
        }
    }
}
