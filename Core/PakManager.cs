using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// PAK资源包管理器
    /// 用于打包和加载资源文件（字体、图片、配置等）
    /// </summary>
    public class PakManager
    {
        private static PakManager _instance;
        private static readonly object _lock = new object();
        
        private readonly Dictionary<string, byte[]> _resourceCache = new Dictionary<string, byte[]>();
        private readonly string _pakFilePath;
        private bool _isLoaded = false;
        
        // PAK文件标识
        private const string PAK_MAGIC = "CCANVAS";
        private const int PAK_VERSION = 1;
        
        /// <summary>
        /// 单例实例
        /// </summary>
        public static PakManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new PakManager();
                        }
                    }
                }
                return _instance;
            }
        }
        
        private PakManager()
        {
            _pakFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources.pak");
        }
        
        /// <summary>
        /// 加载PAK资源包
        /// </summary>
        public bool LoadPak()
        {
            if (_isLoaded)
                return true;
                
            try
            {
                if (!File.Exists(_pakFilePath))
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ [PAK] 资源包不存在: {_pakFilePath}");
                    return false;
                }
                
                using (var fs = new FileStream(_pakFilePath, FileMode.Open, FileAccess.Read))
                using (var br = new BinaryReader(fs))
                {
                    // 读取头部
                    var magic = Encoding.ASCII.GetString(br.ReadBytes(PAK_MAGIC.Length));
                    if (magic != PAK_MAGIC)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ [PAK] 无效的资源包格式");
                        return false;
                    }
                    
                    var version = br.ReadInt32();
                    if (version != PAK_VERSION)
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ [PAK] 不支持的资源包版本: {version}");
                        return false;
                    }
                    
                    // 读取文件数量
                    var fileCount = br.ReadInt32();
                    #if DEBUG
                    //System.Diagnostics.Debug.WriteLine($"📦 [PAK] 加载资源包: {fileCount} 个文件");
                    #endif
                    
                    // 读取文件索引
                    for (int i = 0; i < fileCount; i++)
                    {
                        var pathLength = br.ReadInt32();
                        var path = Encoding.UTF8.GetString(br.ReadBytes(pathLength));
                        var offset = br.ReadInt64();
                        var size = br.ReadInt32();
                        var isCompressed = br.ReadBoolean();
                        
                        // 保存当前位置
                        var currentPos = fs.Position;
                        
                        // 读取文件数据
                        fs.Seek(offset, SeekOrigin.Begin);
                        var data = br.ReadBytes(size);
                        
                        // 如果是压缩的，解压
                        if (isCompressed)
                        {
                            data = Decompress(data);
                        }
                        
                        // 缓存数据
                        _resourceCache[path.Replace("\\", "/")] = data;
                        
                        // 恢复位置
                        fs.Seek(currentPos, SeekOrigin.Begin);
                    }
                }
                
                _isLoaded = true;
                #if DEBUG
                //System.Diagnostics.Debug.WriteLine($"✅ [PAK] 资源包加载成功: {_resourceCache.Count} 个文件");
                #endif
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [PAK] 加载失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 获取资源文件内容
        /// </summary>
        public byte[] GetResource(string path)
        {
            if (!_isLoaded)
                LoadPak();
                
            path = path.Replace("\\", "/");
            
            if (_resourceCache.TryGetValue(path, out var data))
            {
                return data;
            }
            
            return null;
        }
        
        /// <summary>
        /// 获取资源文件文本内容
        /// </summary>
        public string GetResourceText(string path)
        {
            var data = GetResource(path);
            if (data == null)
                return null;
                
            return Encoding.UTF8.GetString(data);
        }
        
        /// <summary>
        /// 检查资源是否存在
        /// </summary>
        public bool ResourceExists(string path)
        {
            if (!_isLoaded)
                LoadPak();
                
            path = path.Replace("\\", "/");
            return _resourceCache.ContainsKey(path);
        }
        
        /// <summary>
        /// 获取所有资源路径
        /// </summary>
        public List<string> GetAllResourcePaths()
        {
            if (!_isLoaded)
                LoadPak();
                
            return _resourceCache.Keys.ToList();
        }
        
        /// <summary>
        /// 创建PAK资源包（工具方法）
        /// </summary>
        public static void CreatePak(string sourceDirectory, string outputPakPath, string[] includePatterns = null)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"📦 [PAK] 开始创建资源包...");
                System.Diagnostics.Debug.WriteLine($"📂 源目录: {sourceDirectory}");
                System.Diagnostics.Debug.WriteLine($"📄 输出: {outputPakPath}");
                
                // 收集要打包的文件
                var files = new List<string>();
                
                if (includePatterns == null || includePatterns.Length == 0)
                {
                    // 默认包含所有文件
                    files.AddRange(Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories));
                }
                else
                {
                    // 根据模式收集文件
                    foreach (var pattern in includePatterns)
                    {
                        files.AddRange(Directory.GetFiles(sourceDirectory, pattern, SearchOption.AllDirectories));
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"📋 找到 {files.Count} 个文件");
                
                using (var fs = new FileStream(outputPakPath, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    // 写入头部
                    bw.Write(Encoding.ASCII.GetBytes(PAK_MAGIC));
                    bw.Write(PAK_VERSION);
                    bw.Write(files.Count);
                    
                    // 预留索引空间（稍后填充）
                    var indexStartPos = fs.Position;
                    var indexSize = files.Count * (4 + 1024 + 8 + 4 + 1); // 近似值
                    fs.Seek(indexSize, SeekOrigin.Current);
                    
                    // 写入文件数据并记录索引
                    var fileInfos = new List<(string path, long offset, int size, bool compressed)>();
                    
                    foreach (var file in files)
                    {
                        var relativePath = Path.GetRelativePath(sourceDirectory, file);
                        var data = File.ReadAllBytes(file);
                        var offset = fs.Position;
                        
                        // 尝试压缩（仅对大于1KB的文件）
                        bool isCompressed = false;
                        if (data.Length > 1024)
                        {
                            var compressed = Compress(data);
                            if (compressed.Length < data.Length * 0.9) // 压缩率超过10%才使用
                            {
                                data = compressed;
                                isCompressed = true;
                            }
                        }
                        
                        bw.Write(data);
                        fileInfos.Add((relativePath, offset, data.Length, isCompressed));
                        
                        System.Diagnostics.Debug.WriteLine($"  ✓ {relativePath} ({data.Length} bytes{(isCompressed ? ", 已压缩" : "")})");
                    }
                    
                    // 回到索引位置写入索引
                    var dataEndPos = fs.Position;
                    fs.Seek(indexStartPos, SeekOrigin.Begin);
                    
                    foreach (var (path, offset, size, compressed) in fileInfos)
                    {
                        var pathBytes = Encoding.UTF8.GetBytes(path);
                        bw.Write(pathBytes.Length);
                        bw.Write(pathBytes);
                        bw.Write(offset);
                        bw.Write(size);
                        bw.Write(compressed);
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ [PAK] 资源包创建成功: {outputPakPath}");
                System.Diagnostics.Debug.WriteLine($"📊 文件大小: {new FileInfo(outputPakPath).Length / 1024} KB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ [PAK] 创建失败: {ex.Message}");
                throw;
            }
        }
        
        /// <summary>
        /// 压缩数据
        /// </summary>
        private static byte[] Compress(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }
        
        /// <summary>
        /// 解压数据
        /// </summary>
        private static byte[] Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var output = new MemoryStream())
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}

