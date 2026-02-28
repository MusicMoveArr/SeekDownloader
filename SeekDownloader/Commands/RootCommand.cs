using CliFx;
using CliFx.Attributes;
using CliFx.Infrastructure;
using FuzzySharp;
using ListRandomizer;
using SeekDownloader.Enums;
using SeekDownloader.Helpers;
using SeekDownloader.Models;
using SeekDownloader.Services;
using Soulseek;
using File = System.IO.File;

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
    
    [CommandOption("allow-non-tagged-files", 
        Description = "Allow non-tagged files, original music-files do not contain tags either.", 
        EnvironmentVariable = "SEEK_ALLOWNONTAGGEDFILES")]
    public bool AllowNonTaggedFiles { get; set; } = false;
    
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
    
    [CommandOption("music-library-match",
        Description = "Set the hitrate percentage against your own music library, if it hits it will skip the download.",
        EnvironmentVariable = "SEEK_MUSICLIBRARY_MATCH")]
    public int MusicLibraryMatch { get; set; } = 50;
    
    [CommandOption("music-library-quick-match",
        Description = "Quickly try to find only the missing tracks from the search.",
        EnvironmentVariable = "SEEK_MUSICLIBRARY_QUICK_MATCH")]
    public bool MusicLibraryQuickMatch { get; set; } = false;
    
    [CommandOption("max-file-size",
        Description = "Set the max file size to download in Megabytes (MB).",
        EnvironmentVariable = "SEEK_MAX_FILE_SIZE")]
    public int MaxFileSize { get; set; } = 50;
    
    [CommandOption("in-memory-downloads",
        Description = "Store the downloads temporarily in memory, only successful downloads are written to disk.",
        EnvironmentVariable = "SEEK_IN_MEMORY_DOWNLOADS")]
    public bool InMemoryDownloads { get; set; } = false;
    
    [CommandOption("in-memory-downloads-max-size",
        Description = "Store the downloads temporarily in memory only if smaller then X MB else the disk is used as normal.",
        EnvironmentVariable = "SEEK_IN_MEMORY_DOWNLOADS_MAX_SIZE")]
    public int InMemoryDownloadMaxSize { get; set; } = 50;
    
    [CommandOption("download-archive",
        Description = "Download only music not listed in the archive file.",
        EnvironmentVariable = "SEEK_DOWNLOAD_ARCHIVE")]
    public string DownloadArchiveFilePath { get; set; }
    
    [CommandOption("search-match-artist",
        Description = "Set the hitrate percentage for searching the Artist.",
        EnvironmentVariable = "SEEK_SEARCH_MATCH_ARTIST")]
    public int SearchMatchArtistPercentage { get; set; } = 50;
    
    [CommandOption("search-match-album",
        Description = "Set the hitrate percentage for searching the Album.",
        EnvironmentVariable = "SEEK_SEARCH_MATCH_ALBUM")]
    public int SearchMatchAlbumPercentage { get; set; } = 50;
    
    [CommandOption("search-match-track",
        Description = "Set the hitrate percentage for searching the Track.",
        EnvironmentVariable = "SEEK_SEARCH_MATCH_TRACK")]
    public int SearchMatchTrackPercentage { get; set; } = 50;
    
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
        downloadService.AllowNonTaggedFiles = AllowNonTaggedFiles;
        downloadService.InMemoryDownloads = InMemoryDownloads;
        downloadService.InMemoryDownloadMaxSize = InMemoryDownloadMaxSize;
        downloadService.DownloadArchiveFilePath = DownloadArchiveFilePath;

        if (!string.IsNullOrWhiteSpace(downloadService.DownloadArchiveFilePath) && 
            File.Exists(downloadService.DownloadArchiveFilePath))
        {
            foreach (var path in await File.ReadAllLinesAsync(downloadService.DownloadArchiveFilePath))
            {
                downloadService.DownloadArchiveList.Add(path);
            }
            
        }
        
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
            foreach (var path in (await File.ReadAllLinesAsync(SearchFilePath))
                     .Where(line => !string.IsNullOrWhiteSpace(line))
                     .Distinct())
            {
                downloadService.MissingNames.Add(path);
            }
        }
        
        await downloadService.ConnectAsync();
        downloadService.StartThreads();

        var searchTermGroups = downloadService.MissingNames
            .Select(term => new SearchTermModel(term, SearchDelimeter))
            .GroupBy(terms => new
            {
                terms.ArtistName,
                terms.AlbumName,
                terms.SearchTermType
            })
            .ToList();
        
        foreach (var terms in searchTermGroups)
        {
            while (downloadService.InQueueCount > 100)
            {
                Thread.Sleep(1000);
            }

            List<SearchTermModel> searchTerms = new List<SearchTermModel>();
            SearchTermModel firstSearchTerm = terms.First();
            downloadService.CurrentlySeeking = firstSearchTerm.SearchTerm;

            if (MusicLibraryQuickMatch && 
                firstSearchTerm.SearchTermType is SearchTermType.ArtistTrack or SearchTermType.ArtistAlbumTrack)
            {
                fileSeeker.AddToCache(firstSearchTerm.ArtistName);

                foreach (var term in terms)
                {
                    if (fileSeeker.AlreadyInLibraryByTrack(firstSearchTerm.ArtistName, term.SongName, MusicLibraryMatch, SearchFileExtensions))
                    {
                        downloadService.AlreadyDownloadedSkipCount++;
                    }
                    else
                    {
                        searchTerms.Add(term);
                    }
                }
            }
            else
            {
                searchTerms = terms.ToList();
            }

            var results = await fileSeeker.SearchAsync(
                searchTerms,
                downloadService.SoulClient, 
                FilterOutFileNames, 
                SearchFileExtensions, 
                MusicLibraryMatch, 
                MaxFileSize,
                downloadService.DownloadArchiveList,
                SearchMatchArtistPercentage,
                SearchMatchAlbumPercentage,
                SearchMatchTrackPercentage);

            if (!string.IsNullOrWhiteSpace(fileSeeker.LastErrorMessage)
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.Connected.ToString())
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.LoggedIn.ToString()))
            {
                await downloadService.ConnectAsync();
            }

            if (!OutputStatus)
            {
                Console.WriteLine($"Seeked: '{firstSearchTerm.SearchTerm}, Found {results.Count} files");
            }
            
            downloadService.SeekCount += terms.Count();

            if (results.Count > 0)
            {
                List<string> songNames = searchTerms
                    .Select(term => term.SongName)
                    .Where(term => !string.IsNullOrEmpty(term))
                    .ToList()!;

                if (GroupedDownloads && DownloadSingles && songNames.Count > 0)
                {
                    //"de-group" so to say...
                    foreach (var songName in songNames)
                    {
                        var degroupedResults = results
                            .Select(result => new
                            {
                                Result = result,
                                MatchedFor = Fuzz.PartialRatio(result.FileNameWithExt.ToLower(), songName.ToLower())
                            })
                            .Where(result => result.MatchedFor >= SearchMatchTrackPercentage)
                            .OrderByDescending(result => result.MatchedFor)
                            .Select(result => result.Result)
                            .ToList();
                        
                        if (degroupedResults.Count > 0)
                        {
                            downloadService.SeekSuccessCount++;
                            downloadService.EnqueueDownload(new SearchGroup()
                            {
                                SearchResults = degroupedResults,
                                TargetAlbumName = firstSearchTerm.AlbumName,
                                TargetArtistName = firstSearchTerm.ArtistName,
                                SongNames = [songName]
                            });
                        }
                    }
                }
                else if (GroupedDownloads)
                {
                    downloadService.SeekSuccessCount++;
                    downloadService.EnqueueDownload(new SearchGroup()
                    {
                        SearchResults = results,
                        TargetAlbumName = firstSearchTerm.AlbumName,
                        TargetArtistName = firstSearchTerm.ArtistName,
                        SongNames = songNames
                    });
                }
                else
                {
                    downloadService.SeekSuccessCount++;
                    foreach (var result in results)
                    {
                        downloadService.EnqueueDownload(new SearchGroup()
                        {
                            SearchResults = new List<SearchResult>([result]),
                            TargetAlbumName = firstSearchTerm.AlbumName,
                            TargetArtistName = firstSearchTerm.ArtistName,
                            SongNames = songNames
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