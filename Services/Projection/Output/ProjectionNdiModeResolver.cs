namespace ImageColorChanger.Services.Projection.Output
{
    /// <summary>
    /// 统一内容类型到传输模式的规则：
    /// - 歌词/圣经可选透明
    /// - 其他内容一律全投影
    /// </summary>
    public sealed class ProjectionNdiModeResolver : IProjectionNdiModeResolver
    {
        private readonly IProjectionNdiConfigProvider _config;

        public ProjectionNdiModeResolver(IProjectionNdiConfigProvider config)
        {
            _config = config;
        }

        public ProjectionNdiTransmissionMode Resolve(ProjectionNdiContentType contentType)
        {
            if (!_config.ProjectionNdiEnabled)
            {
                return ProjectionNdiTransmissionMode.Disabled;
            }

            return contentType switch
            {
                ProjectionNdiContentType.Lyrics => ProjectionNdiTransmissionMode.Transparent,
                ProjectionNdiContentType.Bible => ProjectionNdiTransmissionMode.FullFrame,
                ProjectionNdiContentType.SlideTransparent => ProjectionNdiTransmissionMode.Transparent,
                _ => ProjectionNdiTransmissionMode.FullFrame
            };
        }
    }
}
