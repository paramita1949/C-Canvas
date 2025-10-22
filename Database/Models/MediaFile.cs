using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ImageColorChanger.Database.Models
{
    /// <summary>
    /// 文件类型枚举
    /// </summary>
    public enum FileType
    {
        /// <summary>图片</summary>
        Image,
        /// <summary>视频</summary>
        Video,
        /// <summary>音频</summary>
        Audio
    }

    /// <summary>
    /// 媒体文件实体（对应Python的images表）
    /// </summary>
    [Table("images")]
    public class MediaFile
    {
        /// <summary>
        /// 文件ID
        /// </summary>
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// 文件名（不含扩展名）
        /// </summary>
        [Required]
        [Column("name")]
        public string Name { get; set; }

        /// <summary>
        /// 文件完整路径（唯一）
        /// </summary>
        [Required]
        [Column("path")]
        public string Path { get; set; }

        /// <summary>
        /// 所属文件夹ID（可为NULL，表示根目录）
        /// </summary>
        [Column("folder_id")]
        public int? FolderId { get; set; }

        /// <summary>
        /// 最后修改时间
        /// </summary>
        [Column("last_modified")]
        public DateTime? LastModified { get; set; }

        /// <summary>
        /// 在文件夹内的显示顺序
        /// </summary>
        [Column("order_index")]
        public int? OrderIndex { get; set; }

        /// <summary>
        /// 文件类型：image/video/audio
        /// </summary>
        [Required]
        [Column("file_type")]
        public string FileTypeString { get; set; } = "image";

        /// <summary>
        /// 是否启用合成播放模式（录制完成后自动播放合成）
        /// </summary>
        [Column("composite_playback_enabled")]
        public bool CompositePlaybackEnabled { get; set; } = false;

        /// <summary>
        /// 文件类型枚举（不映射到数据库）
        /// </summary>
        [NotMapped]
        public FileType FileType
        {
            get => FileTypeString?.ToLower() switch
            {
                "video" => FileType.Video,
                "audio" => FileType.Audio,
                _ => FileType.Image
            };
            set => FileTypeString = value switch
            {
                FileType.Video => "video",
                FileType.Audio => "audio",
                _ => "image"
            };
        }

        /// <summary>
        /// 导航属性：所属文件夹
        /// </summary>
        [ForeignKey("FolderId")]
        public virtual Folder Folder { get; set; }

        /// <summary>
        /// 导航属性：关键帧列表
        /// </summary>
        public virtual ICollection<Keyframe> Keyframes { get; set; } = new List<Keyframe>();

        /// <summary>
        /// 导航属性：原图标记
        /// </summary>
        public virtual ICollection<OriginalMark> OriginalMarks { get; set; } = new List<OriginalMark>();

        /// <summary>
        /// 导航属性：显示位置
        /// </summary>
        public virtual ICollection<ImageDisplayLocation> DisplayLocations { get; set; } = new List<ImageDisplayLocation>();
    }
}

