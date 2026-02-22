using System;
using System.Collections.Generic;
using System.Linq;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 屏幕管理相关逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 初始化屏幕信息
        /// </summary>
        private void InitializeScreenInfo()
        {
            try
            {
#if DEBUG
                // System.Diagnostics.Debug.WriteLine("========================================");
                // System.Diagnostics.Debug.WriteLine(" [投影管理器] 开始检测显示器");
                // System.Diagnostics.Debug.WriteLine("========================================");
#endif
                // 优先 EnumDisplayDevices/EnumDisplaySettings，失败时回退主屏幕
                _screens = WpfScreenHelper.GetAllScreens();

                if (_screens.Count == 0)
                {
                    _screens.Add(WpfScreenHelper.GetPrimaryScreen());
                }

#if DEBUG
                // System.Diagnostics.Debug.WriteLine("");
                // System.Diagnostics.Debug.WriteLine("========================================");
                // System.Diagnostics.Debug.WriteLine($" [投影管理器] 最终显示器列表，共 {_screens.Count} 个显示器");
                for (int i = 0; i < _screens.Count; i++)
                {
                    var screen = _screens[i];
                    // System.Diagnostics.Debug.WriteLine($"   [{i}] {screen}");
                }
                // System.Diagnostics.Debug.WriteLine("========================================");
#endif

                UpdateScreenComboBox();
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($" [投影管理器] 初始化屏幕信息失败: {ex.Message}");
#else
                _ = ex;
#endif
                _screens.Add(WpfScreenHelper.GetPrimaryScreen());
            }
        }

        /// <summary>
        /// 更新屏幕下拉框
        /// </summary>
        private void UpdateScreenComboBox()
        {
            if (_screenComboBox == null)
            {
                return;
            }

            RunOnMainDispatcher(() =>
            {
                _screenComboBox.Items.Clear();

                for (int i = 0; i < _screens.Count; i++)
                {
                    var screen = _screens[i];
                    string name = screen.IsPrimary ? "主显示器" : $"显示器{i + 1}";
                    _screenComboBox.Items.Add(name);
                }

                int defaultIndex = 0;
                for (int i = 0; i < _screens.Count; i++)
                {
                    if (!_screens[i].IsPrimary)
                    {
                        defaultIndex = i;
                        break;
                    }
                }

                if (_screenComboBox.Items.Count > 0)
                {
                    _screenComboBox.SelectedIndex = defaultIndex;
                }
            });
        }

        /// <summary>
        /// 获取显示器信息列表
        /// </summary>
        public List<(string Name, bool IsPrimary, int Width, int Height)> GetMonitorInfo()
        {
            var monitors = new List<(string, bool, int, int)>();
            for (int i = 0; i < _screens.Count; i++)
            {
                var screen = _screens[i];
                string name = screen.IsPrimary ? "主显示器" : $"显示器{i + 1}";
                monitors.Add((name, screen.IsPrimary, screen.PhysicalWidth, screen.PhysicalHeight));
            }

            return monitors;
        }

        /// <summary>
        /// 获取当前投影显示器的物理像素分辨率（用于高质量渲染）
        /// </summary>
        public (int width, int height) GetCurrentProjectionPhysicalSize()
        {
            var metrics = GetCurrentProjectionScreenMetrics();
            return (metrics.PhysicalWidth, metrics.PhysicalHeight);
        }

        /// <summary>
        /// 获取当前投影显示器分辨率（WPF 设备独立单位）
        /// </summary>
        public (int width, int height) GetCurrentProjectionSize()
        {
            var metrics = GetCurrentProjectionScreenMetrics();
            return (metrics.WpfWidth, metrics.WpfHeight);
        }

        private WpfScreenInfo GetCurrentProjectionScreenOrNull()
        {
            if (_screens == null || _screens.Count == 0)
            {
                return null;
            }

            if (_currentScreenIndex >= 0 && _currentScreenIndex < _screens.Count)
            {
                return _screens[_currentScreenIndex];
            }

            return _screens.FirstOrDefault();
        }

        private ProjectionScreenMetrics GetCurrentProjectionScreenMetrics()
        {
            var screen = GetCurrentProjectionScreenOrNull();
            if (screen == null)
            {
                return new ProjectionScreenMetrics(
                    DefaultProjectionWidth,
                    DefaultProjectionHeight,
                    DefaultProjectionWidth,
                    DefaultProjectionHeight);
            }

            if (_projectionWindow != null)
            {
                screen.UpdateDpiScale(_projectionWindow);
            }

            return new ProjectionScreenMetrics(
                screen.PhysicalWidth,
                screen.PhysicalHeight,
                screen.WpfWidth,
                screen.WpfHeight);
        }
    }
}


