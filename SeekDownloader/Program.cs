using System.Diagnostics;
using ConsoleAppFramework;
using SeekDownloader.Commands;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var AppBuilder = ConsoleApp.Create();
            AppBuilder.Add("", RootCommand.DownloadCommand);
            AppBuilder.Run(args);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}

