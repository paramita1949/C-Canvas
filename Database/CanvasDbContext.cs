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
        /// 文件夹-素材映射表（folder_images）
        /// </summary>
        public DbSet<FolderImage> FolderImages { get; set; }

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
        /// 文本项目表
        /// </summary>
        public DbSet<TextProject> TextProjects { get; set; }

        /// <summary>
        /// 歌词项目表
        /// </summary>
        public DbSet<LyricsProject> LyricsProjects { get; set; }

        /// <summary>
        /// 文本元素表
        /// </summary>
        public DbSet<TextElement> TextElements { get; set; }

        /// <summary>
        /// 富文本片段表
        /// </summary>
        public DbSet<RichTextSpan> RichTextSpans { get; set; }

        /// <summary>
        /// 幻灯片表
        /// </summary>
        public DbSet<Slide> Slides { get; set; }

        /// <summary>
        /// 合成播放脚本表
        /// </summary>
        public DbSet<CompositeScript> CompositeScripts { get; set; }

        /// <summary>
        /// 圣经历史记录表（20个槽位）
        /// </summary>
        public DbSet<BibleHistoryRecord> BibleHistory { get; set; }

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
                // 规范化路径唯一索引（允许历史空值）
                entity.HasIndex(e => e.NormalizedPath).IsUnique();
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

            // ========== 文件夹-素材映射表配置 ==========
            modelBuilder.Entity<FolderImage>(entity =>
            {
                entity.HasIndex(e => e.FolderId).HasDatabaseName("idx_folder_images_folder");
                entity.HasIndex(e => e.ImageId).HasDatabaseName("idx_folder_images_image");
                entity.HasIndex(e => new { e.FolderId, e.OrderIndex }).HasDatabaseName("idx_folder_images_folder_order");
                entity.HasIndex(e => new { e.FolderId, e.ImageId })
                    .IsUnique()
                    .HasDatabaseName("uidx_folder_images_folder_image");

                entity.HasOne(e => e.Folder)
                    .WithMany(f => f.FolderImages)
                    .HasForeignKey(e => e.FolderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.MediaFile)
                    .WithMany(m => m.FolderImages)
                    .HasForeignKey(e => e.ImageId)
                    .OnDelete(DeleteBehavior.Cascade);
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

            // ========== 文本项目表配置 ==========
            modelBuilder.Entity<TextProject>(entity =>
            {
                // 项目名称索引
                entity.HasIndex(e => e.Name).HasDatabaseName("idx_text_projects_name");
                
                // 创建时间索引
                entity.HasIndex(e => e.CreatedTime).HasDatabaseName("idx_text_projects_created");

                // 配置日期时间转换
                entity.Property(e => e.CreatedTime)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

                entity.Property(e => e.ModifiedTime)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());
            });

            // ========== 歌词项目表配置 ==========
            modelBuilder.Entity<LyricsProject>(entity =>
            {
                // 图片ID索引
                entity.HasIndex(e => e.ImageId).HasDatabaseName("idx_lyrics_projects_image");

                // 外键关系：歌词项目 -> 媒体文件（可选，删除图片时自动置空）
                entity.HasOne(e => e.MediaFile)
                    .WithMany()
                    .HasForeignKey(e => e.ImageId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ========== 文本元素表配置 ==========
            modelBuilder.Entity<TextElement>(entity =>
            {
                // 项目ID索引
                entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_text_elements_project");

                // 幻灯片ID索引
                entity.HasIndex(e => e.SlideId).HasDatabaseName("idx_text_elements_slide");

                // 项目ID+Z-Index复合索引（用于按层级排序）
                entity.HasIndex(e => new { e.ProjectId, e.ZIndex }).HasDatabaseName("idx_text_elements_zindex");

                // 外键关系：文本元素 -> 文本项目（可选，兼容旧数据）
                entity.HasOne(e => e.Project)
                    .WithMany(p => p.Elements)
                    .HasForeignKey(e => e.ProjectId)
                    .OnDelete(DeleteBehavior.SetNull);

                // 外键关系：文本元素 -> 幻灯片
                entity.HasOne(e => e.Slide)
                    .WithMany(s => s.Elements)
                    .HasForeignKey(e => e.SlideId)
                    .OnDelete(DeleteBehavior.Cascade);

                // 🔧 外键关系：文本元素 -> 富文本片段（一对多）
                entity.HasMany(e => e.RichTextSpans)
                    .WithOne()
                    .HasForeignKey(s => s.TextElementId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========== 富文本片段表配置 ==========
            modelBuilder.Entity<RichTextSpan>(entity =>
            {
                // 文本框ID索引
                entity.HasIndex(e => e.TextElementId).HasDatabaseName("idx_rich_text_spans_element");

                // 文本框ID+片段顺序复合索引（用于按顺序查询）
                entity.HasIndex(e => new { e.TextElementId, e.SpanOrder }).HasDatabaseName("idx_rich_text_spans_order");
            });

            // ========== 幻灯片表配置 ==========
            modelBuilder.Entity<Slide>(entity =>
            {
                // 项目ID索引
                entity.HasIndex(e => e.ProjectId).HasDatabaseName("idx_slides_project");
                
                // 项目ID+排序顺序复合索引
                entity.HasIndex(e => new { e.ProjectId, e.SortOrder }).HasDatabaseName("idx_slides_order");

                // 配置日期时间转换
                entity.Property(e => e.CreatedTime)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

                entity.Property(e => e.ModifiedTime)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

                // 外键关系：幻灯片 -> 文本项目
                entity.HasOne(s => s.Project)
                    .WithMany(p => p.Slides)
                    .HasForeignKey(s => s.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ========== 合成播放脚本表配置 ==========
            modelBuilder.Entity<CompositeScript>(entity =>
            {
                // 图片ID唯一索引（一个图片只能有一个合成脚本）
                entity.HasIndex(e => e.ImageId)
                    .IsUnique()
                    .HasDatabaseName("idx_composite_scripts_image");

                // 配置日期时间转换
                entity.Property(e => e.CreatedAt)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

                entity.Property(e => e.UpdatedAt)
                    .HasColumnType("TEXT")
                    .HasConversion(new SqliteDateTimeConverter());

                // 外键关系：合成脚本 -> 媒体文件
                entity.HasOne(s => s.MediaFile)
                    .WithMany()
                    .HasForeignKey(s => s.ImageId)
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

                // v2 文件夹映射层：建表 + 回填（兼容旧库）
                EnsureFolderImagesSchemaExists();

                // 检查并创建关键帧表（兼容旧数据库）
                EnsureKeyframesTableExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureKeyframesTableExists() 完成");

                // 检查并添加合成播放标记字段（兼容旧数据库）
                EnsureCompositePlaybackColumnExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureCompositePlaybackColumnExists() 完成");

                // 检查并创建合成播放脚本表（兼容旧数据库）
                EnsureCompositeScriptTableExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureCompositeScriptTableExists() 完成");

                // 检查并添加歌词项目的图片关联字段（兼容旧数据库）
                EnsureLyricsImageIdColumnExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureLyricsImageIdColumnExists() 完成");

                // 🆕 检查并添加文本项目的排序字段（兼容旧数据库）
                EnsureTextProjectSortOrderColumnExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureTextProjectSortOrderColumnExists() 完成");

                // 🆕 确保默认项目存在
                EnsureDefaultProjectExists();
                // System.Diagnostics.Debug.WriteLine($"✅ EnsureDefaultProjectExists() 完成");

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
        /// 确保 folder_images 及 folders 扩展字段存在，并从旧 folder_id 回填映射。
        /// </summary>
        private void EnsureFolderImagesSchemaExists()
        {
            try
            {
                Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS folder_images (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        folder_id INTEGER NOT NULL,
                        image_id INTEGER NOT NULL,
                        order_index INTEGER NULL,
                        relative_path TEXT NULL,
                        discovered_at TEXT NULL,
                        updated_at TEXT NULL,
                        is_hidden INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (folder_id) REFERENCES folders(id) ON DELETE CASCADE,
                        FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE
                    )");

                Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS uidx_folder_images_folder_image ON folder_images(folder_id, image_id)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_folder_images_folder ON folder_images(folder_id)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_folder_images_image ON folder_images(image_id)");
                Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS idx_folder_images_folder_order ON folder_images(folder_id, order_index)");

                Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN normalized_path TEXT NULL");
            }
            catch
            {
                // 列已存在时 SQLite 会抛异常，忽略即可。
            }

            try { Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN scan_policy TEXT NULL"); } catch { }
            try { Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN last_scan_time TEXT NULL"); } catch { }
            try { Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN last_scan_status TEXT NULL"); } catch { }
            try { Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN last_scan_error TEXT NULL"); } catch { }
            try { Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS uidx_folders_normalized_path ON folders(normalized_path)"); } catch { }

            // 规范化路径回填：先做基础标准化，避免旧数据全为空。
            Database.ExecuteSqlRaw("UPDATE folders SET normalized_path = lower(path) WHERE normalized_path IS NULL OR normalized_path = ''");

            // 从旧字段回填映射，确保历史数据在新读路径可见。
            Database.ExecuteSqlRaw(@"
                INSERT OR IGNORE INTO folder_images(folder_id, image_id, order_index, discovered_at, updated_at, is_hidden)
                SELECT i.folder_id, i.id, i.order_index, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
                FROM images i
                WHERE i.folder_id IS NOT NULL");
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

        /// <summary>
        /// 确保 images 表中存在 composite_playback_enabled 字段（兼容旧数据库）
        /// </summary>
        private void EnsureCompositePlaybackColumnExists()
        {
            try
            {
                var connection = Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    // 检查字段是否存在
                    command.CommandText = "PRAGMA table_info(images)";
                    using (var reader = command.ExecuteReader())
                    {
                        bool columnExists = false;
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "composite_playback_enabled")
                            {
                                columnExists = true;
                                break;
                            }
                        }

                        if (!columnExists)
                        {
                            reader.Close();
                            
                            // 添加字段
                            using (var alterCommand = connection.CreateCommand())
                            {
                                alterCommand.CommandText = @"
                                    ALTER TABLE images 
                                    ADD COLUMN composite_playback_enabled INTEGER NOT NULL DEFAULT 0";
                                alterCommand.ExecuteNonQuery();
                                System.Diagnostics.Debug.WriteLine("✅ 已添加 composite_playback_enabled 字段到 images 表");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 检查/添加 composite_playback_enabled 字段失败: {ex.Message}");
                // 不抛出异常，因为这不是致命错误
            }
        }

        /// <summary>
        /// 确保合成播放脚本表存在（兼容旧数据库）
        /// </summary>
        private void EnsureCompositeScriptTableExists()
        {
            try
            {
                var connection = Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    // 检查表是否存在
                    command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='composite_scripts'";
                    var result = command.ExecuteScalar();

                    if (result == null)
                    {
                        // 创建合成播放脚本表（数据库默认TOTAL时长为105秒，实际使用时从config.json的CompositePlaybackDefaultDuration读取）
                        Database.ExecuteSqlRaw(@"
                            CREATE TABLE composite_scripts (
                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                image_id INTEGER NOT NULL UNIQUE,
                                total_duration REAL NOT NULL DEFAULT 105.0,
                                auto_calculate INTEGER NOT NULL DEFAULT 0,
                                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                                updated_at TEXT DEFAULT CURRENT_TIMESTAMP,
                                FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE
                            )");

                        // 创建索引
                        Database.ExecuteSqlRaw("CREATE UNIQUE INDEX idx_composite_scripts_image ON composite_scripts(image_id)");

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ 已创建 composite_scripts 表");
                        #endif
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 检查/创建 composite_scripts 表失败: {ex.Message}");
                // 不抛出异常，因为这不是致命错误
            }
        }

        /// <summary>
        /// 确保 lyrics_projects 表中存在 image_id 字段（兼容旧数据库）
        /// </summary>
        private void EnsureLyricsImageIdColumnExists()
        {
            try
            {
                var connection = Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    // 检查字段是否存在
                    command.CommandText = "PRAGMA table_info(lyrics_projects)";
                    using (var reader = command.ExecuteReader())
                    {
                        bool columnExists = false;
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "image_id")
                            {
                                columnExists = true;
                                break;
                            }
                        }

                        if (!columnExists)
                        {
                            reader.Close();
                            
                            // 添加字段
                            using (var alterCommand = connection.CreateCommand())
                            {
                                alterCommand.CommandText = @"
                                    ALTER TABLE lyrics_projects 
                                    ADD COLUMN image_id INTEGER NULL";
                                alterCommand.ExecuteNonQuery();
                                System.Diagnostics.Debug.WriteLine("✅ 已添加 image_id 字段到 lyrics_projects 表");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 检查/添加 image_id 字段失败: {ex.Message}");
                // 不抛出异常，因为这不是致命错误
            }
        }

        /// <summary>
        /// 确保 text_projects 表中存在 sort_order 字段（兼容旧数据库）
        /// </summary>
        private void EnsureTextProjectSortOrderColumnExists()
        {
            try
            {
                var connection = Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    // 检查字段是否存在
                    command.CommandText = "PRAGMA table_info(text_projects)";
                    using (var reader = command.ExecuteReader())
                    {
                        bool columnExists = false;
                        while (reader.Read())
                        {
                            if (reader["name"].ToString() == "sort_order")
                            {
                                columnExists = true;
                                break;
                            }
                        }

                        if (!columnExists)
                        {
                            reader.Close();
                            
                            // 添加字段
                            using (var alterCommand = connection.CreateCommand())
                            {
                                alterCommand.CommandText = @"
                                    ALTER TABLE text_projects 
                                    ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0";
                                alterCommand.ExecuteNonQuery();
                                System.Diagnostics.Debug.WriteLine("✅ 已添加 sort_order 字段到 text_projects 表");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 检查/添加 sort_order 字段失败: {ex.Message}");
                // 不抛出异常，因为这不是致命错误
            }
        }

        /// <summary>
        /// 确保默认项目存在
        /// - 首次安装：创建"项目1"和"赞美诗"
        /// - 用户删除所有项目后：只创建"项目1"
        /// </summary>
        private void EnsureDefaultProjectExists()
        {
            try
            {
                // 检查是否已有项目
                if (!TextProjects.Any())
                {
                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine("📋 [数据库初始化] 没有项目，检查是否首次初始化...");
                    #endif

                    // 检查是否是首次初始化（通过 Settings 表中的标记判断）
                    const string INIT_FLAG_KEY = "first_initialization_completed";
                    var initFlag = Settings.FirstOrDefault(s => s.Key == INIT_FLAG_KEY);
                    bool isFirstTime = (initFlag == null);

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"📋 [数据库初始化] 首次初始化: {isFirstTime}");
                    #endif

                    // 创建"项目1"
                    var project1 = new TextProject
                    {
                        Name = "项目1",
                        CanvasWidth = 1920,
                        CanvasHeight = 1080,
                        CreatedTime = DateTime.Now,
                        ModifiedTime = DateTime.Now,
                        SortOrder = 1
                    };
                    TextProjects.Add(project1);
                    SaveChanges();

                    // 为"项目1"创建第一张幻灯片
                    var firstSlide1 = new Slide
                    {
                        ProjectId = project1.Id,
                        Title = "幻灯片 1",
                        SortOrder = 1,
                        BackgroundColor = "#000000",  // 默认黑色背景
                        SplitMode = -1,  // 默认无分割模式
                        SplitStretchMode = false  // 默认适中模式
                    };
                    Slides.Add(firstSlide1);
                    SaveChanges();

                    #if DEBUG
                    System.Diagnostics.Debug.WriteLine($"✅ [数据库初始化] 默认项目创建成功: {project1.Name} (ID={project1.Id})");
                    #endif

                    // 只在首次初始化时创建"赞美诗"
                    if (isFirstTime)
                    {
                        var praiseProject = new TextProject
                        {
                            Name = "赞美诗",
                            CanvasWidth = 1920,
                            CanvasHeight = 1080,
                            CreatedTime = DateTime.Now,
                            ModifiedTime = DateTime.Now,
                            SortOrder = 2
                        };
                        TextProjects.Add(praiseProject);
                        SaveChanges();

                        // 为"赞美诗"创建第一张幻灯片
                        var firstSlide2 = new Slide
                        {
                            ProjectId = praiseProject.Id,
                            Title = "幻灯片 1",
                            SortOrder = 1,
                            BackgroundColor = "#000000",  // 默认黑色背景
                            SplitMode = -1,  // 默认无分割模式
                            SplitStretchMode = false  // 默认适中模式
                        };
                        Slides.Add(firstSlide2);
                        SaveChanges();

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine($"✅ [数据库初始化] 默认项目创建成功: {praiseProject.Name} (ID={praiseProject.Id})");
                        #endif

                        // 标记首次初始化已完成
                        Settings.Add(new Setting
                        {
                            Key = INIT_FLAG_KEY,
                            Value = "true"
                        });
                        SaveChanges();

                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("✅ [数据库初始化] 首次初始化标记已设置");
                        #endif
                    }
                    else
                    {
                        #if DEBUG
                        System.Diagnostics.Debug.WriteLine("ℹ️ [数据库初始化] 非首次初始化，仅创建项目1");
                        #endif
                    }
                }
            }
            catch (Exception ex)
            {
                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [数据库初始化] 创建默认项目失败: {ex.Message}");
                #else
                _ = ex;  // 防止未使用变量警告
                #endif
                // 不抛出异常，因为这不是致命错误
            }
        }
    }
}

