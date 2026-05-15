using System;
using System.Linq;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Services.Ai;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Canvas.TextEditor.Tests.Ai
{
    public sealed class AiSermonHistoryStoreTests
    {
        [Fact]
        public async Task SaveConversationAsync_GroupsSessionsBySpeakerAndSoftDeletesMessages()
        {
            string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"canvas-ai-history-{Guid.NewGuid():N}.db");
            try
            {
                using var context = new CanvasDbContext(dbPath);
                context.Database.EnsureCreated();
                context.EnsureAiSermonSchemaExists();
                var store = new AiSermonHistoryStore(context);

                var speakerA = await store.GetOrCreateSpeakerAsync("讲师A");
                var speakerB = await store.GetOrCreateSpeakerAsync("讲师B");
                var sessionA = await store.CreateSessionAsync(speakerA.Id, projectId: 10, title: "主日讲章");
                var sessionB = await store.CreateSessionAsync(speakerB.Id, projectId: 11, title: "查经分享");
                await store.UpdateSessionSpeakerAsync(sessionA.Id, speakerB.Id);

                var message = await store.AppendMessageAsync(sessionA.Id, "assistant", "AI理解到罗马书八章", "asr");
                await store.AppendMessageAsync(sessionB.Id, "assistant", "AI理解到约翰福音三章", "asr");

                await store.DeleteMessageAsync(message.Id);
                var groups = await store.GetSessionGroupsBySpeakerAsync();

                Assert.Equal(new[] { "讲师B" }, groups.Select(g => g.SpeakerName).OrderBy(n => n).ToArray());
                Assert.Equal(2, groups.Single(g => g.SpeakerName == "讲师B").Sessions.Count);
                Assert.Contains(
                    groups.Single(g => g.SpeakerName == "讲师B").Sessions.SelectMany(s => s.Messages),
                    m => m.Content.Contains("约翰福音", StringComparison.Ordinal));
            }
            finally
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }
}
