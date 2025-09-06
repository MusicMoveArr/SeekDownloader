using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;
using SeekDownloader.Helpers;
using SeekDownloader.Models;
using Soulseek;
using File = Soulseek.File;

namespace SeekDownloader.Services;

public class FileSeekService
{
    public List<string> MusicLibraries = new List<string>();
    public Dictionary<string, List<FileInfo>> ArtistMusicLibraries = new Dictionary<string, List<FileInfo>>();
    
    public static string[] MediaFileExtensions =
    [
        "flac",
        "m4a",
        "mp3",
        "wav",
        "aaif",
        "opus"
    ];

    public string LastErrorMessage = string.Empty;

    public async Task<List<SearchResult>> SearchAsync(
        List<SearchTermModel> searchTerms, 
        SoulseekClient client,
        List<string> filterOutNames,
        List<string> searchFileExtensions,
        int musicLibraryMatch,
        int maxFileSize,
        List<string> downloadArchiveList,
        int searchMatchArtistPercentage,
        int searchMatchAlbumPercentage,
        int searchMatchTrackPercentage)
    {
        LastErrorMessage = string.Empty;
        try
        {
            
            SearchTermModel firstSearchTerm = searchTerms.First();
            AddToCache(firstSearchTerm.ArtistName);
            
            List<string> songNames = searchTerms
                .Select(term => term.SongName)
                .Where(term => !string.IsNullOrEmpty(term))
                .ToList()!;
            
            var options = new SearchOptions(
                fileFilter: (file) =>
                {
                    return searchFileExtensions.Any(ext => file.Filename.EndsWith(ext)) &&
                           (filterOutNames == null || filterOutNames?.Any(name => file.Filename.ToLower().Contains(name.ToLower())) == false) &&
                           file.Size < (maxFileSize * 1024 * 1024) &&
                           !AlreadyInLibrary(firstSearchTerm.ArtistName, file.Filename, musicLibraryMatch, searchFileExtensions);
                });
            
            List<SearchResponse> responses = new List<SearchResponse>();
            
            //search by artist + album
            //or search by just the artist
            if (!string.IsNullOrWhiteSpace(firstSearchTerm.AlbumName))
            {
                var searchQueryArtistAlbum = SearchQuery.FromText($"{firstSearchTerm.ArtistName} - {firstSearchTerm.AlbumName}");
                var responseArtistAlbum = await client.SearchAsync(searchQueryArtistAlbum, options: options);
                responses.AddRange(responseArtistAlbum.Responses.ToList());
            }
            else if (!string.IsNullOrWhiteSpace(firstSearchTerm.ArtistName))
            {
                var searchQueryArtist = SearchQuery.FromText($"{firstSearchTerm.ArtistName}");
                var responseArtist = await client.SearchAsync(searchQueryArtist, options: options);
                responses.AddRange(responseArtist.Responses.ToList());
            }
            
            var files = responses
                .SelectMany(x =>
                    x.Files
                        .Select(f => new SearchResult
                        {
                            Username = x.Username,
                            Filename = f.Filename,
                            Size = f.Size,
                            HasFreeUploadSlot = x.HasFreeUploadSlot,
                            UploadSpeed = x.UploadSpeed,
                            PotentialArtistMatch = Fuzz.PartialRatio(f.Filename.ToLower(), firstSearchTerm.ArtistName.ToLower()),
                            PotentialAlbumMatch = string.IsNullOrWhiteSpace(firstSearchTerm.AlbumName) ? 100 : Fuzz.PartialRatio(f.Filename.ToLower(), firstSearchTerm.AlbumName.ToLower()),
                            
                            PotentialTrackMatch = songNames.Count == 0 ? 100 :
                                songNames.Select(name => Fuzz.PartialRatio(f.Filename?.Split(new char []{'\\', '/'}, StringSplitOptions.RemoveEmptyEntries)
                                                                                .LastOrDefault()?.ToLower(), name.ToLower()))
                                    .OrderDescending()
                                    .First(),
                        })
                )
                .Where(file => file.PotentialArtistMatch >= searchMatchArtistPercentage)
                .Where(file => file.PotentialAlbumMatch >= searchMatchAlbumPercentage)
                .Where(file => file.PotentialTrackMatch >= searchMatchTrackPercentage)
                .Where(x => !downloadArchiveList.Contains(GetDownloadArchiveContent(x.Username, x.Size, x.Filename)))
                .DistinctBy(r => new
                {
                    r?.Filename,
                    r?.Username
                })
                .OrderByDescending(file => file.PotentialArtistMatch)
                .ThenByDescending(file => file.PotentialAlbumMatch)
                .ThenByDescending(file => file.PotentialTrackMatch)
                .ThenByDescending(file => file.HasFreeUploadSlot)
                .ThenByDescending(file => file.Size)
                .ThenByDescending(file => file.UploadSpeed)
                .ToList();
            
            return files;
        }
        catch (Exception e)
        {
            LastErrorMessage = e.Message;
        }

        return new List<SearchResult>();
    }
    
