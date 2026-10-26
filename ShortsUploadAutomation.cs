using System;
namespace ShortsPublisher;
public sealed class ShortsUploadAutomation(PublisherOptions options) : IDisposable
{
    private readonly YouTubePublisher _youtube = new(options);
    public UploadObservationResult PublishVideoOnYouTubeAsync(string fileName, string caption) =>
        _youtube.PublishVideoOnYouTubeAsync(fileName, caption);
    public void Dispose() => _youtube.Dispose();
}
