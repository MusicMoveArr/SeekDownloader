using System.Text.RegularExpressions;
using FuzzySharp;
using Soulseek;

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
                    return MediaFileExtensions.Any(ext => file.Filename.EndsWith(ext)) &&
                           !filterOutNames.Any(name => file.Filename.ToLower().Contains(name)) &&
                           //file.Filename.ToLower().Contains(songNameTarget.ToLower()) &&
                           
                           (file.Filename.ToLower().Contains($"{songArtistTarget.ToLower()}") ||
                            file.Filename.ToLower().Contains($"\\{songArtistTarget.ToLower()}\\") ||
                            file.Filename.ToLower().Contains($"//{songArtistTarget.ToLower()}//")) &&
                           
                           file.Size < maxFileSize &&
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
                            TrackName = GetSeekFileName(f.Filename)
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
    
    private string GetSeekFileName(string fileName)
    {
        const string TrackGroupName = "track";
        
        string word_pattern = "([\\w\\s\\d\\p{P}]+)";
        string digit_pattern = "(\\d{1,3})";
        string dash_pattern = "[ ]{0,1}-[ ]{0,1}";
        
        string fileExtensionPattern = "\\.(mp3|flac|m4a|opus)$";
        string Artist_Album_Number_Track_Pattern = $"^{word_pattern}{dash_pattern}{word_pattern}{dash_pattern}{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Number_Artist_Track_Pattern = $"^{digit_pattern}{dash_pattern}{word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Number_Artist_Track_Pattern2 = $"^{digit_pattern}\\. {word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Collection_Year_Artist_Track_Pattern = $"^{word_pattern}{dash_pattern}\\d{{4}}{dash_pattern}{digit_pattern} {word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Artist_Track_Pattern = $"^{word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Disc_Number_Track_Pattern = $"^{digit_pattern}-{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Number_Track_Pattern = $"^{digit_pattern}\\. {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;
        string Number_Track_Pattern2 = $"^{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern;

        string trackName = string.Empty;
        fileName = fileName.Contains("\\") ? fileName.Split("\\").Last() : fileName.Split("//").Last();
        fileName = fileName.Replace('–', '-');
        fileName = fileName.Replace('_', ' ');

        if (Regex.IsMatch(fileName, @"(\[(?=.*[A-Z])(?=.*[a-z])(?=.*\d)[A-Za-z0-9]{11}\])(\.(mp3|flac|m4a|opus))$"))
        {
            
        }
        
        //replace at the end of the filename the random letters/numbers like [123ABC] (length of 11) (youtube video id's ?), messes up file detection
        fileName = Regex.Replace(fileName, @"\[?(?=(?:.*\d))(?=(?:.*[A-Z]))(?=(?:.*[a-z]))[A-Za-z0-9\-]{8,11}\]?(?=\.(mp3|flac|m4a|opus)$)", "");

        string[] patterns = new[]
        {
            $"^{word_pattern}{dash_pattern}{word_pattern}{dash_pattern}{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{digit_pattern}{dash_pattern}{word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{digit_pattern}\\. {word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{word_pattern}{dash_pattern}\\d{{4}}{dash_pattern}{digit_pattern} {word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{word_pattern}{dash_pattern}{GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{digit_pattern}{dash_pattern}{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{digit_pattern}\\. {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
            $"^{digit_pattern} {GetGroupPattern(word_pattern, TrackGroupName)}" + fileExtensionPattern,
        };

        foreach (string pattern in patterns)
        {
            if (GetTrackName(fileName, pattern, ref trackName))
            {
                return trackName;
            }
        }

        return string.Empty;
    }
    
    private bool AlreadyInLibrary(string artistName, string fileName)
    {
        if (ArtistMusicLibraries.ContainsKey(artistName))
        {
            List<FileInfo> musicFiles = ArtistMusicLibraries[artistName];

            string targetFile = GetSeekFileName(fileName);

            if (string.IsNullOrWhiteSpace(targetFile))
            {
                //ignore file that cannot be parsed
                return true;
            }
            
            var similar = musicFiles
                .Where(musicFile => musicFile.Name.Contains('-'))
                .Select(musicFile => musicFile.Name.Split('-', StringSplitOptions.TrimEntries).Last())
                .Select(musicFile => musicFile.Substring(0, musicFile.LastIndexOf('.')))
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
                    FileInfo[] allFiles = dir.GetFiles("*.*", SearchOption.AllDirectories);
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