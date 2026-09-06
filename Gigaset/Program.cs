using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;

namespace RfpProxy.Gigaset;

public static class Program
{
    private static readonly CancellationTokenSource CancellationTokenSource = new();
    private static GigasetClient _client;

    public static async Task Main(string[] args)
    {
        // handle command line arguments
        var socketName = "client.sock";
        var showHelp = false;
        var options = new OptionSet
        {
            { "s|socket=", "socket path", x => socketName = x },
            { "h|help", "show help", x => showHelp = x != null },
        };

        try
        {
            if (options.Parse(args).Count > 0)
            {
                showHelp = true;
            }
        }
        catch (OptionException ex)
        {
            await Console.Error.WriteAsync("Parsing arguments failed:");
            await Console.Error.WriteLineAsync(ex.Message);
            return;
        }

        if (showHelp)
        {
            options.WriteOptionDescriptions(Console.Error);
            return;
        }

        // actually start the main loop
        Console.WriteLine("Listening...");
        try
        {
            _client = new GigasetClient(socketName);

            // properly handle ctrl+c
            Console.CancelKeyPress += (_, consoleCancelEventArgs) =>
            {
                consoleCancelEventArgs.Cancel = true;
                CancellationTokenSource.Cancel();
                _client.Stop();
            };

            await _client.AddHandlerAsync(0, "000000000000", "000000000000", "0301", "ffff", CancellationTokenSource.Token)
                .ConfigureAwait(false);
            await _client.RunAsync(CancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
        {
            // ignored
        }
    }
}