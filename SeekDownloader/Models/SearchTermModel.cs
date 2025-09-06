using SeekDownloader.Enums;

namespace SeekDownloader.Models;

public class SearchTermModel
{
    public SearchTermType SearchTermType { get; set; }
    public string ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public string? SongName { get; set; }
    
    public SearchTermModel(string searchTerm, string searchDelimeter)
    {
        var split = searchTerm.Split(searchDelimeter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (split.Length > 2)
        {
            SearchTermType = SearchTermType.ArtistAlbumTrack;
            SongName = string.Join(searchDelimeter, split.Skip(2).ToList());
            AlbumName = split.Skip(1).First();
            ArtistName = split.First();
        }
        else if (split.Length > 1)
        {
            SearchTermType = SearchTermType.ArtistTrack;
            SongName = string.Join(searchDelimeter, split.Skip(1).ToList());
            ArtistName = split.First();
        }
        else
        {
            SearchTermType = SearchTermType.Artist;
            ArtistName = searchTerm;
        }
    }

    public string SearchTerm
    {
        get
        {
            switch (SearchTermType)
            {
                case SearchTermType.Artist:
                    return ArtistName;
                case SearchTermType.ArtistTrack:
                    return $"{ArtistName} - {SongName}";
                case SearchTermType.ArtistAlbumTrack:
                    return $"{ArtistName} - {AlbumName}";
            }

            return string.Empty;
        }
    }
}