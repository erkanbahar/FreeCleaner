using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace FreeCleaner.Services;

/// <summary>
/// Enforces single-instance execution per Windows user and forwards CLI args
/// from secondary launches to the running primary window over a named pipe.
/// </summary>
public sealed class SingleInstanceBridgeService : IDisposable
{
    public static string DefaultMutexName =>
        $"Local\\FreeCleanerPro_SingleInstance_{SanitizeToken(Environment.UserName)}";

    public static string DefaultPipeName =>
        $"FreeCleanerPro_IpcPipe_{SanitizeToken(Environment.UserName)}";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public bool TryAcquirePrimaryInstance(string? mutexName = null)
    {
        if (_ownsMutex && _mutex is not null)
            return true;

        string targetMutex = string.IsNullOrWhiteSpace(mutexName) ? DefaultMutexName : mutexName;
        var candidate = new Mutex(initiallyOwned: true, targetMutex, out bool createdNew);
        if (!createdNew)
        {
            candidate.Dispose();
            return false;
        }

        _mutex = candidate;
        _ownsMutex = true;
        return true;
    }

    public static async Task<bool> SendArgsToPrimaryInstanceAsync(
        IReadOnlyList<string>? args,
        string? pipeName = null,
        int timeoutMs = 2000)
    {
        string targetPipe = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                targetPipe,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(Math.Max(200, timeoutMs)).ConfigureAwait(false);

            string[] payloadArray = args?.ToArray() ?? [];
            string jsonLine = JsonSerializer.Serialize(payloadArray) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(jsonLine);

            await client.WriteAsync(bytes).ConfigureAwait(false);
            await client.FlushAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartListening(Action<string[]> onArgsReceived, string? pipeName = null)
    {
        ArgumentNullException.ThrowIfNull(onArgsReceived);

        StopListening();

        string targetPipe = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName;
        var cts = new CancellationTokenSource();
        _listenerCts = cts;
        CancellationToken ct = cts.Token;

        _listenerTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        targetPipe,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[]? receivedArgs = JsonSerializer.Deserialize<string[]>(line);
                        onArgsReceived(receivedArgs ?? []);
                    }
                    else
                    {
                        onArgsReceived([]);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    if (!ct.IsCancellationRequested)
                        await Task.Delay(120, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }, ct);
    }

    public void StopListening()
    {
        var cts = Interlocked.Exchange(ref _listenerCts, null);
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
        }
        _listenerTask = null;
    }

    private static string SanitizeToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "User";

        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
            sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.ToString();
    }

    public void Dispose()
    {
        StopListening();

        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is not null)
        {
            if (_ownsMutex)
            {
                try { mutex.ReleaseMutex(); } catch { }
                _ownsMutex = false;
            }
            mutex.Dispose();
        }
    }
}
