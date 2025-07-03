using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ATL;
using FuzzySharp;
using SeekDownloader.Helpers;
using Soulseek;
using Soulseek.Diagnostics;
using Directory = System.IO.Directory;

namespace SeekDownloader.Services;

public class DownloadService
{
    private const long MinAvailableDiskSpace = 20000; //MB
    
    public string? SoulSeekUsername { get; set; }
    public string? SoulSeekPassword { get; set; }
    public int NicotineListenPort { get; set; }
    public string? DownloadFolderNicotine { get; set; }
    public bool DownloadSingles { get; set; }
    
    public int SeekCount { get; set; }
    public int ThreadCount { get; set; }
    public int AlreadyDownloadedSkipCount { get; set; }
    public int SeekSuccessCount { get; set; }
    public string CurrentlySeeking { get; set; } = string.Empty;
    public int IncorrectTags { get; set; }
    
    public bool UpdateAlbumName { get; set; }
    
    private ConcurrentQueue<SearchGroup> _searchGroups = new ConcurrentQueue<SearchGroup>();
    private Dictionary<string, DownloadProgress> _lastProgressReport = new Dictionary<string, DownloadProgress>();
    private Dictionary<string, int> _errors = new Dictionary<string, int>(); 
    private Dictionary<string, int> _userErrors = new Dictionary<string, int>();
    private List<DownloadProgress> _threadDownloadProgress = new List<DownloadProgress>();
    private List<string> _toIgnoreFiles = new List<string>();
    private List<Thread> _downloadThreads = new List<Thread>();
    private Thread? _progressThread;
    private bool _stopThreads = false;
    private List<FileInfo> _cachedNicotineFiles = new List<FileInfo>();
    
    public SoulseekClient? SoulClient { get; private set; }
    public List<string> MissingNames { get; set; } = new List<string>();
    public int InQueueCount => _searchGroups.Count;
    public bool CheckTags { get; set; }
    public bool CheckTagsDelete { get; set; }
    public bool OutputStatus { get; set; }

