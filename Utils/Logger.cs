using System;
using System.Diagnostics;

namespace ImageColorChanger.Utils
{
    /// <summary>
    /// 简化的日志管理器（使用Debug输出）
    /// </summary>
    public static class Logger
    {
        /// <summary>
        /// 初始化日志系统（空实现）
        /// </summary>
        public static void Initialize()
        {
            // 简化实现，无需初始化
        }

        /// <summary>
        /// 记录调试信息
        /// </summary>
        public static void Debug(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[DEBUG] {message}");
        }

        /// <summary>
        /// 记录调试信息（带参数）
        /// </summary>
        public static void Debug(string message, params object[] args)
        {
            try
            {
                var formatted = FormatMessage(message, args);
                System.Diagnostics.Debug.WriteLine($"[DEBUG] {formatted}");
            }
            catch { }
        }

        /// <summary>
        /// 记录普通信息
        /// </summary>
        public static void Info(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
        }

        /// <summary>
        /// 记录普通信息（带参数）
        /// </summary>
        public static void Info(string message, params object[] args)
        {
            try
            {
                var formatted = FormatMessage(message, args);
                System.Diagnostics.Debug.WriteLine($"[INFO] {formatted}");
            }
            catch { }
        }

        /// <summary>
        /// 记录警告信息
        /// </summary>
        public static void Warning(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[WARN] {message}");
        }

        /// <summary>
        /// 记录警告信息（带参数）
        /// </summary>
        public static void Warning(string message, params object[] args)
        {
            try
            {
                var formatted = FormatMessage(message, args);
                System.Diagnostics.Debug.WriteLine($"[WARN] {formatted}");
            }
            catch { }
        }

        /// <summary>
        /// 记录错误信息
        /// </summary>
        public static void Error(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
        }

        /// <summary>
        /// 记录错误信息（带参数）
        /// </summary>
        public static void Error(string message, params object[] args)
        {
            try
            {
                var formatted = FormatMessage(message, args);
                System.Diagnostics.Debug.WriteLine($"[ERROR] {formatted}");
            }
            catch { }
        }

        /// <summary>
        /// 记录异常信息
        /// </summary>
        public static void Error(Exception exception, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] {message}: {exception.Message}");
        }

        /// <summary>
        /// 记录异常信息（带参数）
        /// </summary>
        public static void Error(Exception exception, string message, params object[] args)
        {
            try
            {
                var formatted = FormatMessage(message, args);
                System.Diagnostics.Debug.WriteLine($"[ERROR] {formatted}: {exception.Message}");
            }
            catch { }
        }

        /// <summary>
        /// 记录致命错误
        /// </summary>
        public static void Fatal(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] {message}");
        }

        /// <summary>
        /// 记录致命错误（带异常）
        /// </summary>
        public static void Fatal(Exception exception, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[FATAL] {message}: {exception.Message}");
        }

        /// <summary>
        /// 关闭日志（空实现）
        /// </summary>
        public static void Shutdown()
        {
            // 简化实现，无需关闭
        }

        /// <summary>
        /// 格式化消息（简单实现）
        /// </summary>
        private static string FormatMessage(string message, params object[] args)
        {
            if (args == null || args.Length == 0)
                return message;

            // 简单替换 {PropertyName} 为参数值
            for (int i = 0; i < args.Length; i++)
            {
                message = message.Replace($"{{{i}}}", args[i]?.ToString() ?? "null");
            }

            // 处理命名参数（如 {ImageId}）
            var index = 0;
            while (message.Contains("{") && index < args.Length)
            {
                var start = message.IndexOf('{');
                var end = message.IndexOf('}', start);
                if (start >= 0 && end > start)
                {
                    message = message.Substring(0, start) + (args[index]?.ToString() ?? "null") + message.Substring(end + 1);
                    index++;
                }
                else
                {
                    break;
                }
            }

            return message;
        }
    }
}

