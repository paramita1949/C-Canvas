using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// 资源加载器
    /// 支持从PAK包或文件系统加载资源
    /// </summary>
    public static class ResourceLoader
    {
        private static bool _usePak = true;
        private static bool _pakInitialized = false;
        
        /// <summary>
        /// 是否使用PAK包（默认true）
        /// </summary>
        public static bool UsePak
        {
            get => _usePak;
            set
            {
                _usePak = value;
                System.Diagnostics.Debug.WriteLine($"📦 [ResourceLoader] 使用PAK包: {_usePak}");
            }
        }
        
        /// <summary>
        /// 初始化资源加载器
        /// </summary>
        public static void Initialize()
        {
            if (_pakInitialized)
                return;
                
            if (_usePak)
            {
                var success = PakManager.Instance.LoadPak();
                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ [ResourceLoader] PAK包加载失败，回退到文件系统");
                    _usePak = false;
                }
            }
            
            _pakInitialized = true;
        }
        
        /// <summary>
        /// 加载文本文件
        /// </summary>
        public static string LoadTextFile(string relativePath)
        {
            Initialize();
            
            try
            {
                if (_usePak)
                {
                    var content = PakManager.Instance.GetResourceText(relativePath);
                    if (content != null)
                    {
                        //System.Diagnostics.Debug.WriteLine($"📦 [ResourceLoader] 从PAK加载: {relativePath}");
                        return content;
                    }
                }
                
                // 从文件系统加载
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    //System.Diagnostics.Debug.WriteLine($"📂 [ResourceLoader] 从文件加载: {relativePath}");
                    return File.ReadAllText(fullPath);
                }
                
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 文件不存在: {relativePath}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 加载失败: {relativePath}, {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 加载二进制文件
        /// </summary>
        public static byte[] LoadBinaryFile(string relativePath)
        {
            Initialize();
            
            try
            {
                if (_usePak)
                {
                    var data = PakManager.Instance.GetResource(relativePath);
                    if (data != null)
                    {
                        //System.Diagnostics.Debug.WriteLine($"📦 [ResourceLoader] 从PAK加载: {relativePath}");
                        return data;
                    }
                }
                
                // 从文件系统加载
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    //System.Diagnostics.Debug.WriteLine($"📂 [ResourceLoader] 从文件加载: {relativePath}");
                    return File.ReadAllBytes(fullPath);
                }
                
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 文件不存在: {relativePath}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 加载失败: {relativePath}, {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 加载图片资源
        /// </summary>
        public static BitmapImage LoadImage(string relativePath)
        {
            var data = LoadBinaryFile(relativePath);
            if (data == null)
                return null;
                
            try
            {
                var image = new BitmapImage();
                using (var ms = new MemoryStream(data))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                    image.Freeze();
                }
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 图片加载失败: {relativePath}, {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 加载字体文件
        /// </summary>
        public static System.Windows.Media.FontFamily LoadFont(string relativePath, string fontFamilyName)
        {
            Initialize();
            
            try
            {
                if (_usePak)
                {
                    var data = PakManager.Instance.GetResource(relativePath);
                    if (data != null)
                    {
                        // 从内存加载字体
                        var tempPath = Path.Combine(Path.GetTempPath(), "CCanvas_Fonts", Path.GetFileName(relativePath));
                        var tempDir = Path.GetDirectoryName(tempPath);
                        
                        if (!Directory.Exists(tempDir))
                            Directory.CreateDirectory(tempDir);
                            
                        // 如果临时文件不存在或已过期，重新写入
                        if (!File.Exists(tempPath) || new FileInfo(tempPath).Length != data.Length)
                        {
                            File.WriteAllBytes(tempPath, data);
                        }
                        
                        var baseUri = new Uri(Path.GetTempPath());
                        return new System.Windows.Media.FontFamily(baseUri, $"./CCanvas_Fonts/{Path.GetFileName(relativePath)}#{fontFamilyName}");
                    }
                }
                
                // 从文件系统加载
                var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    var baseUri = new Uri(AppDomain.CurrentDomain.BaseDirectory);
                    return new System.Windows.Media.FontFamily(baseUri, $"./{relativePath.Replace("\\", "/")}#{fontFamilyName}");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [ResourceLoader] 字体加载失败: {relativePath}, {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        public static bool ResourceExists(string relativePath)
        {
            Initialize();
            
            if (_usePak)
            {
                if (PakManager.Instance.ResourceExists(relativePath))
                    return true;
            }
            
            var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            return File.Exists(fullPath);
        }
        
        /// <summary>
        /// 获取资源的完整路径
        /// </summary>
        public static string GetResourcePath(string relativePath)
        {
            if (_usePak && PakManager.Instance.ResourceExists(relativePath))
            {
                return $"pak://{relativePath}";
            }
            
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        }
    }
}

