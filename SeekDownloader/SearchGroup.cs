namespace SeekDownloader;

public class SearchGroup
{
    public string TargetArtistName { get; set; }
    public string TargetAlbumName { get; set; }
    public string TargetSongName { get; set; }
    public List<SearchResult> SearchResults = new List<SearchResult>();
    public List<string> SongNames = new List<string>();
}