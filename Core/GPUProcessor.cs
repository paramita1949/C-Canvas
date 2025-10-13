using System;
using ComputeSharp;
using SixLabors.ImageSharp;
using ImageSharpRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace ImageColorChanger.Core
{
    /// <summary>
    /// GPU兼容的颜色结构（使用uint打包RGBA）
    /// </summary>
    public struct GpuColor
    {
        public uint PackedValue;

        public GpuColor(byte r, byte g, byte b, byte a)
        {
            PackedValue = ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
        }

        public readonly byte R => (byte)(PackedValue & 0xFF);
        public readonly byte G => (byte)((PackedValue >> 8) & 0xFF);
        public readonly byte B => (byte)((PackedValue >> 16) & 0xFF);
        public readonly byte A => (byte)((PackedValue >> 24) & 0xFF);
    }

    /// <summary>
    /// GPU加速图像处理器 - 使用ComputeSharp
    /// </summary>
    public class GPUProcessor : IDisposable
    {
        private GraphicsDevice device;
        public bool IsInitialized { get; private set; }

        public bool Initialize()
        {
            try
            {
                device = GraphicsDevice.GetDefault();
                IsInitialized = device != null;
                return IsInitialized;
            }
            catch
            {
                IsInitialized = false;
                return false;
            }
        }

        public Image<ImageSharpRgba32> ProcessImage(Image<ImageSharpRgba32> sourceImage, ImageSharpRgba32 targetColor, bool convertWhiteToBlack)
        {
            if (device == null || !IsInitialized)
                throw new InvalidOperationException("GPU未初始化");

            var result = sourceImage.Clone();
            int width = result.Width;
            int height = result.Height;

            // 将图像数据复制到GPU缓冲区
            using var inputBuffer = device.AllocateReadWriteBuffer<GpuColor>(width * height);
            using var outputBuffer = device.AllocateReadWriteBuffer<GpuColor>(width * height);

            // 复制图像到GPU
            var tempData = new GpuColor[width * height];
            result.ProcessPixelRows(accessor =>
            {
                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var p = row[x];
                        tempData[index++] = new GpuColor(p.R, p.G, p.B, p.A);
                    }
                }
            });
            inputBuffer.CopyFrom(tempData);

            // 转换目标颜色
            var gpuTargetColor = new GpuColor(targetColor.R, targetColor.G, targetColor.B, targetColor.A);

            // 在GPU上运行颜色映射着色器
            // 使用2D调度以避免超过1D限制（ComputeSharp的For有65535的限制）
            device.For(width, height, new ColorMappingShader2D(
                inputBuffer,
                outputBuffer,
                gpuTargetColor,
                convertWhiteToBlack ? 1 : 0,
                width
            ));

            // 在GPU上运行边缘增强着色器
            device.For(width, height, new EdgeEnhanceShader2D(
                outputBuffer,
                inputBuffer,
                width,
                height
            ));

            // 复制结果回CPU
            inputBuffer.CopyTo(tempData);
            result.ProcessPixelRows(accessor =>
            {
                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var p = tempData[index++];
                        row[x] = new ImageSharpRgba32(p.R, p.G, p.B, p.A);
                    }
                }
            });

            return result;
        }

        public void Dispose()
        {
            device?.Dispose();
        }
    }

    /// <summary>
    /// GPU颜色映射着色器 (2D版本)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ColorMappingShader2D(
        ReadWriteBuffer<GpuColor> input,
        ReadWriteBuffer<GpuColor> output,
        GpuColor targetColor,
        int convertWhiteToBlack,
        int width) : IComputeShader
    {
        public void Execute()
        {
            int x = ThreadIds.X;
            int y = ThreadIds.Y;
            int i = y * width + x;
            
            // 检查索引是否有效
            if (i >= input.Length)
                return;
            
            uint packed = input[i].PackedValue;

            // 解包颜色
            uint r = packed & 0xFF;
            uint g = (packed >> 8) & 0xFF;
            uint b = (packed >> 16) & 0xFF;
            uint a = (packed >> 24) & 0xFF;

            // 计算亮度
            float luminance = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;

            // 解包目标颜色
            uint targetR = targetColor.PackedValue & 0xFF;
            uint targetG = (targetColor.PackedValue >> 8) & 0xFF;
            uint targetB = (targetColor.PackedValue >> 16) & 0xFF;

            if (convertWhiteToBlack == 1)
            {
                // 白底变黑底模式
                if (luminance > 0.85f)
                {
                    r = g = b = 0;
                }
                else if (luminance < 0.6f)
                {
                    float factor = (1.0f - luminance);
                    factor = Hlsl.Pow(factor, 0.7f);
                    factor = Hlsl.Max(0.5f, Hlsl.Min(1.2f, factor * 1.5f));

                    r = (uint)Hlsl.Min(255, targetR * factor);
                    g = (uint)Hlsl.Min(255, targetG * factor);
                    b = (uint)Hlsl.Min(255, targetB * factor);
                }
                else
                {
                    float transitionFactor = (luminance - 0.6f) / 0.25f;
                    transitionFactor = Hlsl.Max(0, Hlsl.Min(1, transitionFactor));

                    uint tR = (uint)(targetR * 0.7f);
                    uint tG = (uint)(targetG * 0.7f);
                    uint tB = (uint)(targetB * 0.7f);

                    r = (uint)(tR * (1 - transitionFactor));
                    g = (uint)(tG * (1 - transitionFactor));
                    b = (uint)(tB * (1 - transitionFactor));
                }
            }
            else
            {
                // 普通模式
                if (luminance < 0.75f)
                {
                    float factor = 1.0f - luminance * 0.8f;
                    factor = Hlsl.Pow(factor, 0.65f);
                    factor = Hlsl.Max(0.6f, Hlsl.Min(1.3f, factor * 1.4f));

                    r = (uint)Hlsl.Min(255, targetR * factor);
                    g = (uint)Hlsl.Min(255, targetG * factor);
                    b = (uint)Hlsl.Min(255, targetB * factor);
                }
                else
                {
                    r = g = b = 255;
                }
            }

            // 重新打包颜色
            GpuColor result;
            result.PackedValue = (a << 24) | (b << 16) | (g << 8) | r;
            output[i] = result;
        }
    }

    /// <summary>
    /// GPU边缘增强着色器 (2D版本)
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct EdgeEnhanceShader2D(
        ReadWriteBuffer<GpuColor> input,
        ReadWriteBuffer<GpuColor> output,
        int width,
        int height) : IComputeShader
    {
        public void Execute()
        {
            int x = ThreadIds.X;
            int y = ThreadIds.Y;
            int i = y * width + x;
            
            // 检查索引是否超出图像范围
            if (x >= width || y >= height)
                return;

            // 边界像素不处理
            if (x < 1 || x >= width - 1 || y < 1 || y >= height - 1)
            {
                output[i] = input[i];
                return;
            }

            uint packed = input[i].PackedValue;
            uint r = packed & 0xFF;
            uint g = (packed >> 8) & 0xFF;
            uint b = (packed >> 16) & 0xFF;
            uint a = (packed >> 24) & 0xFF;

            float currentLum = (0.299f * r + 0.587f * g + 0.114f * b) / 255f;

            // 拉普拉斯算子边缘增强
            int centerR = (int)(r * 5);
            int centerG = (int)(g * 5);
            int centerB = (int)(b * 5);

            uint p1 = input[i - 1].PackedValue;
            uint p2 = input[i + 1].PackedValue;
            uint p3 = input[i - width].PackedValue;
            uint p4 = input[i + width].PackedValue;

            int surroundR = (int)((p1 & 0xFF) + (p2 & 0xFF) + (p3 & 0xFF) + (p4 & 0xFF));
            int surroundG = (int)(((p1 >> 8) & 0xFF) + ((p2 >> 8) & 0xFF) + ((p3 >> 8) & 0xFF) + ((p4 >> 8) & 0xFF));
            int surroundB = (int)(((p1 >> 16) & 0xFF) + ((p2 >> 16) & 0xFF) + ((p3 >> 16) & 0xFF) + ((p4 >> 16) & 0xFF));

            float sharpenAmount = (currentLum > 0.05f && currentLum < 0.95f) ? 0.5f : 0.3f;

            int newR = (int)(r + (centerR - surroundR) * sharpenAmount);
            int newG = (int)(g + (centerG - surroundG) * sharpenAmount);
            int newB = (int)(b + (centerB - surroundB) * sharpenAmount);

            // 对暗色文字进行额外提亮
            if (currentLum > 0.1f && currentLum < 0.6f)
            {
                float boost = 1.15f;
                newR = (int)(newR * boost);
                newG = (int)(newG * boost);
                newB = (int)(newB * boost);
            }

            uint finalR = (uint)Hlsl.Clamp(newR, 0, 255);
            uint finalG = (uint)Hlsl.Clamp(newG, 0, 255);
            uint finalB = (uint)Hlsl.Clamp(newB, 0, 255);

            GpuColor result;
            result.PackedValue = (a << 24) | (finalB << 16) | (finalG << 8) | finalR;
            output[i] = result;
        }
    }
}
