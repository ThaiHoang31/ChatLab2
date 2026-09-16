using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChatLab;

public sealed record Attachment(string Id, string Name, long Size, bool IsImage, string Sender, string Sha256);
public sealed record TransferRequest(string Operation, string Token, string Id = "", string Name = "",
    long Size = 0, bool IsImage = false, long Offset = 0, long Count = 0);

public static class TransferProtocol
{
    public const int Port = 5001;
    public const int BufferSize = 64 * 1024;
    public const long MaxFileSize = 10L * 1024 * 1024 * 1024;
    public const long MaxImageSize = 20L * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > 65536) throw new InvalidDataException("Metadata too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(bytes, ct);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > 65536) throw new InvalidDataException("Invalid metadata length.");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Missing metadata.");
    }

    // Fixed-size buffer: memory use does not grow with file size.
    public static async Task CopyAsync(Stream source, Stream target, long count,
        Action<int>? progress = null, CancellationToken ct = default)
    {
        byte[] buffer = new byte[BufferSize];
        while (count > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), ct);
            if (read == 0) throw new EndOfStreamException("Transfer interrupted.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            count -= read;
            progress?.Invoke(read);
        }
    }

    public static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static async Task<string> HashAsync(string path, CancellationToken ct = default)
    {
        await using var file = OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
    }

    public static async Task<Attachment> UploadAsync(string host, string token, string path, bool image,
        IProgress<long>? progress, CancellationToken ct)
    {
        await using var file = OpenRead(path);
        if (file.Length > MaxFileSize || (image && file.Length > MaxImageSize))
            throw new InvalidDataException("File tối đa 10 GB; ảnh xem trước tối đa 20 MB.");
        using var client = new TcpClient();
        await client.ConnectAsync(host, Port, ct);
        var stream = client.GetStream();
        await WriteAsync(stream, new TransferRequest("upload", token, Name: Path.GetFileName(path),
            Size: file.Length, IsImage: image), ct);
        string ready = await ReadAsync<string>(stream, ct);
        if (ready != "ready") throw new IOException(ready);
        long sent = 0;
        await CopyAsync(file, stream, file.Length, n => progress?.Report(sent += n), ct);
        return await ReadAsync<Attachment>(stream, ct);
    }

    public static async Task DownloadAsync(string host, string token, Attachment item, string destination,
        IProgress<long>? progress, CancellationToken ct)
    {
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite, BufferSize, FileOptions.Asynchronous))
            {
                output.SetLength(item.Size);
                long completed = 0;
                // Four independent connections write to disjoint byte ranges.
                await Parallel.ForEachAsync(Enumerable.Range(0, 4),
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                    async (index, cancellation) =>
                    {
                        long start = item.Size * index / 4;
                        long end = item.Size * (index + 1) / 4;
                        if (start == end) return;
                        using var client = new TcpClient();
                        await client.ConnectAsync(host, Port, cancellation);
                        var stream = client.GetStream();
                        await WriteAsync(stream, new TransferRequest("download", token, item.Id,
                            Offset: start, Count: end - start), cancellation);
                        string ready = await ReadAsync<string>(stream, cancellation);
                        if (ready != "ready") throw new IOException(ready);
                        await using var part = new FileStream(partial, FileMode.Open, FileAccess.Write,
                            FileShare.ReadWrite, BufferSize, FileOptions.Asynchronous);
                        part.Position = start;
                        await CopyAsync(stream, part, end - start,
                            n => progress?.Report(Interlocked.Add(ref completed, n)), cancellation);
                    });
            }
            if (!string.Equals(await HashAsync(partial, ct), item.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("SHA-256 không khớp. File chưa được lưu.");
            ct.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
