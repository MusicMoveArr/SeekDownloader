namespace SeekDownloader.Models;

public class DownloadFileFormatModel
{
    public required string Username { get; init; }
    public required string Filename { get; init; }
    public required string SubDirectory { get; init; }
    public required string FullPath { get; init; }
    public required long Size { get; init; }
}