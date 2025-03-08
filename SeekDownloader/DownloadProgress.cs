namespace SeekDownloader;

public class DownloadProgress
{
    public string Filename { get; set; } = string.Empty;
    public int Progress { get; set; }
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
    public int ThreadIndex { get; set; }
    public int ThreadDownloads { get; set; }
    public int ThreadDownloadsIndex { get; set; }
    public string ThreadStatus { get; set; } = string.Empty;
    public double AverageDownloadSpeed { get; set; } = 0;

    public DownloadProgress(string filename, int progress)
    {
        this.Filename = filename;
        this.Progress = progress;
    }
    public DownloadProgress()
    {
        
    }
}