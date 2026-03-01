namespace ImageColorChanger.Services.Projection.Output
{
    public interface IProjectionNdiModeResolver
    {
        ProjectionNdiTransmissionMode Resolve(ProjectionNdiContentType contentType);
    }
}

