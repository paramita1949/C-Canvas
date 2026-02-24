using System;
using ImageColorChanger.Database;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Database.Migrations
{
    /// <summary>
    /// 数据库迁移执行器（P0-4A）。
    /// 从 DatabaseManager 中拆分迁移职责，支持独立执行。
    /// </summary>
    public sealed class DatabaseMigrationRunner : IDisposable
    {
        private readonly CanvasDbContext _context;
        private readonly bool _ownsContext;

        public DatabaseMigrationRunner(CanvasDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _ownsContext = false;
        }

        public DatabaseMigrationRunner(string dbPath = null)
        {
            if (string.IsNullOrEmpty(dbPath))
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                dbPath = System.IO.Path.Combine(appDirectory, "pyimages.db");
            }

            _context = new CanvasDbContext(dbPath);
            _context.InitializeDatabase();
            _ownsContext = true;
        }

        /// <summary>
        /// 启动阶段迁移入口（按既有顺序执行，保持行为一致）。
        /// </summary>
        public void RunStartupMigrations()
        {
            MigrateAddLoopCount();
            MigrateAddHighlightColor();
            MigrateAddBibleHistoryTable();
            MigrateAddBibleInsertConfigTable();
            MigrateAddUnderlineSupport();
            MigrateAddRichTextSupport();
            MigrateCreateRichTextSpansTable();
            MigrateUpgradeRichTextSpansV2Schema();
            MigrateAddShadowTypeAndPreset();
            MigrateAddVideoBackgroundSupport();
        }

        public void MigrateAddLoopCount()
        {
            try
            {
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('keyframes') WHERE name='loop_count'";
                var connection = _context.Database.GetDbConnection();
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    if (count == 0)
                    {
                        _context.Database.ExecuteSqlRaw("ALTER TABLE keyframes ADD COLUMN loop_count INTEGER NULL");
                    }
                }

                connection.Close();
            }
            catch
            {
            }
        }

        public void MigrateAddHighlightColor()
        {
            try
            {
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('folders') WHERE name='highlight_color'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    if (count == 0)
                    {
                        _context.Database.ExecuteSqlRaw("ALTER TABLE folders ADD COLUMN highlight_color TEXT NULL");
                    }
                }
            }
            catch
            {
            }
        }

        public void MigrateAddBibleHistoryTable()
        {
            try
            {
                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bible_history'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    if (count == 0)
                    {
                        var createTableSql = @"
                            CREATE TABLE bible_history (
                                slot_index INTEGER PRIMARY KEY,
                                display_text TEXT,
                                book_id INTEGER NOT NULL DEFAULT 0,
                                chapter INTEGER NOT NULL DEFAULT 0,
                                start_verse INTEGER NOT NULL DEFAULT 0,
                                end_verse INTEGER NOT NULL DEFAULT 0,
                                is_checked INTEGER NOT NULL DEFAULT 0,
                                is_locked INTEGER NOT NULL DEFAULT 0,
                                updated_time TEXT NOT NULL
                            )";
                        _context.Database.ExecuteSqlRaw(createTableSql);
                    }
                }
            }
            catch
            {
            }
        }

        public void MigrateAddUnderlineSupport()
        {
            try
            {
                var checkSql = "SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='is_underline'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    if (count == 0)
                    {
                        _context.Database.ExecuteSqlRaw("ALTER TABLE text_elements ADD COLUMN is_underline INTEGER NOT NULL DEFAULT 0");
                    }
                }
            }
            catch
            {
            }
        }

        public void MigrateAddRichTextSupport()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                var columnsToAdd = new[]
                {
                    ("is_italic", "INTEGER NOT NULL DEFAULT 0"),
                    ("border_color", "TEXT NOT NULL DEFAULT '#000000'"),
                    ("border_width", "REAL NOT NULL DEFAULT 0"),
                    ("border_radius", "REAL NOT NULL DEFAULT 0"),
                    ("border_opacity", "INTEGER NOT NULL DEFAULT 0"),
                    ("background_color", "TEXT NOT NULL DEFAULT '#FFFFFF'"),
                    ("background_radius", "REAL NOT NULL DEFAULT 0"),
                    ("background_opacity", "INTEGER NOT NULL DEFAULT 0"),
                    ("shadow_color", "TEXT NOT NULL DEFAULT '#000000'"),
                    ("shadow_offset_x", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_offset_y", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_blur", "REAL NOT NULL DEFAULT 0"),
                    ("shadow_opacity", "INTEGER NOT NULL DEFAULT 0"),
                    ("line_spacing", "REAL NOT NULL DEFAULT 1.2"),
                    ("letter_spacing", "REAL NOT NULL DEFAULT 0.0")
                };

                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());
                        if (count == 0)
                        {
#pragma warning disable EF1002
                            _context.Database.ExecuteSqlRaw($"ALTER TABLE text_elements ADD COLUMN {columnName} {columnDef}");
#pragma warning restore EF1002
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void MigrateAddShadowTypeAndPreset()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                var columnsToAdd = new[]
                {
                    ("shadow_type", "INTEGER NOT NULL DEFAULT 0"),
                    ("shadow_preset", "INTEGER NOT NULL DEFAULT 0")
                };

                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('text_elements') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());
                        if (count == 0)
                        {
                            var alterSql = $"ALTER TABLE text_elements ADD COLUMN {columnName} {columnDef}";
                            _context.Database.ExecuteSqlRaw(alterSql);
                        }
                    }
                }

                if (connection.State == System.Data.ConnectionState.Open)
                {
                    connection.Close();
                }
            }
            catch
            {
                throw;
            }
        }

        public void MigrateCreateRichTextSpansTable()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='rich_text_spans'";
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());

                    if (count == 0)
                    {
                        var createTableSql = @"
                            CREATE TABLE rich_text_spans (
                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                text_element_id INTEGER NOT NULL,
                                span_order INTEGER NOT NULL,
                                text TEXT NOT NULL DEFAULT '',
                                paragraph_index INTEGER NULL,
                                run_index INTEGER NULL,
                                format_version TEXT NULL,
                                font_family TEXT NULL,
                                font_size REAL NULL,
                                font_color TEXT NULL,
                                is_bold INTEGER NOT NULL DEFAULT 0,
                                is_italic INTEGER NOT NULL DEFAULT 0,
                                is_underline INTEGER NOT NULL DEFAULT 0,
                                border_color TEXT NULL,
                                border_width REAL NULL,
                                border_radius REAL NULL,
                                border_opacity INTEGER NULL,
                                background_color TEXT NULL,
                                background_radius REAL NULL,
                                background_opacity INTEGER NULL,
                                shadow_color TEXT NULL,
                                shadow_offset_x REAL NULL,
                                shadow_offset_y REAL NULL,
                                shadow_blur REAL NULL,
                                shadow_opacity INTEGER NULL,
                                FOREIGN KEY (text_element_id) REFERENCES text_elements(id) ON DELETE CASCADE
                            )";

                        _context.Database.ExecuteSqlRaw(createTableSql);
                        _context.Database.ExecuteSqlRaw("CREATE INDEX idx_rich_text_spans_element ON rich_text_spans(text_element_id)");
                        _context.Database.ExecuteSqlRaw("CREATE INDEX idx_rich_text_spans_order ON rich_text_spans(text_element_id, span_order)");
                        _context.Database.ExecuteSqlRaw("CREATE INDEX idx_rich_text_spans_paragraph_run ON rich_text_spans(text_element_id, paragraph_index, run_index)");
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public void MigrateUpgradeRichTextSpansV2Schema()
        {
            try
            {
                EnsureRichTextSpansColumnExists("paragraph_index", "INTEGER NULL");
                EnsureRichTextSpansColumnExists("run_index", "INTEGER NULL");
                EnsureRichTextSpansColumnExists("format_version", "TEXT NULL");
                _context.Database.ExecuteSqlRaw(
                    "CREATE INDEX IF NOT EXISTS idx_rich_text_spans_paragraph_run ON rich_text_spans(text_element_id, paragraph_index, run_index)");
            }
            catch
            {
            }
        }

        private void EnsureRichTextSpansColumnExists(string columnName, string definition)
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using (var checkTableCommand = connection.CreateCommand())
            {
                checkTableCommand.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='rich_text_spans' LIMIT 1";
                if (checkTableCommand.ExecuteScalar() == null)
                {
                    return;
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(rich_text_spans)";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }
            }

#pragma warning disable EF1002
            _context.Database.ExecuteSqlRaw($"ALTER TABLE rich_text_spans ADD COLUMN {columnName} {definition}");
#pragma warning restore EF1002
        }

        public void MigrateAddVideoBackgroundSupport()
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                var columnsToAdd = new[]
                {
                    ("video_background_enabled", "INTEGER NOT NULL DEFAULT 0"),
                    ("video_loop_enabled", "INTEGER NOT NULL DEFAULT 1"),
                    ("video_volume", "REAL NOT NULL DEFAULT 0.5")
                };

                foreach (var (columnName, columnDef) in columnsToAdd)
                {
                    var checkSql = $"SELECT COUNT(*) FROM pragma_table_info('slides') WHERE name='{columnName}'";
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = checkSql;
                        var count = Convert.ToInt32(command.ExecuteScalar());
                        if (count == 0)
                        {
                            var alterSql = $"ALTER TABLE slides ADD COLUMN {columnName} {columnDef}";
                            _context.Database.ExecuteSqlRaw(alterSql);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void MigrateAddBibleInsertConfigTable()
        {
            try
            {
                var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bible_insert_config'";
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = checkSql;
                    var count = Convert.ToInt32(command.ExecuteScalar());
                    if (count == 0)
                    {
                        var createTableSql = @"
                            CREATE TABLE bible_insert_config (
                                key TEXT PRIMARY KEY,
                                value TEXT NOT NULL
                            )";
                        _context.Database.ExecuteSqlRaw(createTableSql);

                        var insertDefaultSql = @"
                            INSERT INTO bible_insert_config (key, value) VALUES
                            ('style', '0'),
                            ('font_family', 'DengXian'),
                            ('title_color', '#FF0000'),
                            ('title_size', '50'),
                            ('title_bold', '1'),
                            ('verse_color', '#FF9A35'),
                            ('verse_size', '40'),
                            ('verse_bold', '0'),
                            ('verse_spacing', '1.2'),
                            ('verse_number_color', '#FFFF00'),
                            ('verse_number_size', '40'),
                            ('verse_number_bold', '1'),
                            ('auto_hide_navigation', '1')";
                        _context.Database.ExecuteSqlRaw(insertDefaultSql);
                    }
                }
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_ownsContext)
            {
                _context?.Dispose();
            }
        }
    }
}
