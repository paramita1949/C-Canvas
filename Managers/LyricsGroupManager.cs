using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using ImageColorChanger.Database;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// 歌词分组管理器。
    /// </summary>
    public class LyricsGroupManager
    {
        private readonly CanvasDbContext _dbContext;

        public LyricsGroupManager(CanvasDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public List<LyricsGroup> GetAllGroups()
        {
            return _dbContext.LyricsGroups
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .ToList();
        }

        public LyricsGroup CreateGroup(string name, int sortOrder = 0, bool isSystem = false)
        {
            string safeName = (name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                throw new ArgumentException("歌词库名称不能为空", nameof(name));
            }

            string externalId = Guid.NewGuid().ToString();
            DateTime createdTime = DateTime.Now;

            // 使用独立 DbContext + 直写 SQL，绕过主上下文跟踪状态，避免并发异常影响新建流程。
            string dbPath = _dbContext.Database.GetDbConnection().DataSource;
            using var isolatedContext = new CanvasDbContext(dbPath);
            var connection = isolatedContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO lyrics_groups(name, external_id, sort_order, created_time, modified_time, is_system)
VALUES (@name, @externalId, @sortOrder, @createdTime, NULL, @isSystem);
SELECT last_insert_rowid();";

            AddParam(command, "@name", safeName);
            AddParam(command, "@externalId", externalId);
            AddParam(command, "@sortOrder", sortOrder);
            AddParam(command, "@createdTime", createdTime.ToString("yyyy-MM-dd HH:mm:ss"));
            AddParam(command, "@isSystem", isSystem ? 1 : 0);

            object scalar = command.ExecuteScalar()
                ?? throw new InvalidOperationException("新建歌词库失败：未返回新ID");
            int id = Convert.ToInt32(scalar);

            return new LyricsGroup
            {
                Id = id,
                Name = safeName,
                ExternalId = externalId,
                SortOrder = sortOrder,
                CreatedTime = createdTime,
                IsSystem = isSystem
            };
        }

        public void SetGroupHighlightColor(int groupId, string colorHex)
        {
            string dbPath = _dbContext.Database.GetDbConnection().DataSource;
            using var isolatedContext = new CanvasDbContext(dbPath);
            var connection = isolatedContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE lyrics_groups
SET highlight_color = @color,
    modified_time = @modified
WHERE id = @id;";
            AddParam(command, "@color", string.IsNullOrWhiteSpace(colorHex) ? DBNull.Value : colorHex.Trim());
            AddParam(command, "@modified", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            AddParam(command, "@id", groupId);
            command.ExecuteNonQuery();
        }

        public string GetGroupHighlightColor(int groupId)
        {
            string dbPath = _dbContext.Database.GetDbConnection().DataSource;
            using var isolatedContext = new CanvasDbContext(dbPath);
            var connection = isolatedContext.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                connection.Open();
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT highlight_color FROM lyrics_groups WHERE id = @id LIMIT 1;";
            AddParam(command, "@id", groupId);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? string.Empty : value.ToString();
        }

        private static void AddParam(System.Data.Common.DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
