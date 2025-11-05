using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using ImageColorChanger.Database.Models.Bible;

namespace ImageColorChanger.Database
{
    /// <summary>
    /// 圣经数据库上下文
    /// </summary>
    public class BibleDbContext : DbContext
    {
        private readonly string _databasePath;

        /// <summary>
        /// 经文表
        /// </summary>
        public DbSet<BibleVerse> Bible { get; set; }

        /// <summary>
        /// 标题表
        /// </summary>
        public DbSet<BibleTitle> Titles { get; set; }

        /// <summary>
        /// 元数据表
        /// </summary>
        public DbSet<BibleMetadata> Metadata { get; set; }

        /// <summary>
        /// 默认构造函数（使用默认数据库路径）
        /// </summary>
        public BibleDbContext()
        {
            _databasePath = GetDefaultDatabasePath();
        }

        /// <summary>
        /// 带参数的构造函数（可指定数据库路径）
        /// </summary>
        public BibleDbContext(string databasePath)
        {
            _databasePath = databasePath;
        }

        /// <summary>
        /// 带选项的构造函数（用于依赖注入）
        /// </summary>
        public BibleDbContext(DbContextOptions<BibleDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var connectionString = $"Data Source={_databasePath};";
                optionsBuilder.UseSqlite(connectionString);

                #if DEBUG
                System.Diagnostics.Debug.WriteLine($"[圣经数据库] 连接字符串: {connectionString}");
                #endif
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置 BibleVerse 实体
            modelBuilder.Entity<BibleVerse>(entity =>
            {
                entity.ToTable("Bible");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Book).IsRequired();
                entity.Property(e => e.Chapter).IsRequired();
                entity.Property(e => e.Verse).IsRequired();
                entity.Property(e => e.Scripture).IsRequired();

                // 创建索引
                entity.HasIndex(e => new { e.Book, e.Chapter });
                entity.HasIndex(e => new { e.Book, e.Chapter, e.Verse });
            });

            // 配置 BibleTitle 实体
            modelBuilder.Entity<BibleTitle>(entity =>
            {
                entity.ToTable("Titles");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Book).IsRequired();
                entity.Property(e => e.Chapter).IsRequired();
                entity.Property(e => e.Verse).IsRequired();
                entity.Property(e => e.Scripture).IsRequired();

                // 创建索引
                entity.HasIndex(e => new { e.Book, e.Chapter });
            });

            // 配置 BibleMetadata 实体
            modelBuilder.Entity<BibleMetadata>(entity =>
            {
                entity.ToTable("metadata");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired();
                entity.Property(e => e.Value).IsRequired();
            });
        }

        /// <summary>
        /// 获取默认数据库路径
        /// </summary>
        private static string GetDefaultDatabasePath()
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(basePath, "data", "assets", "bible.db");
        }

        /// <summary>
        /// 检查数据库文件是否存在
        /// </summary>
        public bool DatabaseExists()
        {
            return File.Exists(_databasePath);
        }

        /// <summary>
        /// 获取数据库路径
        /// </summary>
        public string GetDatabasePath()
        {
            return _databasePath;
        }
    }
}

