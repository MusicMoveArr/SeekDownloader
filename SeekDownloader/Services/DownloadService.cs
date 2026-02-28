using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using ATL;
using FuzzySharp;
using SeekDownloader.Helpers;
using Soulseek;
using Soulseek.Diagnostics;
using Directory = System.IO.Directory;
using File = System.IO.File;

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
    public string DownloadArchiveFilePath { get; set; }
    public ConcurrentBag<string> DownloadArchiveList = new  ConcurrentBag<string>();
    
    public bool UpdateAlbumName { get; set; }
    
    private ConcurrentQueue<SearchGroup> _searchGroups = new ConcurrentQueue<SearchGroup>();
    private ConcurrentDictionary<string, int> _errors = new ConcurrentDictionary<string, int>(); 
    private ConcurrentDictionary<string, int> _userErrors = new ConcurrentDictionary<string, int>();
    private ConcurrentDictionary<string, SemaphoreSlim> _userLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
    private ConcurrentDictionary<Guid, DownloadProgress> _threadDownloadProgress = new ConcurrentDictionary<Guid, DownloadProgress>();
    private ConcurrentBag<string> _toIgnoreFiles = new ConcurrentBag<string>();
    private ConcurrentDictionary<Guid, Thread> _downloadThreads = new ConcurrentDictionary<Guid, Thread>();
    private Thread? _progressThread;
    private bool _stopThreads = false;
    private ConcurrentBag<FileInfo> _cachedNicotineFiles = new ConcurrentBag<FileInfo>();
    
    public SoulseekClient? SoulClient { get; private set; }
    public ConcurrentBag<string> MissingNames { get; set; } = new ConcurrentBag<string>();
    public int InQueueCount => _searchGroups.Count;
    public bool CheckTags { get; set; }
    public bool CheckTagsDelete { get; set; }
    public bool OutputStatus { get; set; }
    
    public bool AllowNonTaggedFiles { get; set; }
    public bool InMemoryDownloads { get; set; }
    public int InMemoryDownloadMaxSize { get; set; }

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

    public void StartProgressThread()
    {
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

    private void SetThreadStatus(DownloadProgress downloadProgress, Action<DownloadProgress> action)
    {
        if (downloadProgress != null)
        {
            downloadProgress.LastUpdatedAt = DateTime.Now;
            action(downloadProgress);
        }
    }

    public bool AnyThreadDownloading()
    {
        return _threadDownloadProgress.AsReadOnly()
            .Any(thread => thread.Value.ThreadStatus?.ToLower().Contains("waiting") == false);
    }

    private string GetDownloadArchiveContent(Transfer transfer)
    {
        return $"{transfer.Username},{transfer.Size},{transfer.Filename}";
    }

    private void AppendDownloadArchive(Transfer transfer)
    {
        if (string.IsNullOrWhiteSpace(this.DownloadArchiveFilePath))
        {
            return;
        }
        
        try
        {
            string content = GetDownloadArchiveContent(transfer) + "\r\n";
            DownloadArchiveList.Add(content);
            File.AppendAllText(this.DownloadArchiveFilePath, content);
        }
        catch (Exception e)
        {
            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                _errors.TryAdd(e.Message, 0);
                _errors[e.Message]++;
            }
        }
    }
    
    void DownloadThread(object? downloadProgressObj)
    {
        DownloadProgress downloadProgress = (DownloadProgress)downloadProgressObj;
        int threadIndex = downloadProgress.ThreadIndex;
        
        try
        {
            SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Waiting");
            
            SearchGroup? searchGroup = null;
            if (!_searchGroups.TryDequeue(out searchGroup))
            {
                SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Waiting");
                Thread.Sleep(2000);
                return;
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
                    
                    SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Waiting for diskspace");
                    Thread.Sleep(5000);
                }
                if (_userErrors.ContainsKey(downFile.Username) &&
                    _userErrors[downFile.Username] >= 10)
                {
                    SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Ignored user, {downFile.Username}");
                    continue;
                }

                if (_stopThreads)
                {
                    break;
                }
                
                downloadIndex++;

                var splitName = downFile.Filename.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
                string fileName = splitName.Last();
                string folderName = splitName.SkipLast(1).LastOrDefault() ?? string.Empty;
                
                string targetFolder = Path.Combine(DownloadFolderNicotine, downFile.Username, folderName);
                string tempTargetFile = Path.Combine(targetFolder, $"{fileName}.bak");
                string realTargetFile = Path.Combine(targetFolder, fileName);

                if (File.Exists(tempTargetFile))
                {
                    File.Delete(tempTargetFile);
                }

                if (_toIgnoreFiles.Contains(fileName.ToLower()))
                {
                    AlreadyDownloadedSkipCount++;
                    continue;
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
        

                try
                {
                    Stream fileStream;
                    Task<Transfer>? downloadTask;
                    
                    bool isInMemoryDownload = InMemoryDownloads && downFile.Size < InMemoryDownloadMaxSize * 1024 * 1024;
                    SetThreadStatus(downloadProgress, status => status.IsInMemoryDownload = isInMemoryDownload);

                    if (isInMemoryDownload)
                    {
                        fileStream = new MemoryStream();
                    }
                    else
                    {
                        if (!Directory.Exists(targetFolder))
                        {
                            Directory.CreateDirectory(targetFolder);
                        }
                        
                        if (File.Exists(tempTargetFile))
                        {
                            File.Delete(tempTargetFile);
                        }
                        fileStream = new FileStream(tempTargetFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    }

                    SemaphoreSlim userLock;

                    if (!_userLocks.TryGetValue(downFile.Username, out userLock))
                    {
                        userLock = new SemaphoreSlim(1, 1);
                        _userLocks.TryAdd(downFile.Username, userLock);
                    }
                    
                    SetThreadStatus(downloadProgress, status => status.Username = downFile.Username);
                    while (!_stopThreads)
                    {
                        bool locked = userLock.Wait(TimeSpan.FromSeconds(1));

                        if (locked)
                        {
                            break;
                        }
                        SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Waiting, already downloading from user '{downFile.Username}'");
                    }
                    if (_stopThreads)
                    {
                        fileStream.Dispose();
                        break;
                    }
                    
                    if (!OutputStatus)
                    {
                        Console.WriteLine($"Downloading, '{downFile.Filename}'");
                    }
                    
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    CancellationTokenSource cancellationToken = new CancellationTokenSource();
                    SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Downloading");

                    downloadTask = this.SoulClient.DownloadAsync(
                        username: downFile.Username, 
                        remoteFilename: downFile.Filename,
                        async () => fileStream, 
                        size:  downFile.Size, 
                        cancellationToken: cancellationToken.Token,
                        options: new TransferOptions(stateChanged: (e) => {  }, 
                        disposeOutputStreamOnCompletion: false,
                        progressUpdated: (e) =>
                            {
                                ProgressUpdatedCallback(
                                    downloadProgress,
                                    e.Transfer,
                                    stopwatch,
                                    threadIndex,
                                    downloadIndex,
                                    possibleDownloadResults);
                            }
                            ));

                    cancellationToken.CancelAfter(TimeSpan.FromMinutes(5));
                    
                    while (!downloadTask.IsCompleted && !downloadTask.IsFaulted)
                    {
                        Thread.Sleep(1000);
                        if (stopwatch.Elapsed.TotalSeconds > 30)
                        {
                            try
                            {
                                cancellationToken.Cancel();
                            }
                            catch (Exception e) { }
                            break;
                        }
                    }

                    if (downloadTask.IsFaulted || downloadTask.Exception != null)
                    {
                        if (!OutputStatus)
                        {
                            Console.WriteLine($"Download failed for '{downFile.Filename}'");
                        }
                        SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Download failed");
                        _errors.TryAdd(downloadTask.Exception.Message, 0);
                        _errors[downloadTask.Exception.Message]++;
                        
                        _userErrors.TryAdd(downFile.Username, 0);
                        _userErrors[downFile.Username]++;
                        
                        if (File.Exists(tempTargetFile))
                        {
                            File.Delete(tempTargetFile);
                        }

                        userLock.Release();
                        fileStream.Dispose();
                        continue;
                    }

                    if (!downloadTask.IsCompleted)
                    {
                        if (File.Exists(tempTargetFile))
                        {
                            File.Delete(tempTargetFile);
                        }
                        //Console.WriteLine($"Download canceled for {targetFile}");
                        userLock.Release();
                        fileStream.Dispose();
                        continue;
                    }

                    fileStream.Position = 0;

                    if (downloadTask.IsCompleted &&
                        fileStream.Length == downFile.Size)
                    {
                        if (!Directory.Exists(targetFolder))
                        {
                            Directory.CreateDirectory(targetFolder);
                        }

                        AppendDownloadArchive(downloadTask.Result);
                        
                        if (!isInMemoryDownload)
                        {
                            File.Move(tempTargetFile, realTargetFile, true);
                        }

                        Track track = new Track(fileStream);
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

                        string? targetNameTrack = searchGroup.SongNames
                            .Where(name => FuzzyHelper.ExactNumberMatch(name, track.Title))
                            .Select(name => new
                            {
                                Name = name, 
                                MatchedFor = Fuzz.PartialTokenSetRatio(name.ToLower(), track.Title.ToLower())
                            })
                            .OrderByDescending(match => match.MatchedFor)
                            .FirstOrDefault()?.Name;
                        
                        bool trackNameMatch = !searchGroup.SongNames.Any() ||
                                              !string.IsNullOrWhiteSpace(targetNameTrack);
                        
                        if (UpdateAlbumName && 
                            artistNameMatch &&
                            trackNameMatch &&
                            !string.IsNullOrWhiteSpace(searchGroup.TargetAlbumName) && 
                            !string.IsNullOrWhiteSpace(targetNameTrack))
                        {
                            track.AdditionalFields.Add("OriginalAlbumName", track.Album);
                            track.Album = searchGroup.TargetAlbumName;
                            track.Save();
                        }

                        bool albumNameMatch = string.IsNullOrWhiteSpace(searchGroup.TargetAlbumName) || 
                                              (Fuzz.PartialTokenSetRatio(searchGroup.TargetAlbumName.ToLower(), track.Album.ToLower()) >= 80 &&
                                               FuzzyHelper.ExactNumberMatch(searchGroup.TargetAlbumName, track.Album));

                        if (AllowNonTaggedFiles &&
                            string.IsNullOrWhiteSpace(track.Artist) &&
                            string.IsNullOrWhiteSpace(track.AlbumArtist) &&
                            string.IsNullOrWhiteSpace(track.SortArtist) &&
                            string.IsNullOrWhiteSpace(track.SortAlbumArtist) &&
                            string.IsNullOrWhiteSpace(track.Album) &&
                            string.IsNullOrWhiteSpace(track.Title))
                        {
                            artistNameMatch = true;
                            trackNameMatch = true;
                            albumNameMatch = true;
                        }
                        
                        if (CheckTags && (!artistNameMatch || 
                                          !trackNameMatch ||
                                          !albumNameMatch))
                        {
                            IncorrectTags++;
                            if (CheckTagsDelete && !isInMemoryDownload)
                            {
                                new FileInfo(realTargetFile).Delete();
                            }
                            userLock.Release();
                            fileStream.Dispose();
                            continue;
                        }

                        if (isInMemoryDownload)
                        {
                            using (FileStream targetStream = new FileStream(realTargetFile, FileMode.OpenOrCreate,
                                       FileAccess.ReadWrite, FileShare.ReadWrite))
                            {
                                targetStream.Position = 0;
                                fileStream.Position = 0;
                                
                                fileStream.CopyTo(targetStream);
                            }
                        }
                        else
                        {
                            fileStream.Flush();
                        }
                        fileStream.Dispose();

                        _toIgnoreFiles.Add(fileName);

                        if (!OutputStatus)
                        {
                            Console.WriteLine($"Downloaded '{realTargetFile}'");
                        }

                        if (_cachedNicotineFiles != null)
                        {
                            _cachedNicotineFiles.Add(new FileInfo(realTargetFile));
                        }

                        if (DownloadSingles)
                        {
                            userLock.Release();
                            break; //for downloading single songs
                        }
                    }
                    fileStream.Dispose();
                }
                catch (Exception e)
                {
                    //Console.WriteLine($"Error trying to download {e.Message}, trying next download");
                    SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Error, {e.StackTrace}");

                    if (!string.IsNullOrWhiteSpace(e.Message))
                    {
                        _errors.TryAdd(e.Message, 0);
                        _errors[e.Message]++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            SetThreadStatus(downloadProgress, status => status.ThreadStatus = $"Error, {e.StackTrace}");
            if (!string.IsNullOrWhiteSpace(e.Message))
            {
                _errors.TryAdd(e.Message, 0);
                _errors[e.Message]++;
            }
        }

        _threadDownloadProgress.TryRemove(downloadProgress.DownloadId, out DownloadProgress p);
        _downloadThreads.TryRemove(downloadProgress.DownloadId, out Thread t);
    }

    private void ProgressUpdatedCallback(
        DownloadProgress downloadProgress, 
        Transfer transfer, 
        Stopwatch stopwatch, 
        int threadIndex, 
        int downloadIndex,
        List<SearchResult> possibleDownloadResults)
    {
        SetThreadStatus(downloadProgress, status => status.AverageDownloadSpeed = transfer.AverageSpeed);
        SetThreadStatus(downloadProgress, status => status.Username = transfer.Username);
        
        int roundedProgress = (int)Math.Round(transfer.PercentComplete);
        
        downloadProgress.ThreadIndex = threadIndex;
        downloadProgress.ThreadDownloads = possibleDownloadResults.Count;
        downloadProgress.ThreadDownloadsIndex = downloadIndex;
        downloadProgress.AverageDownloadSpeed = transfer.AverageSpeed;
                                
        if (downloadProgress.Progress != roundedProgress)
        {
            stopwatch.Reset();
            downloadProgress.Progress = roundedProgress;
            downloadProgress.LastUpdatedAt = DateTime.Now;
        }
    }

    private List<FileInfo> GetCachedNicotineDownloads()
    {
        var files = _cachedNicotineFiles.ToList();
        if (files.Count > 0)
        {
            return files;
        }

        files = new DirectoryInfo(DownloadFolderNicotine)
            .GetFiles("*.*", SearchOption.AllDirectories)
            .Where(musicFile => !musicFile.Name.EndsWith(".bak"))
            .ToList();
        
        return files;
    }

    public void EnqueueDownload(SearchGroup searchGroup)
    {
        _searchGroups.Enqueue(searchGroup);

        while (_downloadThreads.Count >= ThreadCount)
        {
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }
        
        var downloadProgress = new DownloadProgress()
                               {
                                   ThreadIndex = _downloadThreads.Count
                               };
        
        _threadDownloadProgress.TryAdd(downloadProgress.DownloadId, downloadProgress);
        Thread thread = new Thread(new ParameterizedThreadStart(DownloadThread));
        thread.Start(downloadProgress);
        _downloadThreads.TryAdd(downloadProgress.DownloadId, thread);
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

            downloads = _threadDownloadProgress.Values
                .Where(progress => (DateTime.Now - progress.LastUpdatedAt).TotalSeconds <= 5)
                .Where(progress => progress.Progress < 100)
                .OrderBy(progress => progress.ThreadIndex)
                .ToList();

            downloaded = _threadDownloadProgress.Values
                .Count(progress => progress.Progress == 100);
            
            output.AppendLine($"Active downloads: {downloads.Count}".PadRight(totalWidth));
            output.AppendLine($"Succesful downloads: {downloaded}".PadRight(totalWidth));


            foreach (var progress in _threadDownloadProgress)
            {
                var downloadProgress = downloads.FirstOrDefault(d => d.DownloadId == progress.Key);
                if (downloadProgress != null)
                {
                    int downloadSpeed = (int)(progress.Value.AverageDownloadSpeed / 1000);
                    output.AppendLine(($"Thread {progress.Value.ThreadIndex}: " +
                                      $"[{progress.Value.LastUpdatedAt.ToString("HH:mm:ss")}] " +
                                      $"{progress.Value.ThreadStatus}, " +
                                      $"{(progress.Value.IsInMemoryDownload ? "InMemory" : "Disk")}, " +
                                      $"Speed: {downloadSpeed}KBps{DrawProgressBar(downloadProgress)}")
                        .PadRight(totalWidth));
                }
            }
            
            foreach (var error in _errors.OrderByDescending(x => x.Value).Take(5))
            {
                string errorMessage = $"Error {error.Value}, {error.Key}";
                if (errorMessage.Length > totalWidth)
                {
                    errorMessage = errorMessage.Substring(0, totalWidth);
                }
                output.AppendLine(errorMessage);
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