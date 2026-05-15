using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models.Ai;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Services.Ai
{
    public sealed class AiSermonHistoryStore
    {
        private readonly CanvasDbContext _context;
        private readonly SemaphoreSlim _dbLock = new(1, 1);

        public AiSermonHistoryStore(CanvasDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public async Task<AiSpeakerProfile> GetOrCreateSpeakerAsync(string name)
        {
            string normalized = string.IsNullOrWhiteSpace(name) ? "未标记讲师" : name.Trim();
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var existing = await _context.AiSpeakers
                    .FirstOrDefaultAsync(s => s.Name == normalized && !s.IsArchived)
                    .ConfigureAwait(false);
                if (existing != null)
                {
                    return existing;
                }

                var speaker = new AiSpeakerProfile
                {
                    Name = normalized,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                _context.AiSpeakers.Add(speaker);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return speaker;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<AiSermonSessionRecord> CreateSessionAsync(int speakerId, int projectId, string title, string outputMode = "concise")
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var session = new AiSermonSessionRecord
                {
                    SpeakerId = speakerId,
                    ProjectId = projectId,
                    Title = string.IsNullOrWhiteSpace(title) ? "未命名讲章" : title.Trim(),
                    OutputMode = string.IsNullOrWhiteSpace(outputMode) ? "concise" : outputMode.Trim(),
                    StartedAt = DateTime.Now
                };
                _context.AiSermonSessions.Add(session);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return session;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<AiConversationRecord> AppendMessageAsync(int sessionId, string role, string content, string name = "")
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var record = new AiConversationRecord
                {
                    SessionId = sessionId,
                    Role = string.IsNullOrWhiteSpace(role) ? "user" : role.Trim(),
                    Name = name ?? string.Empty,
                    Content = content ?? string.Empty,
                    CreatedAt = DateTime.Now
                };
                _context.AiConversationRecords.Add(record);
                await _context.SaveChangesAsync().ConfigureAwait(false);
                return record;
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task DeleteMessageAsync(int messageId)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var record = await _context.AiConversationRecords.FindAsync(messageId).ConfigureAwait(false);
                if (record == null)
                {
                    return;
                }

                record.IsDeleted = true;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task DeleteSessionAsync(int sessionId)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var session = await _context.AiSermonSessions.FindAsync(sessionId).ConfigureAwait(false);
                if (session == null)
                {
                    return;
                }

                session.IsDeleted = true;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task UpdateSessionSpeakerAsync(int sessionId, int speakerId)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var session = await _context.AiSermonSessions.FindAsync(sessionId).ConfigureAwait(false);
                if (session == null)
                {
                    return;
                }

                session.SpeakerId = speakerId;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task UpdateSessionOutputModeAsync(int sessionId, string outputMode)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var session = await _context.AiSermonSessions.FindAsync(sessionId).ConfigureAwait(false);
                if (session == null)
                {
                    return;
                }

                session.OutputMode = string.Equals(outputMode, "detailed", StringComparison.OrdinalIgnoreCase)
                    ? "detailed"
                    : "concise";
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task UpdateSessionSummaryAsync(int sessionId, string summary)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var session = await _context.AiSermonSessions.FindAsync(sessionId).ConfigureAwait(false);
                if (session == null)
                {
                    return;
                }

                session.Summary = summary ?? string.Empty;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task UpdateSpeakerStyleSummaryAsync(int speakerId, string styleSummary)
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var speaker = await _context.AiSpeakers.FindAsync(speakerId).ConfigureAwait(false);
                if (speaker == null)
                {
                    return;
                }

                speaker.StyleSummary = styleSummary ?? string.Empty;
                speaker.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<IReadOnlyList<string>> GetSpeakerNamesAsync()
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                return await _context.AiSpeakers
                    .Where(s => !s.IsArchived)
                    .OrderBy(s => s.Name)
                    .Select(s => s.Name)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task ArchiveSpeakerAsync(string speakerName)
        {
            string normalized = string.IsNullOrWhiteSpace(speakerName) ? string.Empty : speakerName.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "未标记讲师", StringComparison.Ordinal))
            {
                return;
            }

            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var speaker = await _context.AiSpeakers
                    .FirstOrDefaultAsync(s => s.Name == normalized && !s.IsArchived)
                    .ConfigureAwait(false);
                if (speaker == null)
                {
                    return;
                }

                var fallback = await _context.AiSpeakers
                    .FirstOrDefaultAsync(s => s.Name == "未标记讲师" && !s.IsArchived)
                    .ConfigureAwait(false);
                if (fallback == null)
                {
                    fallback = new AiSpeakerProfile
                    {
                        Name = "未标记讲师",
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now
                    };
                    _context.AiSpeakers.Add(fallback);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                }

                var sessions = await _context.AiSermonSessions
                    .Where(session => session.SpeakerId == speaker.Id && !session.IsDeleted)
                    .ToListAsync()
                    .ConfigureAwait(false);
                foreach (var session in sessions)
                {
                    session.SpeakerId = fallback.Id;
                }

                speaker.IsArchived = true;
                speaker.UpdatedAt = DateTime.Now;
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            finally
            {
                _dbLock.Release();
            }
        }

        public async Task<IReadOnlyList<AiSpeakerSessionGroup>> GetSessionGroupsBySpeakerAsync()
        {
            await _dbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var speakers = await _context.AiSpeakers
                    .Where(s => !s.IsArchived)
                    .OrderBy(s => s.Name)
                    .ToListAsync()
                    .ConfigureAwait(false);
                var sessions = await _context.AiSermonSessions
                    .Where(s => !s.IsDeleted)
                    .OrderByDescending(s => s.StartedAt)
                    .ToListAsync()
                    .ConfigureAwait(false);
                var messages = await _context.AiConversationRecords
                    .Where(m => !m.IsDeleted)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return speakers
                    .Select(speaker => new AiSpeakerSessionGroup
                    {
                        SpeakerId = speaker.Id,
                        SpeakerName = speaker.Name,
                        StyleSummary = speaker.StyleSummary,
                        Sessions = sessions
                            .Where(session => session.SpeakerId == speaker.Id)
                            .Select(session => new AiSermonSessionHistory
                            {
                                Id = session.Id,
                                Title = session.Title,
                                ProjectId = session.ProjectId,
                                Summary = session.Summary,
                                StartedAt = session.StartedAt,
                                Messages = messages
                                    .Where(message => message.SessionId == session.Id)
                                    .Select(message => new AiConversationHistoryMessage
                                    {
                                        Id = message.Id,
                                        Role = message.Role,
                                        Name = message.Name,
                                        Content = message.Content,
                                        CreatedAt = message.CreatedAt
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .Where(group => group.Sessions.Count > 0)
                    .ToList();
            }
            finally
            {
                _dbLock.Release();
            }
        }
    }

    public sealed class AiSpeakerSessionGroup
    {
        public int SpeakerId { get; init; }
        public string SpeakerName { get; init; } = string.Empty;
        public string StyleSummary { get; init; } = string.Empty;
        public IReadOnlyList<AiSermonSessionHistory> Sessions { get; init; } = Array.Empty<AiSermonSessionHistory>();
    }

    public sealed class AiSermonSessionHistory
    {
        public int Id { get; init; }
        public int ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public DateTime StartedAt { get; init; }
        public IReadOnlyList<AiConversationHistoryMessage> Messages { get; init; } = Array.Empty<AiConversationHistoryMessage>();
    }

    public sealed class AiConversationHistoryMessage
    {
        public int Id { get; init; }
        public string Role { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }
}