    public string GetDownloadArchiveContent(string username, long size, string fileName)
    {
        return $"{username},{size},{fileName}";
    }

    public bool GetTrackName(string fileName, string pattern, ref string trackName)
    {
        Match match = Regex.Match(fileName, pattern);
        if (!match.Success)
        {
            return false;
        }

        if (match.Groups.ContainsKey("track"))
        {
            trackName = match.Groups["track"].Value;
        }
        return !string.IsNullOrWhiteSpace(trackName);
    }

    public string GetGroupPattern(string regex, string name)
    {
        return $"(?<{name}>{regex})";
    }
    
    public string GetSeekTrackName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }
        
        const string TrackGroupName = "track";
        
        string fileExtensionPattern = @"\.(mp3|flac|m4a|opus|wav|aiff)$";
        string trackName = string.Empty;
        fileName = fileName.Contains("\\") ? fileName.Split("\\").Last() : fileName.Split("//").Last();
        fileName = fileName.Replace('–', '-');
        fileName = fileName.Replace('_', ' ');

        //replace at the end of the filename the random letters/numbers like [123ABC] (length of 11) (youtube video id's ?), messes up file detection
        fileName = Regex.Replace(fileName, @"(\[?(?=(?:.*\d))(?=(?:.*[A-Z]))(?=(?:.*[a-z]))[A-Za-z0-9\-]{8,11}\])?(?=\.(mp3|flac|m4a|opus|wav|aiff)$)", "");
        
        string[] patterns = new[]
        {
            //Artist - Album - Track.ext
            $@"^(.+?)\s-\s(.+?)\s-\s(\d{{2}}(?:-\d{{2}})?)\s-\s{GetGroupPattern("(.+?)", TrackGroupName)}{fileExtensionPattern}",
            
            //TrackNumber-DiscNumber Artist - TrackName.ext (dot is optional)
            @$"^(\d{{1,3}})-(\d{{1,3}})[\.]{{0,1}}(.+?)[\s]{{0,}}-[\s]{{0,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackNumber-DiscNumber TrackName.ext (dot is optional)
            @$"^(\d{{1,3}})-(\d{{1,3}})[\.]{{0,1}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackNumber-Artist-TrackName.ext
            @$"^(\d{{1,3}})[\s]{{0,}}-[\s]{{0,}}(.+?)[\s]{{0,}}-[\s]{{0,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackNumber. Artist - TrackName.ext ('.' or '-')
            @$"^(\d{{1,3}})[\s]{{0,}}(.+?)[-\.]{{1}}[\s]{{0,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackNumber. TrackName.ext ('.' or '-')
            @$"^(\d{{1,3}})[\s]{{0,}}[-\.]{{1}}[\s]{{0,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackNumber TrackName.ext (without '.' or '-')
            @$"^(\d{{1,3}})[\s]{{1,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //Artist - Track.ext
            @$"^(.+?)[\s]{{0,}}-[\s]{{0,}}(?<track>(.+?)){fileExtensionPattern}",
            
            //TrackName.ext
            @$"^(?<track>(.+?)){fileExtensionPattern}",
        };

        foreach (string pattern in patterns)
        {
            if (GetTrackName(fileName, pattern, ref trackName))
            {
                if (string.IsNullOrWhiteSpace(trackName
                        .Replace("-", string.Empty)
                        .Replace("(", string.Empty)
                        .Replace(")", string.Empty)
                        .Replace("[", string.Empty)
                        .Replace("]", string.Empty)))
                {
                    continue;
                }
                
                return trackName.Trim();
            }
        }

        return string.Empty;
    }
    
    public bool AlreadyInLibraryByTrack(
        string artistName, 
        string trackName, 
        int musicLibraryMatch, 
        List<string> searchFileExtensions)
    {
        if (ArtistMusicLibraries.ContainsKey(artistName))
        {
            List<FileInfo> musicFiles = ArtistMusicLibraries[artistName];
            
            var similar = musicFiles
                .Select(musicFile => new
                {
                    TrackName = GetSeekTrackName(musicFile.Name),
                    Path = musicFile.FullName,
                })
                .Where(musicFile => !string.IsNullOrWhiteSpace(musicFile.TrackName))
                .Where(musicFile => !searchFileExtensions.Any() || searchFileExtensions.Any(extension => musicFile.Path.EndsWith(extension)))
                .Where(musicFile => FuzzyHelper.ExactNumberMatch(trackName, musicFile.TrackName.ToLower()))
                .FirstOrDefault(musicFile => Fuzz.Ratio(trackName.ToLower(), musicFile.TrackName.ToLower()) > musicLibraryMatch);

            if (similar != null)
            {
                return true;
            }
        }
        return false;
    }
    
    public bool AlreadyInLibrary(
        string artistName, 
        string fileName, 
        int musicLibraryMatch, 
        List<string> searchFileExtensions)
    {
        if (ArtistMusicLibraries.ContainsKey(artistName))
        {
            List<FileInfo> musicFiles = ArtistMusicLibraries[artistName];

            string targetFile = GetSeekTrackName(fileName);

            if (string.IsNullOrWhiteSpace(targetFile))
            {
                //ignore file that cannot be parsed
                return true;
            }
            
            var similar = musicFiles
                .Select(musicFile => new
                {
                    TrackName = GetSeekTrackName(musicFile.Name),
                    Name = musicFile.Name,
                    Path = musicFile.FullName,
                })
                .Where(musicFile => !string.IsNullOrWhiteSpace(musicFile.TrackName))
                .Where(musicFile => !searchFileExtensions.Any() || searchFileExtensions.Any(extension => musicFile.Path.EndsWith(extension)))
                .Where(musicFile => FuzzyHelper.ExactNumberMatch(targetFile, musicFile.TrackName.ToLower()))
                .FirstOrDefault(musicFile => Fuzz.Ratio(targetFile.ToLower(), musicFile.TrackName.ToLower()) > musicLibraryMatch);

            if (similar != null)
            {
                return true;
            }
        }
        return false;
    }
    
    public void AddToCache(string artistName)
    {
        if (string.IsNullOrWhiteSpace(artistName))
        {
            return;
        }
        
        if (!ArtistMusicLibraries.ContainsKey(artistName))
        {
            foreach (string musicLib in MusicLibraries)
            {
                var dirs = System.IO.Directory.GetDirectories(musicLib)
                    .Select(dir => new DirectoryInfo(dir))
                    .Where(dir => dir.Name.ToLower().StartsWith(artistName.ToLower()))
                    .ToList();

                foreach (var dir in dirs)
                {
                    FileInfo[] allFiles = dir
                        .GetFiles("*.*", SearchOption.AllDirectories)
                        .Where(file => file.Extension != ".jpg")
                        .ToArray();
                    
                    if (ArtistMusicLibraries.ContainsKey(artistName))
                    {
                        ArtistMusicLibraries[artistName].AddRange(allFiles.ToList());
                    }
                    else
                    {
                        ArtistMusicLibraries.Add(artistName, allFiles.ToList());
                    }
                }
            }
        }
    }
}