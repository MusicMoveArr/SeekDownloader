using System.Collections.Concurrent;
using SubSonicMedia.Responses.Search.Models;

namespace SeekDownloader.Models;

public class ArtistTrackModel
{
    public string ArtistId { get; set; }
    public string ArtistName { get; set; }
    public ConcurrentBag<Song> AllSongs { get; set; } = new ConcurrentBag<Song>();
}