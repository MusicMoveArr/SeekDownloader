namespace SeekDownloader;

public class SearchResult
{
    public required string Username { get; init; }
    public required string Filename { get; init; }
    public required long Size { get; init; }
    public required bool HasFreeUploadSlot { get; init; }
    public required int UploadSpeed { get; init; }
    public required string TrackName { get; init; }
}