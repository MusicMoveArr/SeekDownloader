using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using SeekDownloader.Services;
using Soulseek;

namespace SeekDownloader.Commands;

[Command("", Description = "A simple to use, commandline tool, for downloading from the SoulSeek network")]
public class RootCommand : ICommand
{
    [CommandOption("download-file-path", 
        Description = "Download path to store the downloads.", 
        EnvironmentVariable = "SEEK_DOWNLOADFILEPATH",
        IsRequired = true)]
    public required string DownloadFilePath { get; init; }
    
    [CommandOption("soulseek-listen-port", 
        Description = "Soulseek listen port (used for portforwarding).", 
        EnvironmentVariable = "SEEK_SOULSEEKLISTENPORT",
        IsRequired = true)]
    public required int SoulseekListenPort { get; init; }
    
    [CommandOption("soulseek-username", 
        Description = "Soulseek username for login.", 
        EnvironmentVariable = "SEEK_SOULSEEKUSERNAME",
        IsRequired = true)]
    public required string SoulseekUsername { get; init; }
    
    [CommandOption("soulseek-password", 
        Description = "Soulseek password for login.", 
        EnvironmentVariable = "SEEK_SOULSEEKPASSWORD",
        IsRequired = true)]
    public required string SoulseekPassword { get; init; }
    
    [CommandOption("search-delimeter", 
        Description = "Search term(s) delimeter is used to take the correct Artist, Album, Track names from your Search Term(s).", 
        EnvironmentVariable = "SEEK_SEARCHDELIMETER")]
    public string SearchDelimeter { get; set; } = "-";
    
    [CommandOption("music-library", 
        Description = "Music Library path to use to check for existing local songs.", 
        EnvironmentVariable = "SEEK_MUSICLIBRARY")]
    public string MusicLibrary { get; set; } = string.Empty;
    
    [CommandOption("search-term", 
        Description = "Search term used to search for music use the order, Artist - Album - Track.", 
        EnvironmentVariable = "SEEK_SEARCHTERM")]
    public string SearchTerm { get; set; } = string.Empty;

    [CommandOption("search-file-path",
        Description = "Search term(s) used to search for music use from a file.",
        EnvironmentVariable = "SEEK_SEARCHFILEPATH")]
    public string SearchFilePath { get; set; } = string.Empty;

    [CommandOption("thread-count",
        Description = "Download threads to use.",
        EnvironmentVariable = "SEEK_THREADCOUNT")]
    public int ThreadCount { get; set; } = 10;
    
    [CommandOption("grouped-downloads", 
        Description = "Put each search into his own download thread.", 
        EnvironmentVariable = "SEEK_GROUPEDDOWNLOADS")]
    public bool GroupedDownloads { get; set; } = false;
    
    [CommandOption("download-singles", 
        Description = "When combined with Grouped Downloads, it will quit downloading the entire group after 1 song finished downloading.", 
        EnvironmentVariable = "SEEK_DOWNLOADSINGLES")]
    public bool DownloadSingles { get; set; } = false;
    
    [CommandOption("update-album-name", 
        Description = "Update the Album name's tag by your search term, only updates if Trackname matches as well for +90%.", 
        EnvironmentVariable = "SEEK_UPDATEALBUMNAME")]
    public bool UpdateAlbumName { get; set; } = false;
    
    [CommandOption("music-libraries", 
        Description = "Multiple Music Library path(s) to use to check for existing local songs.", 
        EnvironmentVariable = "SEEK_MUSICLIBRARIES")]
    public List<string> MusicLibraries { get; set; } = null;

    [CommandOption("filter-out-file-names",
        Description = "Filter out names to ignore for downloads.",
        EnvironmentVariable = "SEEK_FILTEROUTFILENAMES")]
    public List<string> FilterOutFileNames { get; set; } = null;
    
    [CommandOption("check-tags", 
        Description = "Check the tags if we downloaded the correct track.", 
        EnvironmentVariable = "SEEK_CHECKTAGS")]
    public bool CheckTags { get; set; } = false;

    [CommandOption("check-tags-delete",
        Description = "If the tags do not match the search, delete after download.",
        EnvironmentVariable = "SEEK_CHECKTAGSDELETE")]
    public bool CheckTagsDelete { get; set; } = false;

    [CommandOption("output-status",
        Description = "Output the overall status and of each thread.",
        EnvironmentVariable = "SEEK_OUTPUTSTATUS")]
    public bool OutputStatus { get; set; } = true;
    
