using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ChatLab;

var clients = new List<ClientInfo>();
var clientsLock = new object();
var attachments = new ConcurrentDictionary<string, Attachment>();
string storage = Path.Combine(AppContext.BaseDirectory, "Transfers");
Directory.CreateDirectory(storage);
var listener = new TcpListener(IPAddress.Any, 5000);
var transferListener = new TcpListener(IPAddress.Any, TransferProtocol.Port);
listener.Start();
transferListener.Start();
Console.WriteLine("Chat: 5000 | File/image: 5001 | Ctrl+C to stop");
_ = AcceptTransfersAsync();
while (true)
{
    var socket = await listener.AcceptTcpClientAsync();
    _ = HandleClientAsync(socket);
}

ClientInfo[] Snapshot()
{
    lock (clientsLock) return clients.ToArray();
}

async Task HandleClientAsync(TcpClient socket)
{
    ClientInfo? user = null;
    try
    {
        var stream = socket.GetStream();
        string name = (await ReceiveAsync(stream)).Trim();
        if (name.Length is < 1 or > 40 || name.IndexOfAny(['[', ']', '|']) >= 0 || name.Any(char.IsControl) ||
            new[] { "NAME", "TOKEN", "ONLINE", "SERVER", "ATTACHMENT" }.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Username must be 1–40 characters, without brackets or |.");
        lock (clientsLock)
        {
            string original = name;
            int suffix = 2;
            while (clients.Any(c => c.Username.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = original + suffix++;
            user = new ClientInfo(socket, name);
            clients.Add(user);
        }
        await SendAsync(user, "[NAME]" + name);
        await SendAsync(user, "[TOKEN]" + user.Token);
        await BroadcastAsync("[SERVER]" + name + " joined the chat.");
        await OnlineAsync();
        Console.WriteLine($"Connected: {name}");
        while (true)
        {
            string message = (await ReceiveAsync(stream)).Trim();
            if (message.Length > 0) await BroadcastAsync($"[{name}]{message}");
        }
    }
    catch (Exception ex) { Console.WriteLine($"Chat closed: {ex.Message}"); }
    finally
    {
        if (user != null)
        {
            lock (clientsLock) clients.Remove(user);
            user.Lifetime.Cancel();
            await BroadcastAsync($"[SERVER]{user.Username} left the chat.");
            await OnlineAsync();
        }
        socket.Dispose();
    }
}

Task OnlineAsync() => BroadcastAsync("[ONLINE]" + string.Join('|', Snapshot().Select(c => c.Username)));

Task BroadcastAsync(string message) => Task.WhenAll(Snapshot().Select(async user =>
{
    try { await SendAsync(user, message); }
    catch { user.Client.Dispose(); }
}));

async Task SendAsync(ClientInfo user, string message)
{
    // Protect the entire frame against concurrent broadcasts.
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await user.SendLock.WaitAsync(timeout.Token);
    try
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        var stream = user.Client.GetStream();
        await stream.WriteAsync(BitConverter.GetBytes(data.Length), timeout.Token);
        await stream.WriteAsync(data, timeout.Token);
    }
    finally { user.SendLock.Release(); }
}

async Task<string> ReceiveAsync(Stream stream)
{
    byte[] header = new byte[4];
    await stream.ReadExactlyAsync(header);
    int length = BitConverter.ToInt32(header);
    if (length <= 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid chat length.");
    byte[] data = new byte[length];
    await stream.ReadExactlyAsync(data);
    return Encoding.UTF8.GetString(data);
}

async Task AcceptTransfersAsync()
{
    while (true)
    {
        var socket = await transferListener.AcceptTcpClientAsync();
        _ = HandleTransferAsync(socket);
    }
}

async Task HandleTransferAsync(TcpClient socket)
{
    using (socket)
    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30)))
    {
        string? partial = null;
        try
        {
            var stream = socket.GetStream();
            using var handshake = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var request = await TransferProtocol.ReadAsync<TransferRequest>(stream, handshake.Token);
            var user = Snapshot().FirstOrDefault(c => c.Token == request.Token)
                ?? throw new InvalidDataException("Chat session expired. Reconnect.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, user.Lifetime.Token);
            var ct = linked.Token;
            if (request.Operation == "upload")
            {
                if (request.Size < 0 || request.Size > TransferProtocol.MaxFileSize ||
                    (request.IsImage && request.Size > TransferProtocol.MaxImageSize))
                    throw new InvalidDataException("File too large.");
                string name = Path.GetFileName(request.Name);
                if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidDataException("Invalid filename.");
                string id = Guid.NewGuid().ToString("N");
                partial = Path.Combine(storage, id + ".part");
                await TransferProtocol.WriteAsync(stream, "ready", ct);
                await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, TransferProtocol.BufferSize, FileOptions.Asynchronous))
                    await TransferProtocol.CopyAsync(stream, file, request.Size, ct: ct);
                string hash = await TransferProtocol.HashAsync(partial, ct);
                ct.ThrowIfCancellationRequested();
                File.Move(partial, Path.Combine(storage, id));
                partial = null;
                var item = new Attachment(id, name, request.Size, request.IsImage, user.Username, hash);
                attachments[id] = item;
                await BroadcastAsync("[ATTACHMENT]" + JsonSerializer.Serialize(item));
                await TransferProtocol.WriteAsync(stream, item, ct);
                Console.WriteLine($"Stored {name}: {item.Size:N0} bytes ({user.Username})");
            }
            else if (request.Operation == "download")
            {
                if (!attachments.TryGetValue(request.Id, out var item)) throw new FileNotFoundException("Attachment unavailable.");
                if (request.Offset < 0 || request.Count < 0 || request.Offset > item.Size || request.Count > item.Size - request.Offset)
                    throw new InvalidDataException("Invalid range.");
                await using var file = TransferProtocol.OpenRead(Path.Combine(storage, item.Id));
                file.Position = request.Offset;
                await TransferProtocol.WriteAsync(stream, "ready", ct);
                await TransferProtocol.CopyAsync(file, stream, request.Count, ct: ct);
            }
            else throw new InvalidDataException("Unknown operation.");
        }
        catch (Exception ex) { Console.WriteLine($"Transfer failed: {ex.Message}"); }
        finally
        {
            if (partial != null && File.Exists(partial)) File.Delete(partial);
        }
    }
}

sealed class ClientInfo(TcpClient client, string username)
{
    public TcpClient Client { get; } = client;
    public string Username { get; } = username;
    public string Token { get; } = Guid.NewGuid().ToString("N");
    public SemaphoreSlim SendLock { get; } = new(1, 1);
    public CancellationTokenSource Lifetime { get; } = new();
}
