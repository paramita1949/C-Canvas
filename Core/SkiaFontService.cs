using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// SkiaSharp字体服务 - 为SkiaSharp提供字体加载支持
    /// </summary>
    public class SkiaFontService
    {
        private static SkiaFontService _instance;
        private static readonly object _lock = new object();
        
        private readonly Dictionary<string, SKTypeface> _typefaceCache;
        
        /// <summary>
        /// 单例实例
        /// </summary>
        public static SkiaFontService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SkiaFontService();
                        }
                    }
                }
                return _instance;
            }
        }
        
        private SkiaFontService()
        {
            _typefaceCache = new Dictionary<string, SKTypeface>();
        }
        
        /// <summary>
        /// 根据字体族名称获取SKTypeface（支持自定义字体文件）
        /// </summary>
        /// <param name="fontFamilyName">字体族名称（如："江西拙楷"）</param>
        /// <param name="isBold">是否加粗</param>
        /// <param name="isItalic">是否斜体</param>
        /// <returns>SKTypeface对象，失败返回默认字体</returns>
        public SKTypeface GetTypeface(string fontFamilyName, bool isBold = false, bool isItalic = false)
        {
            if (string.IsNullOrEmpty(fontFamilyName))
            {
                return GetDefaultTypeface(isBold, isItalic);
            }
            
            // 生成缓存键
            string cacheKey = $"{fontFamilyName}_{isBold}_{isItalic}";
            
//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"🔍 [SkiaFontService] GetTypeface 调用: {fontFamilyName} (粗体:{isBold}, 斜体:{isItalic})");
//#endif
            
            // 从缓存中查找
            if (_typefaceCache.ContainsKey(cacheKey))
            {
//#if DEBUG
//                var cachedTypeface = _typefaceCache[cacheKey];
//                System.Diagnostics.Debug.WriteLine($"  ✅ [SkiaFontService] 缓存命中: {cacheKey} -> {cachedTypeface.FamilyName}");
//#endif
                return _typefaceCache[cacheKey];
            }
            
//#if DEBUG
//            System.Diagnostics.Debug.WriteLine($"  ⚠️ [SkiaFontService] 缓存未命中，开始加载字体");
//#endif
            
            // 加载字体
            SKTypeface typeface = LoadTypeface(fontFamilyName, isBold, isItalic);
            
            // 缓存字体
            if (typeface != null)
            {
                _typefaceCache[cacheKey] = typeface;
            }
            
            return typeface ?? GetDefaultTypeface(isBold, isItalic);
        }
        
        /// <summary>
        /// 加载SKTypeface
        /// </summary>
        private SKTypeface LoadTypeface(string fontFamilyName, bool isBold, bool isItalic)
        {
            try
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"    📂 [SkiaFontService] LoadTypeface 开始: {fontFamilyName}, 加粗:{isBold}, 斜体:{isItalic}");
#endif
                
                // 1. 尝试从FontService获取字体配置
                var fontService = FontService.Instance;
                var fontConfig = fontService.GetFontConfig();
                
                if (fontConfig != null)
                {
                    // 查找字体配置
                    foreach (var category in fontConfig.FontCategories)
                    {
                        foreach (var font in category.Fonts)
                        {
                            if (font.Family.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase) ||
                                font.Name.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase))
                            {
#if DEBUG
                                System.Diagnostics.Debug.WriteLine($"    ✅ [SkiaFontService] 找到字体配置: {font.Name} (Family:{font.Family}, File:{font.File})");
#endif
                                // 找到字体配置
                                if (font.File == "system")
                                {
                                    // 系统字体
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"    🎯 [SkiaFontService] 系统字体，将应用加粗样式: {isBold}");
#endif
                                    return GetDefaultTypeface(isBold, isItalic, fontFamilyName);
                                }
                                else
                                {
                                    // 自定义字体文件
#if DEBUG
                                    System.Diagnostics.Debug.WriteLine($"    ⚠️ [SkiaFontService] 自定义字体，加载字体文件（注意：加粗参数isBold={isBold}未传递到LoadTypefaceFromFile）");
#endif
                                    return LoadTypefaceFromFile(font.File, fontFamilyName);
                                }
                            }
                        }
                    }
                }
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"    ⚠️ [SkiaFontService] 未找到字体配置，回退到系统字体");
//#endif
                
                // 2. 如果FontService找不到，尝试作为系统字体加载
                return GetDefaultTypeface(isBold, isItalic, fontFamilyName);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [SkiaFontService] 字体加载失败 [{fontFamilyName}]: {ex.Message}");
#else
                _ = ex;
