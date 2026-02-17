using System;
using ImageColorChanger.Database;
using ImageColorChanger.Services.Interfaces;

namespace ImageColorChanger.Services
{
    /// <summary>
    /// 基于 DatabaseManager 的 UI 设置存储实现。
    /// </summary>
    public sealed class UiSettingsStore : IUiSettingsStore
    {
        private readonly DatabaseManager _databaseManager;

        public UiSettingsStore(DatabaseManager databaseManager)
        {
            _databaseManager = databaseManager ?? throw new ArgumentNullException(nameof(databaseManager));
        }

        public string GetValue(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return _databaseManager.GetUISetting(key);
        }

        public void SaveValue(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _databaseManager.SaveUISetting(key, value);
        }
    }
}
