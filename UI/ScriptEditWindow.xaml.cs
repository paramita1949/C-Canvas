using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Database.Models.Enums;
using MessageBox = System.Windows.MessageBox;

namespace ImageColorChanger.UI
{
    /// <summary>
    /// 脚本编辑窗口（支持关键帧模式和原图模式）
    /// 参考Python版本：script_manager.py 第108-229行（关键帧模式）
    /// 参考Python版本：keytime.py 行2170-2233（原图模式）
    /// </summary>
    public partial class ScriptEditWindow : Window
    {
        private readonly int _imageId;
        private readonly PlaybackMode _mode;
        private readonly List<TimingSequenceDto> _keyframeTimings;
        private readonly List<OriginalTimingSequenceDto> _originalTimings;

        // 关键帧模式构造函数
        public ScriptEditWindow(int imageId, List<TimingSequenceDto> timings)
        {
            InitializeComponent();
            _imageId = imageId;
            _mode = PlaybackMode.Keyframe;
            _keyframeTimings = timings;

            Title = "关键帧脚本编辑";
            // 格式化并显示脚本内容
            ScriptTextBox.Text = FormatKeyframeScriptContent(timings);
        }
        
        // 原图模式构造函数
        public ScriptEditWindow(int imageId, List<OriginalTimingSequenceDto> timings)
        {
            InitializeComponent();
            _imageId = imageId;
            _mode = PlaybackMode.Original;
            _originalTimings = timings;

            Title = "原图模式脚本编辑";
            // 格式化并显示脚本内容
            ScriptTextBox.Text = FormatOriginalScriptContent(timings);
        }

