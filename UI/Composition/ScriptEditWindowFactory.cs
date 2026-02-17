using System;
using System.Collections.Generic;
using ImageColorChanger.Database.Models.DTOs;
using ImageColorChanger.Repositories.Interfaces;

namespace ImageColorChanger.UI.Composition
{
    /// <summary>
    /// ScriptEditWindow 统一工厂，收敛窗口层的服务解析。
    /// </summary>
    public sealed class ScriptEditWindowFactory
    {
        private readonly ICompositeScriptRepository _compositeScriptRepository;

        public ScriptEditWindowFactory(ICompositeScriptRepository compositeScriptRepository)
        {
            _compositeScriptRepository = compositeScriptRepository ?? throw new ArgumentNullException(nameof(compositeScriptRepository));
        }

        public ScriptEditWindow CreateForKeyframe(int imageId, List<TimingSequenceDto> timings)
        {
            return new ScriptEditWindow(imageId, timings, _compositeScriptRepository);
        }

        public ScriptEditWindow CreateForOriginal(int imageId, List<OriginalTimingSequenceDto> timings)
        {
            return new ScriptEditWindow(imageId, timings, _compositeScriptRepository);
        }
    }
}
