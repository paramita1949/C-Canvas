using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Ai
{
    [Table("ai_speakers")]
    public sealed class AiSpeakerProfile
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("style_summary")]
        public string StyleSummary { get; set; } = string.Empty;

        [Column("common_books")]
        public string CommonBooks { get; set; } = string.Empty;

        [Column("common_phrases")]
        public string CommonPhrases { get; set; } = string.Empty;

        [Column("asr_confusions")]
        public string AsrConfusions { get; set; } = string.Empty;

        [Column("is_archived")]
        public bool IsArchived { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