        /// <summary>
        /// 获取关键帧ID到帧号的映射（参考Python版本：script_manager.py 第196-206行）
        /// </summary>
        private Dictionary<int, int> GetKeyframeMapping()
        {
            var mapping = new Dictionary<int, int>();
            
            try
            {
                // 创建临时DbContext获取关键帧
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using var context = new Database.CanvasDbContext(dbPath);
                
                // 获取当前图片的所有关键帧（按Position排序）
                var keyframes = context.Keyframes
                    .Where(k => k.ImageId == _imageId)
                    .OrderBy(k => k.Position)
                    .ToList();
                
                // 建立映射：keyframe_id -> 帧号（从1开始）
                for (int i = 0; i < keyframes.Count; i++)
                {
                    mapping[keyframes[i].Id] = i + 1;
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 获取关键帧映射失败: {ex.Message}");
            }
            
            return mapping;
        }

        /// <summary>
        /// 格式化关键帧脚本内容（参考Python版本：script_manager.py 第183-194行）
        /// </summary>
        private string FormatKeyframeScriptContent(List<TimingSequenceDto> timings)
        {
            // 获取关键帧映射（ID到帧号）
            var keyframeMapping = GetKeyframeMapping();
            
            var lines = new List<string>();
            
            for (int i = 0; i < timings.Count; i++)
            {
                var timing = timings[i];
                // 获取关键帧的实际帧号（1,2,3,4,5）
                var frameNumber = keyframeMapping.ContainsKey(timing.KeyframeId) 
                    ? keyframeMapping[timing.KeyframeId].ToString() 
                    : "?";
                // Python格式: f"{frame_number}  {duration:6.1f}s"
                lines.Add($"{frameNumber}  {timing.Duration,6:F1}s");
            }

            return string.Join(Environment.NewLine, lines);
        }
        
        /// <summary>
        /// 格式化原图模式脚本内容（参考Python版本：keytime.py 行2170-2193）
        /// </summary>
        private string FormatOriginalScriptContent(List<OriginalTimingSequenceDto> timings)
        {
            var lines = new List<string>();
            
            foreach (var timing in timings)
            {
                // Python格式: f"{base_id} -> {similar_id} : {duration:.1f}"
                lines.Add($"{timing.BaseImageId} -> {timing.SimilarImageId} : {timing.Duration:F1}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 保存按钮点击事件（支持关键帧和原图模式）
        /// </summary>
        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (_mode == PlaybackMode.Keyframe)
            {
                await SaveKeyframeScript();
            }
            else
            {
                await SaveOriginalScript();
            }
        }
        
        /// <summary>
        /// 保存关键帧脚本（参考Python版本：script_manager.py 第230-301行）
        /// </summary>
        private async System.Threading.Tasks.Task SaveKeyframeScript()
        {
            try
            {
                // 解析修改后的文本
                var lines = ScriptTextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var newTimings = new List<(int keyframeId, double duration, int sequenceOrder)>();
                
                int lineNum = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    // 解析格式: "1  10.0s" 或 "1 10.0"
                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        MessageBox.Show($"第{lineNum + 1}行格式错误: {line}\n请使用格式: 帧序号 时间", 
                            "格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    try
                    {
                        // 验证帧号（但不使用）
                        _ = int.Parse(parts[0]);
                        
                        // 解析时间
                        var timeStr = parts[1].TrimEnd('s', 'S');
                        var duration = double.Parse(timeStr);

                        if (duration < 0)
                        {
                            throw new ArgumentException("时间不能为负数");
                        }

                        // 使用原始的keyframe_id和sequence_order
                        if (lineNum < _keyframeTimings.Count)
                        {
                            newTimings.Add((
                                _keyframeTimings[lineNum].KeyframeId,
                                duration,
                                _keyframeTimings[lineNum].SequenceOrder
                            ));
                        }

                        lineNum++;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"第{lineNum + 1}行格式错误: {line}\n{ex.Message}\n请使用格式: 帧序号 时间", 
                            "格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (newTimings.Count == 0)
                {
                    MessageBox.Show("没有找到有效的时间数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (newTimings.Count != _keyframeTimings.Count)
                {
                    var result = MessageBox.Show(
                        $"原始数据有{_keyframeTimings.Count}帧，新数据有{newTimings.Count}帧\n是否继续保存？",
                        "确认修改", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // 保存到数据库
                if (await SaveTimingsToDatabase(newTimings))
                {
                    MessageBox.Show("脚本信息已更新", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("无法保存脚本信息到数据库", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存脚本时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// 保存原图模式脚本（参考Python版本：keytime.py 行2195-2233）
        /// </summary>
        private async System.Threading.Tasks.Task SaveOriginalScript()
        {
            try
            {
                // 解析修改后的文本
                var lines = ScriptTextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var newTimings = new List<(int baseImageId, int similarImageId, double duration, int sequenceOrder)>();
                
                int lineNum = 0;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    try
                    {
                        // 解析格式: "100 -> 101 : 2.5"
                        // 支持多种分隔符
                        var parts = trimmed.Split(new[] { "->", ":" }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3)
                        {
                            throw new ArgumentException("格式错误，请使用: from_id -> to_id : duration");
                        }

                        var baseImageId = int.Parse(parts[0].Trim());
                        var similarImageId = int.Parse(parts[1].Trim());
                        var duration = double.Parse(parts[2].Trim());

                        if (duration < 0)
                        {
                            throw new ArgumentException("时间不能为负数");
                        }

                        // 使用原始的sequence_order
                        if (lineNum < _originalTimings.Count)
                        {
                            newTimings.Add((
                                baseImageId,
                                similarImageId,
                                duration,
                                _originalTimings[lineNum].SequenceOrder
                            ));
                        }

                        lineNum++;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"第{lineNum + 1}行格式错误: {line}\n{ex.Message}\n请使用格式: from_id -> to_id : duration", 
                            "格式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (newTimings.Count == 0)
                {
                    MessageBox.Show("没有找到有效的时间数据", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (newTimings.Count != _originalTimings.Count)
                {
                    var result = MessageBox.Show(
                        $"原始数据有{_originalTimings.Count}条记录，新数据有{newTimings.Count}条\n是否继续保存？",
                        "确认修改", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                // 保存到数据库
                if (await SaveOriginalTimingsToDatabase(newTimings))
                {
                    MessageBox.Show("脚本信息已更新", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("无法保存脚本信息到数据库", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存脚本时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 保存时间数据到数据库（关键帧模式，参考Python版本：script_manager.py 第302-341行）
        /// </summary>
        private async System.Threading.Tasks.Task<bool> SaveTimingsToDatabase(List<(int keyframeId, double duration, int sequenceOrder)> newTimings)
        {
            try
            {
                // 创建独立的DbContext实例
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using var context = new Database.CanvasDbContext(dbPath);

                // 开始事务
                using var transaction = await context.Database.BeginTransactionAsync();
                
                try
                {
                    // 1. 删除旧数据
                    var oldTimings = context.KeyframeTimings.Where(t => t.ImageId == _imageId);
                    context.KeyframeTimings.RemoveRange(oldTimings);

                    // 2. 插入新数据（参考Python版本：script_manager.py 第314-327行）
                    // Python版本只插入4个字段，CreatedAt由数据库DEFAULT CURRENT_TIMESTAMP自动设置
                    foreach (var (keyframeId, duration, sequenceOrder) in newTimings)
                    {
                        context.KeyframeTimings.Add(new Database.Models.KeyframeTiming
                        {
                            ImageId = _imageId,
                            KeyframeId = keyframeId,
                            Duration = duration,
                            SequenceOrder = sequenceOrder
                            // ❌ 不要设置CreatedAt，让数据库自动处理
                        });
                    }

                    // 3. 保存更改
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    //System.Diagnostics.Debug.WriteLine($"✅ 已更新图片 {_imageId} 的时间数据，共 {newTimings.Count} 条记录");
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 更新时间数据失败: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 保存原图模式时间数据到数据库
        /// </summary>
        private async System.Threading.Tasks.Task<bool> SaveOriginalTimingsToDatabase(List<(int baseImageId, int similarImageId, double duration, int sequenceOrder)> newTimings)
        {
            try
            {
                // 创建独立的DbContext实例
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pyimages.db");
                using var context = new Database.CanvasDbContext(dbPath);

                // 开始事务
                using var transaction = await context.Database.BeginTransactionAsync();
                
                try
                {
                    // 1. 删除旧数据（base_image_id为当前图片的所有记录）
                    var oldTimings = context.OriginalModeTimings.Where(t => t.BaseImageId == _imageId);
                    context.OriginalModeTimings.RemoveRange(oldTimings);

                    // 2. 插入新数据
                    foreach (var (baseImageId, similarImageId, duration, sequenceOrder) in newTimings)
                    {
                        context.OriginalModeTimings.Add(new Database.Models.OriginalModeTiming
                        {
                            BaseImageId = _imageId,
                            FromImageId = baseImageId,
                            ToImageId = similarImageId,
                            Duration = duration,
                            SequenceOrder = sequenceOrder
                            // CreatedAt由数据库自动处理
                        });
                    }

                    // 3. 保存更改
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    //System.Diagnostics.Debug.WriteLine($"✅ 已更新图片 {_imageId} 的原图模式时间数据，共 {newTimings.Count} 条记录");
                    return true;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"❌ 更新原图模式时间数据失败: {ex.Message}");
                return false;
            }
        }
    }
}

