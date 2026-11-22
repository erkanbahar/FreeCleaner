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
    public const int MaxIpcPayloadBytes = 16 * 1024;
    public const int MaxIpcArgumentCount = 32;
    public const int MaxIpcArgumentLength = 1024;

    public static string DefaultMutexName =>
        $"Local\\FreeCleanerPro_SingleInstance_{SanitizeToken(Environment.UserName)}";

    public static string DefaultPipeName =>
        $"FreeCleanerPro_IpcPipe_{SanitizeToken(Environment.UserName)}";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;

    public static string[]? TryParseBoundedIpcPayload(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.IsEmpty || utf8Bytes.Length > MaxIpcPayloadBytes)
            return null;

        try
        {
            string[]? parsed = JsonSerializer.Deserialize<string[]>(utf8Bytes);
            if (parsed is null || parsed.Length > MaxIpcArgumentCount)
                return null;

            foreach (string arg in parsed)
            {
                if (arg is null || arg.Length > MaxIpcArgumentLength)
                    return null;
            }

            return parsed;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<(byte[] LineBytes, bool ExceededLimit)> ReadBoundedUtf8LineAsync(
        Stream stream,
        int maxBytes,
        CancellationToken ct)
    {
        using var ms = new MemoryStream(Math.Min(1024, maxBytes));
        byte[] singleByte = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(singleByte.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
                break;

            byte b = singleByte[0];
            if (b == (byte)'\n')
                break;

            if (b == (byte)'\r')
                continue;

            if (ms.Length >= maxBytes)
                return ([], true);

            ms.WriteByte(b);
        }

        return (ms.ToArray(), false);
    }

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
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await client.ConnectAsync(Math.Max(200, timeoutMs)).ConfigureAwait(false);

            string[] payloadArray = (args ?? [])
                .Where(a => a is not null)
                .Take(MaxIpcArgumentCount)
                .Select(a => a.Length > MaxIpcArgumentLength ? a[..MaxIpcArgumentLength] : a)
                .ToArray();

            string jsonLine = JsonSerializer.Serialize(payloadArray) + "\n";
            byte[] bytes = Encoding.UTF8.GetBytes(jsonLine);
            if (bytes.Length > MaxIpcPayloadBytes)
            {
                int count = payloadArray.Length;
                while (count > 0)
                {
                    count--;
                    jsonLine = JsonSerializer.Serialize(payloadArray.AsSpan(0, count).ToArray()) + "\n";
                    bytes = Encoding.UTF8.GetBytes(jsonLine);
                    if (bytes.Length <= MaxIpcPayloadBytes)
                        break;
                }

                if (bytes.Length > MaxIpcPayloadBytes)
                    return false;
            }

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
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                    (byte[] lineBytes, bool exceededLimit) = await ReadBoundedUtf8LineAsync(
                        server,
                        MaxIpcPayloadBytes,
                        ct).ConfigureAwait(false);

                    if (exceededLimit)
                    {
                        ErrorLogService.Log("SingleInstanceIPC", $"Dropped oversized IPC payload exceeding {MaxIpcPayloadBytes} bytes.");
                        continue;
                    }

                    if (lineBytes.Length == 0)
                    {
                        onArgsReceived([]);
                        continue;
                    }

                    string[]? receivedArgs = TryParseBoundedIpcPayload(lineBytes);
                    if (receivedArgs is not null)
                    {
                        onArgsReceived(receivedArgs);
                    }
                    else
                    {
                        ErrorLogService.Log("SingleInstanceIPC", "Dropped malformed or out-of-bounds IPC payload.");
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
