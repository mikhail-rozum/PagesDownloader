namespace PagesDownloader.Console;

using System;
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PagesDownloader.Library;

/// <summary>
/// Contains entry point.
/// </summary>
class Program
{
    private const int MaxTimeoutInSeconds = 300;
    private const int DefaultTimeoutInSeconds = 60;
    private const int MaxParallelismDegree = 32;
    private const int DefaultParallelismDegree = 8;

    /// <summary>
    /// Entry point.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code.</returns>
    static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

        ConfigureServices(builder.Services);

        using var host = builder.Build();
        var command = CreateCommand(host.Services);
        return await command.Parse(args).InvokeAsync();
    }

    private static RootCommand CreateCommand(IServiceProvider serviceProvider)
    {
        var timeoutOption = new Option<int>("--timeout", "-t")
        {
            Description = "Timeout in seconds",
            DefaultValueFactory = _ => DefaultTimeoutInSeconds,
            Validators =
            {
                result =>
                {
                    var value = result.GetValueOrDefault<int>();
                    if (value is < 1 or > MaxTimeoutInSeconds)
                    {
                        result.AddError($"Timeout must be between 1 and {MaxTimeoutInSeconds}");
                    }
                }
            }
        };

        var parallelismOption = new Option<int>("--parallelism", "-p")
        {
            Description = "Degree of parallelism",
            DefaultValueFactory = _ => DefaultParallelismDegree,
            Validators =
            {
                result =>
                {
                    var value = result.GetValueOrDefault<int>();
                    if (value is < 1 or > MaxParallelismDegree)
                    {
                        result.AddError($"Parallelism must be between 1 and {MaxParallelismDegree}");
                    }
                }
            }
        };

        var destinationOption = new Option<string>("--destination", "-d")
        {
            Description = "Destination path",
            Required = true,
        };

        var inputFileOption = new Option<FileInfo?>("--input", "-i")
        {
            Description = "Text file with URLs, one per line",
            Validators =
            {
                result =>
                {
                    var file = result.GetValueOrDefault<FileInfo?>();
                    if (file is { Exists: false })
                    {
                        result.AddError($"Input file '{file.FullName}' does not exist");
                    }
                }
            },
        };

        var urlsArgument = new Argument<Uri[]>("urls")
        {
            Description = "Urls list",
            Arity = ArgumentArity.ZeroOrMore,
            CustomParser = result =>
            {
                var urls = new List<Uri>();
                foreach (var token in result.Tokens)
                {
                    if (Uri.TryCreate(token.Value, UriKind.RelativeOrAbsolute, out var url))
                    {
                        urls.Add(url);
                    }
                    else
                    {
                        result.AddError($"Invalid URL: {token.Value}");
                    }
                }

                return urls.ToArray();
            },
        };

        var downloadCommand = new RootCommand()
        {
            Options =
            {
                timeoutOption,
                parallelismOption,
                destinationOption,
                inputFileOption,
            },
            Arguments =
            {
                urlsArgument,
            },
        };
        downloadCommand.SetAction((parseResult, cancellationToken) => RunAsync(serviceProvider,
            parseResult.GetValue(urlsArgument) ?? [],
            parseResult.GetValue(inputFileOption),
            parseResult.GetRequiredValue(destinationOption),
            parseResult.GetRequiredValue(parallelismOption),
            parseResult.GetRequiredValue(timeoutOption),
            cancellationToken));

        return downloadCommand;
    }

    private static async Task<int> RunAsync(IServiceProvider serviceProvider, Uri[] urls, FileInfo? inputFile, string destination, int parallelismDegree, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        if (inputFile is not null)
        {
            try
            {
                var fromFile = await ReadUrlsAsync(inputFile, logger, cancellationToken);
                urls = [.. urls, .. fromFile];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogError("Cannot read input file '{Path}': {Reason}", inputFile.FullName, ex.Message);
                return ExitCodes.InputFileError;
            }
        }

        if (urls.Length == 0)
        {
            logger.LogError("No URLs specified. Pass URLs as arguments or use --input <file>");
            return ExitCodes.NoUrlsPassed;
        }

        var pageDownloader = serviceProvider.GetRequiredService<PageDownloader>();

        try
        {
            var results = await pageDownloader.DownloadAsync(urls, destination, parallelismDegree, TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
            var succeeded = results.Count(r => r.Status == DownloadStatus.Succeeded);

            logger.LogInformation("Downloaded {Succeeded}/{Total} pages", succeeded, results.Count);
            return succeeded == results.Count ? ExitCodes.Success : ExitCodes.DownloadFailures;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Download canceled");
            return ExitCodes.Canceled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogError("Cannot use destination '{Path}': {Reason}", destination, ex.Message);
            return ExitCodes.DestinationError;
        }
    }

    private static async Task<Uri[]> ReadUrlsAsync(FileInfo file, ILogger logger, CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(file.FullName, cancellationToken);
        var urls = new List<Uri>();

        foreach (var line in lines.Select(l => l.Trim()))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (Uri.TryCreate(line, UriKind.RelativeOrAbsolute, out var url))
            {
                urls.Add(url);
            }
            else
            {
                logger.LogWarning("Skipping malformed line in {File}: {Line}", file.Name, line);
            }
        }

        return urls.ToArray();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddTransient<IFileService, FileService>();
        services.AddHttpClient<PageDownloader>(client =>
        {
            // some resources may reject requests without UserAgent header
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PagesDownloader/1.0");

            // turning it off since this timeout does NOT cover body reading
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
    }
}
