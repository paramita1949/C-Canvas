namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 启动性能日志（已停用）。
    /// </summary>
    public static class StartupPerfLogger
    {
        // 兼容现有调用点：保持 API 不变，但不再写任何日志文件。
        public static string LogFilePath => string.Empty;

        public static void Initialize(string phase, string detail = null)
        {
            // no-op
        }

        public static void Mark(string phase, string detail = null)
        {
            // no-op
        }

        public static void Error(string phase, Exception exception)
        {
            // no-op
        }
    }
}
