using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using ChatLab;

// Run against a fresh server on localhost. Uses the same transfer implementation as the WPF client.
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var ct = timeout.Token;
string work = Path.Combine(AppContext.BaseDirectory, "test-files", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    using var alice = new TcpClient();
    using var bob = new TcpClient();
    await alice.ConnectAsync("127.0.0.1", 5000, ct);
    await bob.ConnectAsync("127.0.0.1", 5000, ct);
    await Send(alice, "IntegrationAlice");
    string aliceToken = (await Until(alice, "[TOKEN]"))[7..];
    await Send(bob, "IntegrationBob");
    string bobToken = (await Until(bob, "[TOKEN]"))[7..];
    Console.WriteLine("PASS: two chat sessions authenticated");

    string bigPath = Path.Combine(work, "512MB.bin");
    byte[] block = RandomNumberGenerator.GetBytes(64 * 1024);
    await using (var file = File.Create(bigPath))
        for (int i = 0; i < 8192; i++) await file.WriteAsync(block, ct);
    string originalHash = await TransferProtocol.HashAsync(bigPath, ct);
    string imagePath = Path.Combine(work, "preview.png");
    await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a5VQAAAAASUVORK5CYII="), ct);
    var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var upload = TransferProtocol.UploadAsync("127.0.0.1", aliceToken, bigPath, false,
        new InlineProgress(n => began.TrySetResult()), ct);
    await began.Task.WaitAsync(ct);
    Check(!upload.IsCompleted, "large upload in progress");
    var watch = Stopwatch.StartNew();
    await Send(alice, "chat-during-upload 😀 ❤️");
    await Until(bob, "[IntegrationAlice]chat-during-upload 😀 ❤️");
    var image = await TransferProtocol.UploadAsync("127.0.0.1", aliceToken, imagePath, true, null, ct);
    string receivedImage = Path.Combine(work, "received.png");
    await TransferProtocol.DownloadAsync("127.0.0.1", bobToken, image, receivedImage, null, ct);
    Check(!upload.IsCompleted, "image arrived before large upload completed");
    Check(await TransferProtocol.HashAsync(receivedImage, ct) == await TransferProtocol.HashAsync(imagePath, ct), "image hash");
    Console.WriteLine($"PASS: chat + image received during 512 MB upload ({watch.ElapsedMilliseconds} ms)");

    var big = await upload;
    Check(big.Size == 512L * 1024 * 1024 && big.Sha256 == originalHash, "uploaded size/hash");
    var downloadBegan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    string received = Path.Combine(work, "received.bin");
    var download = TransferProtocol.DownloadAsync("127.0.0.1", bobToken, big, received,
        new InlineProgress(_ => downloadBegan.TrySetResult()), ct);
    await downloadBegan.Task.WaitAsync(ct);
    Check(!download.IsCompleted, "download running");
    await Send(alice, "chat-during-download");
    await Until(bob, "[IntegrationAlice]chat-during-download");
    var image2 = await TransferProtocol.UploadAsync("127.0.0.1", aliceToken, imagePath, true, null, ct);
    await TransferProtocol.DownloadAsync("127.0.0.1", bobToken, image2, receivedImage, null, ct);
    Check(!download.IsCompleted, "image received during large download");
    await download;
    Check(new FileInfo(received).Length == big.Size && await TransferProtocol.HashAsync(received, ct) == originalHash,
        "four-range download preserves all bytes");
    Console.WriteLine("PASS: 512 MB download / 4 parallel ranges / SHA-256; chat + image during download");

    using var cancel = new CancellationTokenSource();
    string preserved = Path.Combine(work, "existing.txt");
    await File.WriteAllTextAsync(preserved, "preserve me", ct);
    try
    {
        await TransferProtocol.DownloadAsync("127.0.0.1", bobToken, big, preserved,
            new InlineProgress(_ => cancel.Cancel()), cancel.Token);
        throw new Exception("Expected cancellation");
    }
    catch (OperationCanceledException) { }
    Check(await File.ReadAllTextAsync(preserved, ct) == "preserve me", "cancel preserves existing destination");
    Check(!Directory.EnumerateFiles(work, "*.part").Any(), "partial download removed");
    Console.WriteLine("PASS: cancellation cleans partial file and preserves existing destination");

    try
    {
        await TransferProtocol.DownloadAsync("127.0.0.1", bobToken, image with { Sha256 = "bad" }, preserved, null, ct);
        throw new Exception("Expected checksum rejection");
    }
    catch (InvalidDataException) { }
    Check(await File.ReadAllTextAsync(preserved, ct) == "preserve me", "hash failure preserves destination");
    Console.WriteLine("PASS: corrupt checksum rejected");

    string empty = Path.Combine(work, "empty.bin");
    await File.WriteAllBytesAsync(empty, [], ct);
    var emptyItem = await TransferProtocol.UploadAsync("127.0.0.1", aliceToken, empty, false, null, ct);
    await TransferProtocol.DownloadAsync("127.0.0.1", bobToken, emptyItem, received, null, ct);
    Check(new FileInfo(received).Length == 0, "empty file");
    using var invalid = new TcpClient();
    await invalid.ConnectAsync("127.0.0.1", 5001, ct);
    await TransferProtocol.WriteAsync(invalid.GetStream(), new TransferRequest("download", bobToken, big.Id,
        Offset: big.Size, Count: 1), ct);
    Check(await invalid.GetStream().ReadAsync(new byte[1], ct) == 0, "invalid range rejected");
    using var malformed = new TcpClient();
    await malformed.ConnectAsync("127.0.0.1", 5001, ct);
    await malformed.GetStream().WriteAsync(BitConverter.GetBytes(int.MaxValue), ct);
    Check(await malformed.GetStream().ReadAsync(new byte[1], ct) == 0, "oversized metadata rejected");
    Console.WriteLine("PASS: empty file, invalid range, oversized metadata");
    Console.WriteLine("ALL INTEGRATION CHECKS PASSED");
}
finally
{
    // Only exact files created under this test's fixed output directory are removed.
    foreach (string file in Directory.EnumerateFiles(work)) File.Delete(file);
}

async Task Send(TcpClient client, string value)
{
    byte[] bytes = Encoding.UTF8.GetBytes(value);
    await client.GetStream().WriteAsync(BitConverter.GetBytes(bytes.Length), ct);
    await client.GetStream().WriteAsync(bytes, ct);
}
async Task<string> Until(TcpClient client, string prefix)
{
    while (true)
    {
        byte[] header = new byte[4];
        await client.GetStream().ReadExactlyAsync(header, ct);
        int count = BitConverter.ToInt32(header);
        Check(count is > 0 and <= 1024 * 1024, "valid chat frame");
        byte[] data = new byte[count];
        await client.GetStream().ReadExactlyAsync(data, ct);
        string value = Encoding.UTF8.GetString(data);
        if (value.StartsWith(prefix, StringComparison.Ordinal)) return value;
    }
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
}
sealed class InlineProgress(Action<long> action) : IProgress<long>
{
    public void Report(long value) => action(value);
}
