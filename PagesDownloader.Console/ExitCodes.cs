namespace PagesDownloader.Console;

/// <summary>
/// Contains application exit codes.
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int DownloadFailures = 1;
    public const int Canceled = 2;
    public const int DestinationError = 3;
    public const int NoUrlsPassed = 4;
    public const int InputFileError = 5;
}
