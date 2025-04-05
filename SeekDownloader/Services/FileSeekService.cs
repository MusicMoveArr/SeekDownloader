using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;
using Soulseek;
using File = Soulseek.File;

namespace SeekDownloader.Services;

public class FileSeekService
{
    public List<string> MusicLibraries = new List<string>();
    public Dictionary<string, List<FileInfo>> ArtistMusicLibraries = new Dictionary<string, List<FileInfo>>();
    int maxFileSize = 50 * 1024 * 1024; //50MB
    
    private string[] MediaFileExtensions = new string[]
    {
        "flac",
        "m4a",
        "mp3",
        "wav",
        "aaif",
        "opus"
    };

    public string LastErrorMessage = string.Empty;
    
    public async Task<List<SearchResult>> SearchAsync(string searchString, 
        string songNameTarget, 
        string songArtistTarget, 
        SoulseekClient client,
        List<string> filterOutNames)
    {
        LastErrorMessage = string.Empty;
        try
        {
            AddToCache(songArtistTarget);

            var searchQuery = SearchQuery.FromText(searchString);
            var options = new SearchOptions(
                fileFilter: (file) =>
                {
                    string seekTrackName = GetSeekTrackName(file.Filename.ToLower());
                    return MediaFileExtensions.Any(ext => file.Filename.EndsWith(ext)) &&
                           (filterOutNames == null || filterOutNames?.Any(name => file.Filename.ToLower().Contains(name)) == false) &&
                           //file.Filename.ToLower().Contains(songNameTarget.ToLower()) &&
                           
                           (file.Filename.ToLower().Contains($"{songArtistTarget.ToLower()}") ||
                            file.Filename.ToLower().Contains($"\\{songArtistTarget.ToLower()}\\") ||
                            file.Filename.ToLower().Contains($"//{songArtistTarget.ToLower()}//")) &&
                           
                           file.Size < maxFileSize &&
                           (!string.IsNullOrWhiteSpace(seekTrackName) && 
                            !string.IsNullOrWhiteSpace(songNameTarget) && 
                            Fuzz.Ratio(songNameTarget.ToLower(), seekTrackName) > 70) &&
                           !AlreadyInLibrary(songArtistTarget, file.Filename);
                });
            
            var responses = await client.SearchAsync(searchQuery, options: options);

            var files = responses.Responses
                .SelectMany(x =>
                    x.Files
                        .Select(f => new SearchResult
                        {
                            Username = x.Username,
                            Filename = f.Filename,
                            Size = f.Size,
                            HasFreeUploadSlot = x.HasFreeUploadSlot,
                            UploadSpeed = x.UploadSpeed,
                            TrackName = GetSeekTrackName(f.Filename)
                        })
                        .ToList()
                )
                .Where(file => !string.IsNullOrWhiteSpace(file.TrackName))
                .GroupBy(file => new
                {
                    file.TrackName,
                    extension = file.Filename.Substring(file.Filename.LastIndexOf('.'))
                })
                .Select(file => file.FirstOrDefault())
                .DistinctBy(r => new
                {
                    r.Filename,
                    r.Username
                })
                .OrderByDescending(r => r.HasFreeUploadSlot)
                .ThenByDescending(r => r.Size)
                .ThenByDescending(r => r.UploadSpeed)
                .ToList();
            
            return files;
        }
        catch (Exception e)
        {
            LastErrorMessage = e.Message;
        }

        return new List<SearchResult>();
    }

    private bool GetTrackName(string fileName, string pattern, ref string trackName)
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

    private string GetGroupPattern(string regex, string name)
    {
        return $"(?<{name}>{regex})";
    }
    
    private string GetSeekTrackName(string fileName)
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
    
    private bool AlreadyInLibrary(string artistName, string fileName)
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
                .Select(musicFile => GetSeekTrackName(musicFile.Name))
                .Where(musicFile => !string.IsNullOrWhiteSpace(musicFile))
                .FirstOrDefault(musicFile => Fuzz.Ratio(targetFile.ToLower(), musicFile.ToLower()) > 50);

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