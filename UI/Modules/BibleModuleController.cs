using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using ImageColorChanger.Core;
using ImageColorChanger.Services.Implementations;
using ImageColorChanger.Services.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace ImageColorChanger.UI.Modules
{
    /// <summary>
    /// 圣经模块控制器：负责 BibleService 创建与数据库可用性探测。
    /// </summary>
    public sealed class BibleModuleController
    {
        private readonly Dispatcher _dispatcher;

        public BibleModuleController(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public IBibleService CreateService(IMemoryCache memoryCache, ConfigManager configManager)
        {
            if (memoryCache == null)
            {
                throw new ArgumentNullException(nameof(memoryCache));
            }

            if (configManager == null)
            {
                throw new ArgumentNullException(nameof(configManager));
            }

            return new BibleService(memoryCache, configManager);
        }

        public void StartDatabaseAvailabilityProbe(IBibleService bibleService, Action onDatabaseUnavailable)
        {
            if (bibleService == null)
            {
                throw new ArgumentNullException(nameof(bibleService));
            }

            if (onDatabaseUnavailable == null)
            {
                throw new ArgumentNullException(nameof(onDatabaseUnavailable));
            }

            _ = Task.Run(async () =>
            {
                bool available = false;
                try
                {
                    available = await bibleService.IsDatabaseAvailableAsync();
                }
                catch
                {
                    available = false;
                }

                if (!available)
                {
                    _dispatcher.Invoke(onDatabaseUnavailable);
                }
            });
        }
    }
}
