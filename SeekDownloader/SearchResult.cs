namespace SeekDownloader;

public class SearchResult
{
    public required string Username { get; init; }
    public required string Filename { get; init; }
    public required long Size { get; init; }
    public required bool HasFreeUploadSlot { get; init; }
    public required int UploadSpeed { get; init; }
    public required int PotentialArtistMatch { get; init; }
    public required int PotentialAlbumMatch { get; init; }
    public required int PotentialTrackMatch { get; init; }
    public required int PotentialTrackWithoutVersionMatch { get; init; }

    public string? FileNameWithExt
        => Filename?.Split(new char []{'\\', '/'}, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
}