    public async Task ConnectAsync()
    {
        var options = new SoulseekClientOptions(
            minimumDiagnosticLevel: DiagnosticLevel.None,
            peerConnectionOptions: new ConnectionOptions(connectTimeout: 15000, inactivityTimeout: 15000),
            transferConnectionOptions: new ConnectionOptions(connectTimeout: 15000, inactivityTimeout: 15000),
            distributedConnectionOptions: new ConnectionOptions(connectTimeout: 15000, inactivityTimeout: 15000),
            enableDistributedNetwork: true,
            acceptDistributedChildren: true,
            enableListener: true,
            listenPort: NicotineListenPort,
            messageTimeout: 5000
        );
        
        this.SoulClient = new SoulseekClient(options);
        this.SoulClient.Connected += (sender, e) => Debug.WriteLine("connected");
        this.SoulClient.Disconnected += (sender, e) => Debug.WriteLine("disconnected");
        this.SoulClient.BrowseProgressUpdated += (sender, e) => Debug.WriteLine($"Browse progress {e.PercentComplete}%");
        this.SoulClient.StateChanged += (sender, e) => Debug.WriteLine($"State changed: {e.State}");
        this.SoulClient.DiagnosticGenerated += (sender, e) =>
        {
            Debug.WriteLine($"[{e.Level}]: {e.Message}");
        };

        try
        {
            await this.SoulClient.ConnectAsync(SoulSeekUsername, SoulSeekPassword);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public void StartThreads()
    {
        for (int i = 0; i < ThreadCount; i++)
        {
            _threadDownloadProgress.Add(new DownloadProgress()
            {
                ThreadIndex = i
            });
            Thread thread = new Thread(new ParameterizedThreadStart(DownloadThread));
            thread.Start(i);
            _downloadThreads.Add(thread);
        }

        if (OutputStatus)
        {
            _progressThread = new Thread(new ThreadStart(ProgressThread));
            _progressThread.Start();
        }
    }

    public void StopThreads()
    {
        _stopThreads = true;
    }

    private void SetThreadStatus(int threadIndex, Action<DownloadProgress> action)
    {
        lock (_threadDownloadProgress)
        {
            var downloadProgress = _threadDownloadProgress.FirstOrDefault(progress => progress.ThreadIndex == threadIndex);
            if (downloadProgress != null)
            {
                action(downloadProgress);
            }
        }
    }

    public bool AnyThreadDownloading()
    {
        lock (_threadDownloadProgress)
        {
            return _threadDownloadProgress
                .Any(thread => thread.ThreadStatus?.ToLower().Contains("waiting") == false);
        }
    }
    
    void DownloadThread(object? threadIndexObj)
    {
        int threadIndex = (int?)threadIndexObj ?? 0;
        while (!_stopThreads)
        {
            try
            {
                SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Waiting");
                
                SearchGroup? searchGroup = null;
                if (!_searchGroups.TryDequeue(out searchGroup))
                {
                    SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Waiting");
                    Thread.Sleep(2000);
                    continue;
                }

                var possibleDownloadResults = searchGroup.SearchResults.ToList();

                int downloadIndex = 0;

                foreach (var downFile in possibleDownloadResults)
                {
                    while (!EnoughDiskSpace(DownloadFolderNicotine) && !_stopThreads)
                    {
                        if (!OutputStatus)
                        {
                            Console.WriteLine($"Waiting for diskspace");
                        }
                        
                        SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Waiting for diskspace");
                        Thread.Sleep(5000);
                    }
                    lock (_userErrors)
                    {
                        if (_userErrors.ContainsKey(downFile.Username) &&
                            _userErrors[downFile.Username] >= 10)
                        {
                            SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Ignored user, {downFile.Username}");
                            continue;
                        }
                    }

                    if (_stopThreads)
                    {
                        break;
                    }
                    
                    downloadIndex++;

                    var splitName = new string[0];
                    string folderName = string.Empty;
                    string fileName = string.Empty;

                    if (downFile.Filename.Contains("\\"))
                    {
                        splitName = downFile.Filename.Split('\\');
                    }
                    else
                    {
                        splitName = downFile.Filename.Split("//");
                    }

                    try
                    {
                        fileName = splitName.Last();
                        folderName = splitName[splitName.Length - 2];
                    }
                    catch (Exception e)
                    {
                        if (!OutputStatus)
                        {
                            Console.WriteLine($"Error, {e.Message}");
                        }
                        
                        SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Error, {e.Message}");
                        //continue;
                    }
                    
                    string targetFolder = Path.Combine(DownloadFolderNicotine, downFile.Username, folderName);
                    string tempTargetFile = Path.Combine(targetFolder, $"{fileName}.bak");
                    string realTargetFile = Path.Combine(targetFolder, fileName);

                    lock (_toIgnoreFiles)
                    {
                        if (_toIgnoreFiles.Contains(fileName.ToLower()))
                        {
                            AlreadyDownloadedSkipCount++;
                            continue;
                        }
                    }
                    
                    //already downloaded by user?
                    FileInfo targetFileInfo = new FileInfo(realTargetFile);
                    if (targetFileInfo.Exists && targetFileInfo.Length == downFile.Size)
                    {
                        AlreadyDownloadedSkipCount++;
                        continue;
                    }
                    
                    //already downloaded by nicotine in download folder?
                    bool isAlreadyDownloaded = GetCachedNicotineDownloads()
                        //.Select(musicFile => musicFile.Name.Split('-', StringSplitOptions.TrimEntries).Last())
                        .Where(musicFile => musicFile.Name.Contains('.'))
                        .Select(musicFile => musicFile.Name.Substring(0, musicFile.Name.LastIndexOf('.')))
                        .Any(musicFile => Fuzz.Ratio(fileName, musicFile) > 90);
                    
                    if (isAlreadyDownloaded)
                    {
                        AlreadyDownloadedSkipCount++;
                        continue;
                    }
            
                    CancellationTokenSource cancellationToken = new CancellationTokenSource();

                    if (!Directory.Exists(targetFolder))
                    {
                        Directory.CreateDirectory(targetFolder);
                    }
                    
                    try
                    {
                        if (!OutputStatus)
                        {
                            Console.WriteLine($"Downloading, '{downFile.Filename}'");
                        }
                        
                        SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Downloading");

                        double averageSpeed = 0;
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        
                        var downloadTask = this.SoulClient.DownloadAsync(
                            username: downFile.Username, 
                            remoteFilename: downFile.Filename, 
                            tempTargetFile, 
                            size:  downFile.Size, 
                            cancellationToken: cancellationToken.Token,
                            options: new TransferOptions(stateChanged: (e) => { },
                            progressUpdated: (e) =>
                            {
                                averageSpeed = e.Transfer.AverageSpeed;

                                if (averageSpeed > 0)
                                {
                                    stopwatch.Reset();
                                }
                                
                                SetThreadStatus(threadIndex, status => status.AverageDownloadSpeed = averageSpeed);
                                
                                lock (_lastProgressReport)
                                {
                                    int roundedProgress = (int)Math.Round(e.Transfer.PercentComplete);
                                    if (!_lastProgressReport.ContainsKey(e.Transfer.Filename))
                                    {
                                        _lastProgressReport[e.Transfer.Filename] = new DownloadProgress(e.Transfer.Filename, roundedProgress);
                                    }
                                    
                                    _lastProgressReport[e.Transfer.Filename].ThreadIndex = threadIndex;
                                    _lastProgressReport[e.Transfer.Filename].ThreadDownloads = possibleDownloadResults.Count;
                                    _lastProgressReport[e.Transfer.Filename].ThreadDownloadsIndex = downloadIndex;
                                    _lastProgressReport[e.Transfer.Filename].AverageDownloadSpeed = e.Transfer.AverageSpeed;
                                    
                                    if (_lastProgressReport[e.Transfer.Filename].Progress != roundedProgress)
                                    {
                                        _lastProgressReport[e.Transfer.Filename].Progress = roundedProgress;
                                        _lastProgressReport[e.Transfer.Filename].LastUpdatedAt = DateTime.Now;
                                    }
                                }
                            }));

                        
                        //while (!downloadTask.IsCompleted && !downloadTask.IsFaulted)
                        //{
                        //    Thread.Sleep(1000);
                        //    //Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(1)), downloadTask).GetAwaiter().GetResult();
                        //    if (stopwatch.Elapsed.TotalSeconds > 30)
                        //    {
                        //        cancellationToken.Cancel();
                        //        break;
                        //    }
                        //}
                
                        Task.WhenAny(downloadTask, Task.Delay(TimeSpan.FromSeconds(300))).GetAwaiter().GetResult();

                        if (downloadTask.IsFaulted || downloadTask.Exception != null)
                        {
                            if (!OutputStatus)
                            {
                                Console.WriteLine($"Download failed for '{downFile.Filename}'");
                            }
                            SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Download failed");
                            lock (_errors)
                            {
                                if (!_errors.ContainsKey(downloadTask.Exception.Message))
                                {
                                    _errors.Add(downloadTask.Exception.Message, 0);
                                }
                                _errors[downloadTask.Exception.Message]++;
                            }
                            
                            lock (_userErrors)
                            {
                                if (!_userErrors.ContainsKey(downFile.Username))
                                {
                                    _userErrors.Add(downFile.Username, 0);
                                }
                                _userErrors[downFile.Username]++;
                            }
                            continue;
                        }

                        if (!downloadTask.IsCompleted)
                        {
                            //Console.WriteLine($"Download canceled for {targetFile}");
                            cancellationToken.Token.ThrowIfCancellationRequested();  // Signal cancellation
                            continue;
                        }

                        FileInfo tempTargetFileInfo = new FileInfo(tempTargetFile);
                        if (downloadTask.IsCompleted &&
                            tempTargetFileInfo.Exists &&
                            tempTargetFileInfo.Length == downFile.Size)
                        {
                            tempTargetFileInfo.MoveTo(realTargetFile, true);

                            
                            Track track = new Track(realTargetFile);
                            bool artistNameMatch = Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), track.Artist.ToLower()) >= 80 ||
                                                   Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), track.AlbumArtist.ToLower()) >= 80 ||
                                                   Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), track.SortArtist.ToLower()) >= 80 ||
                                                   Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), track.SortAlbumArtist.ToLower()) >= 80;
                            
