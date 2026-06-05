using System;

namespace ImageColorChanger.Managers
{
    /// <summary>
    /// ProjectionManager 投影试用计时与账号巡检逻辑（部分类）。
    /// </summary>
    public partial class ProjectionManager
    {
        /// <summary>
        /// 停止投影健康计时器。基础投影已不再启用账号试用/过期巡检。
        /// </summary>
        private void StopProjectionTimer()
        {
            _projectionTimer?.Dispose();
            _projectionTimer = null;
        }
    }
}
