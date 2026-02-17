namespace ImageColorChanger.Database.Repositories
{
    public interface ISettingsRepository
    {
        string GetSetting(string key, string defaultValue = null);
        void SaveSetting(string key, string value);
        string GetUISetting(string key, string defaultValue = null);
        void SaveUISetting(string key, string value);
        string GetBibleInsertConfigValue(string key, string defaultValue = "");
        void SetBibleInsertConfigValue(string key, string value);
    }
}