                            if (!artistNameMatch)
                            {
                                artistNameMatch = Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), track.Album) >= 80 ||
                                                    track.AdditionalFields.Any(field =>
                                                        !string.IsNullOrWhiteSpace(field.Value) &&
                                                        Fuzz.PartialTokenSetRatio(searchGroup.TargetArtistName.ToLower(), field.Value) >= 80);
                            }
                            
                            bool trackNameMatch = string.IsNullOrWhiteSpace(searchGroup.TargetSongName) || 
                                                  (Fuzz.PartialTokenSetRatio(searchGroup.TargetSongName.ToLower(), track.Title.ToLower()) >= 80 &&
                                                  FuzzyHelper.ExactNumberMatch(searchGroup.TargetSongName, track.Title));
                            
                            if (UpdateAlbumName && 
                                artistNameMatch &&
                                trackNameMatch &&
                                !string.IsNullOrWhiteSpace(searchGroup.TargetAlbumName) && 
                                !string.IsNullOrWhiteSpace(searchGroup.TargetSongName))
                            {
                                track.AdditionalFields.Add("OriginalAlbumName", track.Album);
                                track.Album = searchGroup.TargetAlbumName;
                                track.Save();
                            }

                            bool albumNameMatch = string.IsNullOrWhiteSpace(searchGroup.TargetAlbumName) || 
                                                  (Fuzz.PartialTokenSetRatio(searchGroup.TargetAlbumName.ToLower(), track.Album.ToLower()) >= 80 &&
                                                   FuzzyHelper.ExactNumberMatch(searchGroup.TargetAlbumName, track.Album));

                            if (CheckTags && (!artistNameMatch || 
                                              !trackNameMatch ||
                                              !albumNameMatch))
                            {
                                IncorrectTags++;
                                new FileInfo(realTargetFile).Delete();
                                continue;
                            }
                            
                            lock (_toIgnoreFiles)
                            {
                                _toIgnoreFiles.Add(fileName);
                            }

                            if (!OutputStatus)
                            {
                                Console.WriteLine($"Downloaded '{realTargetFile}'");
                            }

                            if (_cachedNicotineFiles != null)
                            {
                                lock (_cachedNicotineFiles)
                                {
                                    _cachedNicotineFiles.Add(new FileInfo(realTargetFile));
                                }
                            }

                            if (DownloadSingles)
                            {
                                break; //for downloading single songs
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        //Console.WriteLine($"Error trying to download {e.Message}, trying next download");
                    }
                }
            }
            catch (Exception e)
            {
                SetThreadStatus(threadIndex, status => status.ThreadStatus = $"[{DateTime.Now.ToString("HH:mm:ss")}] Error, {e.StackTrace}");
                lock (_errors)
                {
                    if (!string.IsNullOrWhiteSpace(e.Message))
                    {
                        _errors.TryAdd(e.Message, 0);
                        _errors[e.Message]++;
                    }
                }
            }
        }
    }

    private List<FileInfo> GetCachedNicotineDownloads()
    {
        lock (_cachedNicotineFiles)
        {
            if (_cachedNicotineFiles.Count > 0)
            {
                return _cachedNicotineFiles;
            }

            _cachedNicotineFiles = new DirectoryInfo(DownloadFolderNicotine)
                .GetFiles("*.*", SearchOption.AllDirectories)
                .Where(musicFile => !musicFile.Name.EndsWith(".bak"))
                .ToList();
            
            return _cachedNicotineFiles;
        }
    }

    public void EnqueueDownload(SearchGroup searchGroup)
    {
        _searchGroups.Enqueue(searchGroup);
    }
    
    void ProgressThread()
    {
        while (!_stopThreads)
        {
            Thread.Sleep(1000);
            //Console.Clear();
            //Console.SetCursorPosition(0, 0);
            
            int totalWidth = Console.BufferWidth;
            StringBuilder output = new StringBuilder();
        
            output.AppendLine($"Seeked: {SeekCount} (success: {SeekSuccessCount}) / {this.MissingNames.Count}".PadRight(totalWidth));
            output.AppendLine($"Currently seeking: {CurrentlySeeking}".PadRight(totalWidth));
            output.AppendLine($"Queue: {_searchGroups.Count}".PadRight(totalWidth));
            output.AppendLine($"Skipped already downloaded: {AlreadyDownloadedSkipCount}".PadRight(totalWidth));
            output.AppendLine($"Incorrect tagged: {IncorrectTags}".PadRight(totalWidth));

            List<DownloadProgress> downloads;
            int downloaded = 0;

            lock (_lastProgressReport)
            {
                downloads = _lastProgressReport.Values
                    .Where(progress => (DateTime.Now - progress.LastUpdatedAt).TotalSeconds <= 5)
                    .Where(progress => progress.Progress < 100)
                    .OrderBy(progress => progress.ThreadIndex)
                    .ToList();

                downloaded = _lastProgressReport.Values
                    .Count(progress => progress.Progress == 100);
            }
            
            output.AppendLine($"Active downloads: {downloads.Count}".PadRight(totalWidth));
            output.AppendLine($"Succesful downloads: {downloaded}".PadRight(totalWidth));


            lock (_threadDownloadProgress)
            {
                foreach (var progress in _threadDownloadProgress)
                {
                    var downloadProgress = downloads.FirstOrDefault(d => d.ThreadIndex == progress.ThreadIndex);
                    
                    int downloadSpeed = (int)(progress.AverageDownloadSpeed / 1000);
                    output.AppendLine($"Thread {progress.ThreadIndex}: {progress.ThreadStatus}, Download speed: {downloadSpeed}KBps{DrawProgressBar(downloadProgress)}".PadRight(totalWidth));
                }
            }
            
            lock (_errors)
            {
                foreach (var error in _errors.OrderByDescending(x => x.Value).Take(5))
                {
                    output.AppendLine($"Error {error.Value}x, {error.Key}");
                }
            }

            for (int i = 0; i < 2; i++)
            {
                output.AppendLine("".PadRight(totalWidth));
            }
            
            Console.SetCursorPosition(0, 0);
            Console.Write(output.ToString());
        }
    }
    
    private string DrawProgressBar(DownloadProgress? progress, int barSize = 50)
    {
        if (progress == null || progress.ThreadDownloads == 0 || string.IsNullOrWhiteSpace(progress.Filename))
        {
            return string.Empty;
        }
        
        // Limit the file name display to 20 characters
        string displayFileName = progress.Filename.Length > 50 ? progress.Filename.Substring(progress.Filename.Length - 50, 50) : progress.Filename;
        
        // Build the progress bar
        int filledBars = (int)((progress.Progress / 100.0) * barSize);
        string progressBar = new string('=', filledBars) + new string('-', barSize - filledBars);

        // Write the progress bar to the console
        return $", Downloading [{progress.ThreadDownloadsIndex} / {progress.ThreadDownloads}] {displayFileName} [{progressBar}] {progress.Progress}%";
    }
    
    private bool EnoughDiskSpace(string directoryPath)
    {
        DriveInfo drive = new DriveInfo(directoryPath);

        if (!drive.IsReady)
        {
            return false;
        }

        return drive.AvailableFreeSpace > MinAvailableDiskSpace * (1024 * 1024);
    }
}