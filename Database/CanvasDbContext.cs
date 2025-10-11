using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Database
{
    /// <summary>
    /// Canvas Cast 数据库上下文
    /// </summary>
    public class CanvasDbContext : DbContext
    {
        /// <summary>
        /// 数据库文件路径
        /// </summary>
        private readonly string _dbPath;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dbPath">数据库文件路径（必须提供完整路径）</param>
        public CanvasDbContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        /// <summary>
        /// 文件夹表
        /// </summary>
        public DbSet<Folder> Folders { get; set; }

        /// <summary>
        /// 媒体文件表（对应Python的images表）
        /// </summary>
        public DbSet<MediaFile> MediaFiles { get; set; }

        /// <summary>
        /// 通用设置表
        /// </summary>
        public DbSet<Setting> Settings { get; set; }

        /// <summary>
        /// UI设置表
        /// </summary>
        public DbSet<UISetting> UISettings { get; set; }

        /// <summary>
        /// 关键帧表
        /// </summary>
        public DbSet<Keyframe> Keyframes { get; set; }

        /// <summary>
        /// 关键帧时间记录表
        /// </summary>
        public DbSet<KeyframeTiming> KeyframeTimings { get; set; }

        /// <summary>
        /// 原图标记表
        /// </summary>
        public DbSet<OriginalMark> OriginalMarks { get; set; }

        /// <summary>
        /// 原图模式时间记录表
        /// </summary>
        public DbSet<OriginalModeTiming> OriginalModeTimings { get; set; }

        /// <summary>
        /// 手动排序文件夹表
        /// </summary>
        public DbSet<ManualSortFolder> ManualSortFolders { get; set; }

        /// <summary>
        /// 图片显示位置表
        /// </summary>
        public DbSet<ImageDisplayLocation> ImageDisplayLocations { get; set; }

        /// <summary>
        /// 配置数据库连接
        /// </summary>
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite($"Data Source={_dbPath}");
                
                // 开发环境下启用敏感数据日志
                #if DEBUG
                optionsBuilder.EnableSensitiveDataLogging();
                optionsBuilder.EnableDetailedErrors();
                #endif
            }
        }

        /// <summary>
        /// 配置实体模型
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ========== 文件夹表配置 ==========
            modelBuilder.Entity<Folder>(entity =>
            {
                // 路径唯一索引
                entity.HasIndex(e => e.Path).IsUnique();
                // 排序索引
                entity.HasIndex(e => e.OrderIndex).HasDatabaseName("idx_order_folders");
            });

            // ========== 媒体文件表配置 ==========
            modelBuilder.Entity<MediaFile>(entity =>
            {
                // 路径唯一索引
                entity.HasIndex(e => e.Path).IsUnique();
                // 文件夹ID索引
                entity.HasIndex(e => e.FolderId).HasDatabaseName("idx_folder_id");
                // 排序索引
                entity.HasIndex(e => e.OrderIndex).HasDatabaseName("idx_order_images");
                // 名称索引
                entity.HasIndex(e => e.Name).HasDatabaseName("idx_images_name");
                // 文件夹+排序复合索引
                entity.HasIndex(e => new { e.FolderId, e.OrderIndex }).HasDatabaseName("idx_images_folder_order");
                // 文件类型索引
                entity.HasIndex(e => e.FileTypeString).HasDatabaseName("idx_images_file_type");

                // 外键关系：媒体文件 -> 文件夹（可选）
                entity.HasOne(m => m.Folder)
                    .WithMany(f => f.MediaFiles)
                    .HasForeignKey(m => m.FolderId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ========== 关键帧表配置 ==========
            modelBuilder.Entity<Keyframe>(entity =>
            {
                // 图片ID索引
                entity.HasIndex(e => e.ImageId).HasDatabaseName("idx_keyframes_image");
                // 排序索引
                entity.HasIndex(e => e.OrderIndex).HasDatabaseName("idx_keyframes_order");

                // 外键关系：关键帧 -> 媒体文件
                entity.HasOne(k => k.MediaFile)
                    .WithMany(m => m.Keyframes)
                    .HasForeignKey(k => k.ImageId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========== 关键帧时间记录表配置 ==========
            modelBuilder.Entity<KeyframeTiming>(entity =>
            {
                // 图片ID索引
                entity.HasIndex(e => e.ImageId).HasDatabaseName("idx_timing_image");
                // 图片ID+序列顺序复合索引
                entity.HasIndex(e => new { e.ImageId, e.SequenceOrder }).HasDatabaseName("idx_timing_sequence");

                // 外键关系：时间记录 -> 媒体文件
                entity.HasOne(t => t.MediaFile)
                    .WithMany()
                    .HasForeignKey(t => t.ImageId)
                    .OnDelete(DeleteBehavior.Cascade);

                // 外键关系：时间记录 -> 关键帧
                entity.HasOne(t => t.Keyframe)
                    .WithMany(k => k.Timings)
                    .HasForeignKey(t => t.KeyframeId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========== 原图标记表配置 ==========
            modelBuilder.Entity<OriginalMark>(entity =>
            {
                // 项目类型+项目ID唯一索引
                entity.HasIndex(e => new { e.ItemTypeString, e.ItemId })
                    .IsUnique()
                    .HasDatabaseName("idx_original_marks");
            });

            // ========== 原图模式时间记录表配置 ==========
            modelBuilder.Entity<OriginalModeTiming>(entity =>
            {
                // 基础图片ID索引
                entity.HasIndex(e => e.BaseImageId).HasDatabaseName("idx_original_base");
                // 基础图片ID+序列顺序复合索引
                entity.HasIndex(e => new { e.BaseImageId, e.SequenceOrder }).HasDatabaseName("idx_original_sequence");
            });

            // ========== 手动排序文件夹表配置 ==========
            modelBuilder.Entity<ManualSortFolder>(entity =>
            {
                // 文件夹ID索引
                entity.HasIndex(e => e.FolderId).HasDatabaseName("idx_manual_sort");

                // 外键关系：手动排序 -> 文件夹
                entity.HasOne(m => m.Folder)
                    .WithOne(f => f.ManualSortFolder)
                    .HasForeignKey<ManualSortFolder>(m => m.FolderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========== 图片显示位置表配置 ==========
            modelBuilder.Entity<ImageDisplayLocation>(entity =>
            {
                // 图片ID+位置类型+文件夹ID唯一索引
                entity.HasIndex(e => new { e.ImageId, e.LocationTypeString, e.FolderId })
                    .IsUnique();

                // 外键关系：显示位置 -> 媒体文件
                entity.HasOne(l => l.MediaFile)
                    .WithMany(m => m.DisplayLocations)
                    .HasForeignKey(l => l.ImageId)
                    .OnDelete(DeleteBehavior.Cascade);

                // 外键关系：显示位置 -> 文件夹（可选）
                entity.HasOne(l => l.Folder)
                    .WithMany()
                    .HasForeignKey(l => l.FolderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        /// <summary>
        /// 初始化数据库（创建表和索引）
        /// </summary>
        public void InitializeDatabase()
        {
            // 确保数据库已创建
            Database.EnsureCreated();

            // 执行SQLite性能优化配置
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
            Database.ExecuteSqlRaw("PRAGMA cache_size=-10000;");
            Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
            Database.ExecuteSqlRaw("PRAGMA temp_store=MEMORY;");
        }
    }
}

