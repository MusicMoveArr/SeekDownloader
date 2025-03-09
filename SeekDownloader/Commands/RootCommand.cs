using ConsoleAppFramework;
using SeekDownloader.Services;
using Soulseek;

namespace SeekDownloader.Commands;

public class RootCommand
{
    /// <summary>
    /// Split the target media tag by the seperator
    /// </summary>
    /// <param name="searchTerm">-s, Search term used to search for music use the order, Artist - Album - Track.</param>
    /// <param name="searchFilePath">-S, Search term(s) used to search for music use from a file.</param>
    /// <param name="downloadFilePath">-D, Download path to store the downloads.</param>
    /// <param name="soulseekListenPort">-p, Soulseek listen port (used for portforwarding).</param>
    /// <param name="soulseekUsername">-U, Soulseek username for login.</param>
    /// <param name="soulseekPassword">-P, Soulseek password for login.</param>
    /// <param name="musicLibrary">-m, Music Library path to use to check for existing local songs.</param>
    /// <param name="threadCount">-t, Download threads to use.</param>
    /// <param name="musicLibraries">-M, Multiple Music Library path(s) to use to check for existing local songs.</param>
    /// <param name="filterOutFileNames">-F, Filter out names to ignore for downloads.</param>
    [Command("")]
    public static void DownloadCommand(
            string downloadFilePath,
            int soulseekListenPort,
            string soulseekUsername,
            string soulseekPassword,
            string musicLibrary,
            string searchTerm = "", 
            string searchFilePath = "", 
            int threadCount = 10,
            List<string> musicLibraries = null,
            List<string> filterOutFileNames = null)
    {
        FileSeekService fileSeeker = new FileSeekService();
        DownloadService downloadService = new DownloadService();
        downloadService.SoulSeekUsername = soulseekUsername;
        downloadService.SoulSeekPassword = soulseekPassword;
        downloadService.ThreadCount = threadCount;
        downloadService.NicotineListenPort = soulseekListenPort;
        downloadService.DownloadFolderNicotine = downloadFilePath;

        if (!string.IsNullOrWhiteSpace(musicLibrary))
        {
            fileSeeker.MusicLibraries.Add(musicLibrary);
        }
        if (musicLibraries?.Count > 0)
        {
            fileSeeker.MusicLibraries.AddRange(musicLibraries);
        }
        
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            downloadService.MissingNames.Add(searchTerm);
        }
        if (!string.IsNullOrWhiteSpace(searchFilePath))
        {
            downloadService.MissingNames.AddRange(System.IO.File
                .ReadAllLines(searchFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct());
        }
        
        downloadService.ConnectAsync().GetAwaiter().GetResult();
        downloadService.StartThreads();
        
        foreach (string name in downloadService.MissingNames)
        {
            downloadService.SeekCount++;
            downloadService.CurrentlySeeking = name;
            
            var split = name.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            string songNameTarget = string.Empty;
            string songAlbumTarget = string.Empty;
            string songArtistTarget = string.Empty;
            
            if (split.Length > 2)
            {
                songNameTarget = split.Last();
                songAlbumTarget = split.Skip(1).First();
                songArtistTarget = split.First();
            }
            else if (split.Length > 1)
            {
                songNameTarget = split.Last();
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
            
            var results = fileSeeker.
                SearchAsync(name, songNameTarget, songArtistTarget,  downloadService.SoulClient, filterOutFileNames)
                .GetAwaiter()
                .GetResult();
            
            if (!string.IsNullOrWhiteSpace(fileSeeker.LastErrorMessage) 
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.Connected.ToString())
                && !downloadService.SoulClient.State.ToString().Contains(SoulseekClientStates.LoggedIn.ToString()))
            {
                downloadService.ConnectAsync().GetAwaiter().GetResult();
            }
            
            if (results.Count() > 1)
            {
                downloadService.SeekSuccessCount++;
            }
            
            if (results.Count > 0)
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
        }
        
        while (downloadService.InQueueCount > 0 || downloadService.AnyThreadDownloading())
        {
            Thread.Sleep(100);
        }
        
        downloadService.StopThreads();
    }
}