#endif
                return null;
            }
        }
        
        /// <summary>
        /// 从文件加载SKTypeface（支持PAK和文件系统）
        /// </summary>
        private SKTypeface LoadTypefaceFromFile(string fontFileName, string fontFamilyName)
        {
            try
            {
                string fontRelativePath = $"Fonts/{fontFileName}";
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"🔍 [SkiaFontService] 尝试加载字体: {fontRelativePath}");
//#endif
                
                // 1. 尝试从PAK加载
                if (ResourceLoader.UsePak)
                {
                    // 检查各种可能的路径
                    string[] possiblePaths = new string[]
                    {
                        fontRelativePath,
                        $"CCanvas_Fonts/{fontFileName}",
                        $"./CCanvas_Fonts/{fontFileName}",
                        fontFileName
                    };
                    
                    foreach (var pakPath in possiblePaths)
                    {
                        var fontData = PakManager.Instance.GetResource(pakPath);
                        
                        if (fontData != null && fontData.Length > 0)
                        {
//#if DEBUG
//                            System.Diagnostics.Debug.WriteLine($"  [SkiaFontService] PAK数据获取成功: {fontData.Length} bytes, 路径: {pakPath}");
//#endif
                            var typeface = SKTypeface.FromData(SKData.CreateCopy(fontData));
                            
                            if (typeface != null)
                            {
//#if DEBUG
//                                System.Diagnostics.Debug.WriteLine($"  ✅ [SkiaFontService] 从PAK加载成功: {pakPath}");
//                                System.Diagnostics.Debug.WriteLine($"     请求字体族: {fontFamilyName}");
//                                System.Diagnostics.Debug.WriteLine($"     实际字体族: {typeface.FamilyName}");
//                                System.Diagnostics.Debug.WriteLine($"     字形数量: {typeface.GetGlyphs("测试文字").Length}");
//#endif
                                return typeface;
                            }
                        }
                    }
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  [SkiaFontService] PAK中未找到字体，尝试文件系统");
//#endif
                }
                
                // 2. 尝试从文件系统加载
                string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fontRelativePath);
                
                if (File.Exists(fullPath))
                {
                    var typeface = SKTypeface.FromFile(fullPath);
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  ✅ [SkiaFontService] 从文件加载: {fullPath}");
//                    System.Diagnostics.Debug.WriteLine($"     请求字体族: {fontFamilyName}");
//                    System.Diagnostics.Debug.WriteLine($"     实际字体族: {typeface.FamilyName}");
//                    System.Diagnostics.Debug.WriteLine($"     字形数量: {typeface.GetGlyphs("测试文字").Length}");
//#endif
                    return typeface;
                }
                
                // 3. 尝试从临时目录加载（WPF可能已经解压到这里）
                string tempPath = Path.Combine(Path.GetTempPath(), "CCanvas_Fonts", fontFileName);
                
                if (File.Exists(tempPath))
                {
                    var typeface = SKTypeface.FromFile(tempPath);
                    
//#if DEBUG
//                    System.Diagnostics.Debug.WriteLine($"  ✅ [SkiaFontService] 从临时目录加载: {tempPath}");
//                    System.Diagnostics.Debug.WriteLine($"     请求字体族: {fontFamilyName}");
//                    System.Diagnostics.Debug.WriteLine($"     实际字体族: {typeface.FamilyName}");
//                    System.Diagnostics.Debug.WriteLine($"     字形数量: {typeface.GetGlyphs("测试文字").Length}");
//#endif
                    return typeface;
                }
                
//#if DEBUG
//                System.Diagnostics.Debug.WriteLine($"  ⚠️ [SkiaFontService] 字体文件不存在: {fontRelativePath}");
//#endif
                return null;
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"❌ [SkiaFontService] 字体文件加载失败 [{fontFileName}]: {ex.Message}");
#else
                _ = ex;
#endif
                return null;
            }
        }
        
        /// <summary>
        /// 获取默认字体（系统字体）
        /// </summary>
        private SKTypeface GetDefaultTypeface(bool isBold, bool isItalic, string fontFamilyName = null)
        {
            try
            {
                var fontStyle = GetFontStyle(isBold, isItalic);
                
                if (!string.IsNullOrEmpty(fontFamilyName))
                {
                    // 尝试使用指定的字体族名称
                    return SKTypeface.FromFamilyName(fontFamilyName, fontStyle);
                }
                
                // 使用默认字体
                return SKTypeface.FromFamilyName("Microsoft YaHei UI", fontStyle);
            }
            catch
            {
                // 如果所有尝试都失败，返回系统默认字体
                return SKTypeface.Default;
            }
        }
        
        /// <summary>
        /// 获取字体样式
        /// </summary>
        private SKFontStyle GetFontStyle(bool isBold, bool isItalic)
        {
            if (isBold && isItalic)
                return SKFontStyle.BoldItalic;
            else if (isBold)
                return SKFontStyle.Bold;
            else if (isItalic)
                return SKFontStyle.Italic;
            else
                return SKFontStyle.Normal;
        }
        
        /// <summary>
        /// 清除字体缓存
        /// </summary>
        public void ClearCache()
        {
            foreach (var typeface in _typefaceCache.Values)
            {
                typeface?.Dispose();
            }
            _typefaceCache.Clear();
        }
    }
}

