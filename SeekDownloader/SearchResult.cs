namespace SeekDownloader;

public class SearchResult
{
    public string Username { get; set; }
    public string Filename { get; set; }
    public long Size { get; set; }
    public bool HasFreeUploadSlot { get; set; }
    public int UploadSpeed { get; set; }
    public string TrackName { get; set; }
}