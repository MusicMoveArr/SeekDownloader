using System.Dynamic;

namespace SeekDownloader;

public class SearchGroup
{
    public required string TargetArtistName { get; init; }
    public required string TargetAlbumName { get; init; }
    public required List<SearchResult> SearchResults { get; init; } = new List<SearchResult>();
    public required List<string> SongNames { get; init; } = new List<string>();
}