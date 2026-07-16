using FuzzySharp;
using Microsoft.Extensions.Caching.Memory;
using SeekDownloader.Helpers;
using SeekDownloader.Models;
using SubSonicMedia;
using SubSonicMedia.Models;
using SubSonicMedia.Responses.Browsing;
using SubSonicMedia.Responses.Search;

namespace SeekDownloader.Services;

public class SubSonicService
{
    private readonly string _hostname;
    private readonly string _username;
    private readonly string _password;
    private readonly MemoryCache _cache;
    private const int MatchPercentage = 90;
    
    private readonly MemoryCacheEntryOptions _cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    };
    
    public SubSonicService(string hostname, string username, string password)
    {
        this._hostname = hostname;
        this._username = username;
        this._password = password;
        
        var options = new MemoryCacheOptions();
        _cache = new MemoryCache(options);
    }

    public bool AlreadyInLibrary(string artist, string? album, string title)
    {
        if (string.IsNullOrWhiteSpace(_hostname) || 
            string.IsNullOrWhiteSpace(_username) || 
            string.IsNullOrWhiteSpace(_password))
        {
            return false;
        }
        
        var keys = _cache.Keys
            .Where(k => k.ToString()?.StartsWith(artist, StringComparison.OrdinalIgnoreCase) == true)
            .Select(k => k.ToString())
            .ToList();

        foreach (var key in keys)
        {
            if (!_cache.TryGetValue(key, out ArtistTrackModel artistTrackModel))
            {
                continue;
            }

            bool exists = artistTrackModel.AllSongs
                .Where(song => string.IsNullOrWhiteSpace(album) || Fuzz.PartialRatio(song.Album, album) > MatchPercentage)
                .Where(song => Fuzz.PartialRatio(song.Title, title) > MatchPercentage)
                .Any();
            
            if (exists)
            {
                return true;
            }
        }

        return false;
    }

    public void PopulateArtistCache(string artist)
    {
        if (string.IsNullOrWhiteSpace(_hostname) || 
            string.IsNullOrWhiteSpace(_username) || 
            string.IsNullOrWhiteSpace(_password))
        {
            return;
        }

        var connection = new SubsonicConnectionInfo(
            serverUrl: _hostname,
            username: _username,
            password: _password
        );
        
        using var client = new SubsonicClient(connection);

        string searchKey = $"search_{artist}";
        if (!_cache.TryGetValue(searchKey, out Search3Response? artistSearchResponse))
        {
            artistSearchResponse = client.Search.Search3Async(artist, songCount: 0, albumCount: 0, artistCount: 10).Result;
            _cache.Set(searchKey, artistSearchResponse, _cacheOptions);
        }
        
        foreach (var artistResult in artistSearchResponse.SearchResult.Artists
                     .Where(a => Fuzz.PartialRatio(a.Name.ToLower(), artist.ToLower()) >= MatchPercentage)
                     .Where(a => FuzzyHelper.ExactNumberMatch(a.Name, artist)))
        {
            ArtistTrackModel artistTrackModel = new ArtistTrackModel();
            artistTrackModel.ArtistId = artistResult.Id;
            artistTrackModel.ArtistName = artistResult.Name;
            string searchArtistKey = $"{artistResult.Name}{artistTrackModel.ArtistId}";
            
            if (!_cache.TryGetValue(searchArtistKey, out _))
            {
                try
                {
                    var artistInfo = client.Browsing.GetArtistAsync(artistResult.Id).Result;
                    var albumList = artistInfo.Artist.Album
                        .Where(a => Fuzz.PartialRatio(a.Artist.ToLower(), artist.ToLower()) >= MatchPercentage)
                        .DistinctBy(a => a.Id)
                        .ToList();

                    Parallel.ForEach(albumList, new ParallelOptions() { MaxDegreeOfParallelism = 4 }, albumSummary =>
                    {
                        try
                        {
                            string getAlbumKey = $"album_{artistResult.Id}_{albumSummary.Id}";
                            if (!_cache.TryGetValue(getAlbumKey, out AlbumResponse? albumInfo))
                            {
                                albumInfo = client.Browsing.GetAlbumAsync(albumSummary.Id).Result;
                                _cache.Set(getAlbumKey, albumInfo, _cacheOptions);
                            }
                            
                            foreach (var song in albumInfo.Album.Song.Where(s => Fuzz.PartialRatio(s.Artist.ToLower(), artist.ToLower()) >= MatchPercentage))
                            {
                                artistTrackModel.AllSongs.Add(song);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message + "\r\n" + e.StackTrace);
                        }
                    });
                    _cache.Set(searchArtistKey, artistTrackModel, _cacheOptions);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message + "\r\n" + e.StackTrace);
                }
            }
        }
    }
}