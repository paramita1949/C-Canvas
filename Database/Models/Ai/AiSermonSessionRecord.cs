using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Ai
{
    [Table("ai_sermon_sessions")]
    public sealed class AiSermonSessionRecord
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("speaker_id")]
        public int SpeakerId { get; set; }

        [Column("project_id")]
        public int ProjectId { get; set; }

        [Required]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [Column("summary")]
        public string Summary { get; set; } = string.Empty;

        [Column("confirmed_scriptures")]
        public string ConfirmedScriptures { get; set; } = string.Empty;

        [Column("output_mode")]
        public string OutputMode { get; set; } = "concise";

        [Column("started_at")]
        public DateTime StartedAt { get; set; } = DateTime.Now;

        [Column("ended_at")]
        public DateTime? EndedAt { get; set; }

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }
    }
}
