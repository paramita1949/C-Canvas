using System;
using System.IO;
using System.IO.Compression;

namespace BuildTools
{
    /// <summary>
    /// 编译时打包工具 - 独立程序
    /// </summary>
    class PackResources
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: PackResources <source-dir> <output-pak-file>");
                return 1;
            }

            string sourceDir = args[0];
            string outputPak = args[1];

            try
            {
                Console.WriteLine($"[PAK] Packing resources...");
                Console.WriteLine($"[PAK] Source: {sourceDir}");
                Console.WriteLine($"[PAK] Output: {outputPak}");

                CreatePak(sourceDir, outputPak);

                var fileInfo = new FileInfo(outputPak);
                Console.WriteLine($"[PAK] Success! Size: {fileInfo.Length / 1024} KB");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PAK] Error: {ex.Message}");
                return 1;
            }
        }

        static void CreatePak(string sourceDirectory, string outputPakPath)
        {
            const string PAK_MAGIC = "CCANVAS";
            const int PAK_VERSION = 1;

            // 只打包需要的资源文件
            var resourcesToInclude = new[]
            {
                Path.Combine(sourceDirectory, "Fonts"),
                Path.Combine(sourceDirectory, "weixin.png"),
                Path.Combine(sourceDirectory, "pay.png")
            };

            var files = new System.Collections.Generic.List<string>();
            
            // 收集 Fonts 目录下的所有文件
            if (Directory.Exists(Path.Combine(sourceDirectory, "Fonts")))
            {
                files.AddRange(Directory.GetFiles(Path.Combine(sourceDirectory, "Fonts"), "*.*", SearchOption.AllDirectories));
            }
            
            // 收集图片文件
            var imageFile1 = Path.Combine(sourceDirectory, "weixin.png");
            if (File.Exists(imageFile1)) files.Add(imageFile1);
            
            var imageFile2 = Path.Combine(sourceDirectory, "pay.png");
            if (File.Exists(imageFile2)) files.Add(imageFile2);

            Console.WriteLine($"[PAK] Found {files.Count} resource files");
            
            using (var fs = new FileStream(outputPakPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                // 写入头部
                bw.Write(System.Text.Encoding.ASCII.GetBytes(PAK_MAGIC));
                bw.Write(PAK_VERSION);
                bw.Write(files.Count);

                // 预留索引空间
                var indexStartPos = fs.Position;
                fs.Seek(files.Count * 1050, SeekOrigin.Current);

                // 写入文件数据
                var fileInfos = new System.Collections.Generic.List<(string path, long offset, int size, bool compressed)>();

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(sourceDirectory, file);
                    var data = File.ReadAllBytes(file);
                    var offset = fs.Position;

                    // 压缩大文件
                    bool isCompressed = false;
                    if (data.Length > 1024)
                    {
                        var compressed = Compress(data);
                        if (compressed.Length < data.Length * 0.9)
                        {
                            data = compressed;
                            isCompressed = true;
                        }
                    }

                    bw.Write(data);
                    fileInfos.Add((relativePath, offset, data.Length, isCompressed));
                }

                // 写入索引
                var dataEndPos = fs.Position;
                fs.Seek(indexStartPos, SeekOrigin.Begin);

                foreach (var (path, offset, size, compressed) in fileInfos)
                {
                    var pathBytes = System.Text.Encoding.UTF8.GetBytes(path);
                    bw.Write(pathBytes.Length);
                    bw.Write(pathBytes);
                    bw.Write(offset);
                    bw.Write(size);
                    bw.Write(compressed);
                }
            }
        }

        static byte[] Compress(byte[] data)
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
    }
}

