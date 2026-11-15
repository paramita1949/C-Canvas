using System;
using ImageColorChanger.Database.Models;
using ImageColorChanger.Services.Lyrics;

namespace ImageColorChanger.UI
{
    public partial class MainWindow
    {
        private LyricsDocument _currentLyricsDocument;
        private ImageLyricsService _imageLyricsService;
        private int _loadedImageLyricsId;

        /// <summary>共用编辑/渲染文档，歌曲元数据不再承载图片所有权。</summary>
        private LyricsDocument LoadLyricsEntryDocument()
        {
            EnsureLyricsProjectManager();
            _imageLyricsService ??= new ImageLyricsService(_dbContext);
            _currentLyricsProject = null;
            if (_lyricsEntryKind == LyricsEntryKind.ImageBound)
            {
                _currentLyricsProjectId = 0;
                _currentLyricsDocument = _imageLyricsService.Load(_lyricsEntryId);
                _loadedImageLyricsId = _imageLyricsService.GetRecordId(_lyricsEntryId);
            }
            else
            {
                _currentLyricsProject = _currentLyricsProjectId > 0 ? _lyricsProjectManager.FindById(_currentLyricsProjectId) : null;
                if (_currentLyricsProject?.ImageId != null)
                    throw new InvalidOperationException("图片私有歌词不能作为独立歌曲打开。");
                _currentLyricsDocument = LyricsDocument.FromProject(_currentLyricsProject);
            }
            return _currentLyricsDocument;
        }

        /// <summary>保存与回读校验在同一个边界完成；禁止界面直接修补混合歌词表。</summary>
        private void PersistLyricsEntryDocument(LyricsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            EnsureLyricsProjectManager();
            LyricsDocument saved;
            if (_lyricsEntryKind == LyricsEntryKind.ImageBound)
            {
                _imageLyricsService ??= new ImageLyricsService(_dbContext);
                _imageLyricsService.SaveLoaded(_lyricsEntryId, document, _loadedImageLyricsId);
                _loadedImageLyricsId = _imageLyricsService.GetRecordId(_lyricsEntryId);
                saved = _imageLyricsService.Load(_lyricsEntryId);
            }
            else
            {
                if (_currentLyricsProject == null) throw new InvalidOperationException("独立歌曲入口不存在，未保存。");
                if (_currentLyricsProject.Id > 0 && _lyricsProjectManager.FindById(_currentLyricsProject.Id) == null)
                    throw new InvalidOperationException("独立歌曲已删除，未重新创建。");
                _currentLyricsProject.ImageId = null; _currentLyricsProject.SourceType = 1;
                document.ApplyTo(_currentLyricsProject); _currentLyricsProject.ModifiedTime = DateTime.Now;
                _lyricsProjectManager.Save(_currentLyricsProject);
                _currentLyricsProjectId = _currentLyricsProject.Id;
                saved = LyricsDocument.FromProject(_lyricsProjectManager.FindById(_currentLyricsProject.Id));
            }
            if (!document.SameAs(saved)) throw new InvalidOperationException("保存内容校验失败，歌词仍保留在编辑器中，请重试保存。");
            _currentLyricsDocument = document;
        }
    }
}
