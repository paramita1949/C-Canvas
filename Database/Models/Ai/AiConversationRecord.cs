using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models.Ai
{
    [Table("ai_conversation_records")]
    public sealed class AiConversationRecord
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("session_id")]
        public int SessionId { get; set; }

        [Required]
        [Column("role")]
        public string Role { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Column("content")]
        public string Content { get; set; } = string.Empty;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; }
    }
}
