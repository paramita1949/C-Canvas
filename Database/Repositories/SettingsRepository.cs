using System;
using ImageColorChanger.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageColorChanger.Database.Repositories
{
    public sealed class SettingsRepository : ISettingsRepository
    {
        private readonly CanvasDbContext _context;

        public SettingsRepository(CanvasDbContext context)
        {
            _context = context;
        }

        public string GetSetting(string key, string defaultValue = null)
        {
            var setting = _context.Settings.Find(key);
            return setting?.Value ?? defaultValue;
        }

        public void SaveSetting(string key, string value)
        {
            var setting = _context.Settings.Find(key);
            if (setting == null)
            {
                setting = new Setting { Key = key, Value = value };
                _context.Settings.Add(setting);
            }
            else
            {
                setting.Value = value;
            }

            _context.SaveChanges();
        }

        public string GetUISetting(string key, string defaultValue = null)
        {
            var setting = _context.UISettings.Find(key);
            return setting?.Value ?? defaultValue;
        }

        public void SaveUISetting(string key, string value)
        {
            var setting = _context.UISettings.Find(key);
            if (setting == null)
            {
                setting = new UISetting { Key = key, Value = value };
                _context.UISettings.Add(setting);
            }
            else
            {
                setting.Value = value;
            }

            _context.SaveChanges();
        }

        public string GetBibleInsertConfigValue(string key, string defaultValue = "")
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT value FROM bible_insert_config WHERE key = @key";
                    var param = command.CreateParameter();
                    param.ParameterName = "@key";
                    param.Value = key;
                    command.Parameters.Add(param);
                    var result = command.ExecuteScalar();
                    return result?.ToString() ?? defaultValue;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        public void SetBibleInsertConfigValue(string key, string value)
        {
            try
            {
                var connection = _context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    connection.Open();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO bible_insert_config (key, value) 
                        VALUES (@key, @value)";

                    var keyParam = command.CreateParameter();
                    keyParam.ParameterName = "@key";
                    keyParam.Value = key;
                    command.Parameters.Add(keyParam);

                    var valueParam = command.CreateParameter();
                    valueParam.ParameterName = "@value";
                    valueParam.Value = value;
                    command.Parameters.Add(valueParam);

                    command.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }
    }
}
