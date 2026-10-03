using System.Text;
using System.Threading.Channels;

namespace Forge.Core;

/// <summary>Single writer: game callbacks enqueue detached data, never block on disk I/O.</summary>
public sealed class JsonlJournal : IAsyncDisposable
{
    private readonly Channel<object> _queue = Channel.CreateUnbounded<object>(new UnboundedChannelOptions
        { SingleReader = true, SingleWriter = false });
    private readonly Task _writer;
    public Exception? Failure { get; private set; }
    public JsonlJournal(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = Task.Run(async () =>
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                await foreach (var entry in _queue.Reader.ReadAllAsync()) await writer.WriteLineAsync(Wire.Encode(entry));
            }
            catch (Exception ex) { Failure = ex; _queue.Writer.TryComplete(ex); }
        });
    }
    public void Append(string kind, object payload) => _queue.Writer.TryWrite(new
        { schema_version = 1, timestamp_utc = DateTimeOffset.UtcNow, kind, payload });
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }
}