    [CommandOption("search-file-extensions",
        Description = "Search for specific file extensions.",
        EnvironmentVariable = "SEEK_FILEEXTENSIONS")]
    public List<string> SearchFileExtensions { get; set; } = FileSeekService.MediaFileExtensions.ToList();
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        FileSeekService fileSeeker = new FileSeekService();
        DownloadService downloadService = new DownloadService();
        downloadService.SoulSeekUsername = SoulseekUsername;
        downloadService.SoulSeekPassword = SoulseekPassword;
        downloadService.ThreadCount = ThreadCount;
        downloadService.NicotineListenPort = SoulseekListenPort;
        downloadService.DownloadFolderNicotine = DownloadFilePath;
        downloadService.DownloadSingles = DownloadSingles;
        downloadService.UpdateAlbumName = UpdateAlbumName;
        downloadService.CheckTags = CheckTags;
        downloadService.CheckTagsDelete = CheckTagsDelete;
        downloadService.OutputStatus = OutputStatus;
        
        if (!string.IsNullOrWhiteSpace(MusicLibrary))
        {
            fileSeeker.MusicLibraries.Add(MusicLibrary);
        }
        if (MusicLibraries?.Count > 0)
        {
            fileSeeker.MusicLibraries.AddRange(MusicLibraries);
        }
        
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            downloadService.MissingNames.Add(SearchTerm);
        }
        if (!string.IsNullOrWhiteSpace(SearchFilePath))
        {
            downloadService.MissingNames.AddRange((await System.IO.File.ReadAllLinesAsync(SearchFilePath))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct());
        }
        
        await downloadService.ConnectAsync();
        downloadService.StartThreads();
        
        foreach (string name in downloadService.MissingNames)
        {
            var split = name.Split(SearchDelimeter, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string songNameTarget = string.Empty;
            string songAlbumTarget = string.Empty;
            string songArtistTarget = string.Empty;
            
            if (split.Length > 2)
            {
                songNameTarget = string.Join(SearchDelimeter,split.Skip(2).ToList());
                songAlbumTarget = split.Skip(1).First();
                songArtistTarget = split.First();
            }
            else if (split.Length > 1)
            {
                songNameTarget = string.Join(SearchDelimeter, split.Skip(1).ToList());
                songAlbumTarget = split.First();
            }
            else
            {
                songArtistTarget = name;
            }

            while (downloadService.InQueueCount > 100)
            {
                Thread.Sleep(1000);
            }

            List<string> reconstructedName = new List<string>();
            if (!string.IsNullOrWhiteSpace(songArtistTarget))
            {
                reconstructedName.Add(songArtistTarget);
            }
            if (!string.IsNullOrWhiteSpace(songAlbumTarget))
            {
                reconstructedName.Add(songAlbumTarget);
            }
            if (!string.IsNullOrWhiteSpace(songNameTarget))
            {
                reconstructedName.Add(songNameTarget);
            }

            string tempSearchTerm = string.Join(" - ", reconstructedName);
            
            downloadService.SeekCount++;
            downloadService.CurrentlySeeking = tempSearchTerm;

            var results = await fileSeeker.SearchAsync(tempSearchTerm, songNameTarget, songArtistTarget,
                downloadService.SoulClient, FilterOutFileNames, SearchFileExtensions);
            
            if (!string.IsNullOrWhiteSpace(fileSeeker.LastErrorMessage) 
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.Connected.ToString())
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.LoggedIn.ToString()))
            {
                await downloadService.ConnectAsync();
            }
            
            if (results.Any())
            {
                downloadService.SeekSuccessCount++;
            }

            if (!OutputStatus)
            {
                Console.WriteLine($"Seeked: '{tempSearchTerm}, Found {results.Count} files");
            }
            
            if (results.Count > 0)
            {
                if (GroupedDownloads)
                {
                    downloadService.EnqueueDownload(new SearchGroup()
                    {
                        SearchResults = results,
                        TargetAlbumName = songAlbumTarget,
                        TargetArtistName = songArtistTarget,
                        TargetSongName = songNameTarget,
                        SongNames = new List<string>()
                    });
                }
                else
                {
                    foreach (var result in results)
                    {
                        downloadService.EnqueueDownload(new SearchGroup()
                        {
                            SearchResults = new List<SearchResult>([result]),
                            TargetAlbumName = songAlbumTarget,
                            TargetArtistName = songArtistTarget,
                            TargetSongName = songNameTarget,
                            SongNames = new List<string>()
                        });
                    }
                }
            }
        }
        
        while (downloadService.InQueueCount > 0 || downloadService.AnyThreadDownloading())
        {
            Thread.Sleep(100);
        }
        
        downloadService.StopThreads();
    }
}