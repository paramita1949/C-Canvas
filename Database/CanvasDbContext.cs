using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ImageColorChanger.Database.Models;

namespace ImageColorChanger.Database
{
    /// <summary>
    /// 日期时间转换器（兼容多种格式）
    /// </summary>
    public class SqliteDateTimeConverter : ValueConverter<DateTime, string>
    {
        public SqliteDateTimeConverter()
            : base(
                v => v.ToString("yyyy-MM-dd HH:mm:ss"),
                v => ParseDateTime(v))
        {
        }

        private static DateTime ParseDateTime(string value)
        {
            if (string.IsNullOrEmpty(value))
                return DateTime.Now;

            // 尝试解析多种格式
            if (DateTime.TryParse(value, out var result))
                return result;

            return DateTime.Now;
        }
    }

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
        /// 手动排序文件夹表
        /// </summary>
        public DbSet<ManualSortFolder> ManualSortFolders { get; set; }

        /// <summary>
        /// 原图模式时间记录表
        /// </summary>
        public DbSet<OriginalModeTiming> OriginalModeTimings { get; set; }

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

                // 配置 CreatedAt 字段的类型转换（SQLite 使用 TEXT 存储日期时间）
                entity.Property(e => e.CreatedAt)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

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

                // 配置 CreatedAt 字段的类型转换
                entity.Property(e => e.CreatedAt)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());
            });

            // ========== 手动排序文件夹表配置 ==========
            modelBuilder.Entity<ManualSortFolder>(entity =>
            {
                // 主键：FolderId
                entity.HasKey(e => e.FolderId);
                
                // 文件夹ID索引
                entity.HasIndex(e => e.FolderId).HasDatabaseName("idx_manual_sort");

                // 外键关系：手动排序 -> 文件夹
                entity.HasOne(m => m.Folder)
                    .WithOne(f => f.ManualSortFolder)
                    .HasForeignKey<ManualSortFolder>(m => m.FolderId)
                    .OnDelete(DeleteBehavior.Cascade);
                    
                // 默认值
                entity.Property(e => e.IsManualSort)
                    .HasDefaultValue(false);

                entity.Property(e => e.LastManualSortTime)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");
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
            try
            {
                // System.Diagnostics.Debug.WriteLine($"🔧 开始初始化数据库: {_dbPath}");
                
                // 确保数据库已创建
                Database.EnsureCreated();
                // System.Diagnostics.Debug.WriteLine($"✅ Database.EnsureCreated() 完成");

                // 检查并创建关键帧表（兼容旧数据库）
                EnsureKeyframesTableExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureKeyframesTableExists() 完成");

                // 执行SQLite性能优化配置
                Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
                Database.ExecuteSqlRaw("PRAGMA cache_size=-10000;");
                Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
                Database.ExecuteSqlRaw("PRAGMA temp_store=MEMORY;");
                
                // System.Diagnostics.Debug.WriteLine($"✅ 数据库初始化完成");
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 数据库初始化失败: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// 确保关键帧表存在（兼容旧数据库）
        /// </summary>
        private void EnsureKeyframesTableExists()
        {
            try
            {
                // System.Diagnostics.Debug.WriteLine("🔍 开始检查关键帧表...");
                
                // 检查 keyframes 表是否存在
                var connection = Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                    // System.Diagnostics.Debug.WriteLine("📂 数据库连接已打开");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='keyframes'";
                    var result = command.ExecuteScalar();
                    // System.Diagnostics.Debug.WriteLine($"🔍 检查 keyframes 表: {(result == null ? "不存在" : "已存在")}");

                    if (result == null)
                    {
                        // System.Diagnostics.Debug.WriteLine("⚠️ keyframes表不存在，正在创建...");
                        
                        // 创建 keyframes 表
                        Database.ExecuteSqlRaw(@"
                            CREATE TABLE keyframes (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                ImageId INTEGER NOT NULL,
                                Position REAL NOT NULL,
                                YPosition INTEGER NOT NULL,
                                OrderIndex INTEGER NOT NULL,
                                LoopCount INTEGER NULL,
                                FOREIGN KEY (ImageId) REFERENCES MediaFiles(Id) ON DELETE CASCADE
                            )");
                        
                        // 创建索引
                        Database.ExecuteSqlRaw("CREATE INDEX idx_keyframes_image ON keyframes(ImageId)");
                        Database.ExecuteSqlRaw("CREATE INDEX idx_keyframes_order ON keyframes(OrderIndex)");
                        
                        // System.Diagnostics.Debug.WriteLine("✅ keyframes表创建成功");
                    }
                    else
                    {
                        // System.Diagnostics.Debug.WriteLine("ℹ️ keyframes表已存在，跳过创建");
                    }

                    // 检查 keyframe_timings 表是否存在
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='keyframe_timings'";
                    result = command.ExecuteScalar();
                    // System.Diagnostics.Debug.WriteLine($"🔍 检查 keyframe_timings 表: {(result == null ? "不存在" : "已存在")}");

                    if (result == null)
                    {
                        // System.Diagnostics.Debug.WriteLine("⚠️ keyframe_timings表不存在，正在创建...");
                        
                        // 创建 keyframe_timings 表
                        Database.ExecuteSqlRaw(@"
                            CREATE TABLE keyframe_timings (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                ImageId INTEGER NOT NULL,
                                KeyframeId INTEGER NOT NULL,
                                SequenceOrder INTEGER NOT NULL,
                                Duration REAL NOT NULL,
                                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                                FOREIGN KEY (ImageId) REFERENCES MediaFiles(Id) ON DELETE CASCADE,
                                FOREIGN KEY (KeyframeId) REFERENCES keyframes(Id) ON DELETE CASCADE
                            )");
                        
                        // 创建索引
                        Database.ExecuteSqlRaw("CREATE INDEX idx_timing_image ON keyframe_timings(ImageId)");
                        Database.ExecuteSqlRaw("CREATE INDEX idx_timing_sequence ON keyframe_timings(ImageId, SequenceOrder)");
                        
                        // System.Diagnostics.Debug.WriteLine("✅ keyframe_timings表创建成功");
                    }
                    else
                    {
                        // System.Diagnostics.Debug.WriteLine("ℹ️ keyframe_timings表已存在，跳过创建");
                    }
                }

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                    // System.Diagnostics.Debug.WriteLine("📂 数据库连接已关闭");
                }
            }
            catch (Exception)
            {
                // System.Diagnostics.Debug.WriteLine($"❌ 检查/创建关键帧表失败: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
    }
}

