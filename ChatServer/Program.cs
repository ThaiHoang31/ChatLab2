using System.Net;
using System.Net.Sockets;
using System.Text;

const int PORT = 5000;

List<ClientInfo> clients = new();

var listener = new TcpListener(
    IPAddress.Any,
    PORT);

listener.Start();

Console.WriteLine("=================================");
Console.WriteLine("           CHAT SERVER");
Console.WriteLine("=================================");
Console.WriteLine($"Server started on port {PORT}");
Console.WriteLine("Waiting for clients...");
Console.WriteLine();

while (true)
{
    TcpClient client =
        await listener.AcceptTcpClientAsync();

    Console.WriteLine(
        $"Client connected: {client.Client.RemoteEndPoint}");

    _ = HandleClientAsync(client);
}


// =====================================================
// HANDLE CLIENT
// =====================================================

async Task HandleClientAsync(TcpClient client)
{
    ClientInfo? clientInfo = null;

    try
    {
        NetworkStream stream =
            client.GetStream();

        // =============================================
        // RECEIVE USERNAME
        // =============================================

        string? username =
            await ReceiveMessageAsync(stream);

        if (string.IsNullOrWhiteSpace(username))
        {
            client.Close();
            return;
        }

        username = username.Trim();

        // =============================================
        // DEFAULT USERNAME
        // =============================================

        if (string.IsNullOrWhiteSpace(username))
        {
            username = "User";
        }

        // =============================================
        // UNIQUE USERNAME
        // =============================================

        username =
            GetUniqueUsername(username);

        // =============================================
        // CREATE CLIENT
        // =============================================

        clientInfo = new ClientInfo
        {
            Client = client,
            Username = username
        };

        clients.Add(clientInfo);

        Console.WriteLine(
            $"{username} joined the chat.");

        Console.WriteLine(
            $"Online clients: {clients.Count}");

        // =============================================
        // SEND FINAL USERNAME
        // =============================================

        await SendMessageAsync(
            clientInfo,
            $"[NAME]{username}");

        // =============================================
        // NOTIFY EVERYONE
        // =============================================

        await BroadcastAsync(
            $"[SERVER]{username} joined the chat.");

        // =============================================
        // UPDATE ONLINE LIST
        // =============================================

        await BroadcastOnlineUsersAsync();

        // =============================================
        // RECEIVE CHAT MESSAGES
        // =============================================

        while (true)
        {
            string? message =
                await ReceiveMessageAsync(stream);

            if (message == null)
            {
                break;
            }

            message = message.Trim();

            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            string formattedMessage =
                $"[{username}]{message}";

            Console.WriteLine(
                formattedMessage);

            await BroadcastAsync(
                formattedMessage);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine(
            $"[ERROR] {ex.Message}");
    }
    finally
    {
        // =============================================
        // REMOVE CLIENT
        // =============================================

        if (clientInfo != null)
        {
            clients.Remove(clientInfo);

            Console.WriteLine(
                $"{clientInfo.Username} left the chat.");

            Console.WriteLine(
                $"Online clients: {clients.Count}");

            try
            {
                await BroadcastAsync(
                    $"[SERVER]{clientInfo.Username} left the chat.");

                await BroadcastOnlineUsersAsync();
            }
            catch
            {
                // Ignore broadcast errors.
            }
        }

        client.Close();
    }
}


// =====================================================
// GET UNIQUE USERNAME
// =====================================================

string GetUniqueUsername(string username)
{
    string originalUsername =
        username;

    int counter = 2;

    while (clients.Any(x =>
        string.Equals(
            x.Username,
            username,
            StringComparison.OrdinalIgnoreCase)))
    {
        username =
            $"{originalUsername}{counter}";

        counter++;
    }

    return username;
}


// =====================================================
// BROADCAST MESSAGE
// =====================================================

async Task BroadcastAsync(string message)
{
    List<ClientInfo> currentClients =
        clients.ToList();

    foreach (ClientInfo clientInfo
             in currentClients)
    {
        try
        {
            await SendMessageAsync(
                clientInfo,
                message);
        }
        catch
        {
            // Ignore disconnected clients.
        }
    }
}


// =====================================================
// SEND MESSAGE
// =====================================================
//
// Protocol:
//
// [4 bytes message length]
// [message bytes]
//
// This solves TCP message-boundary problems.
// =====================================================

async Task SendMessageAsync(
    ClientInfo clientInfo,
    string message)
{
    byte[] messageBytes =
        Encoding.UTF8.GetBytes(message);

    byte[] lengthBytes =
        BitConverter.GetBytes(
            messageBytes.Length);

    NetworkStream stream =
        clientInfo.Client.GetStream();

    await stream.WriteAsync(
        lengthBytes);

    await stream.WriteAsync(
        messageBytes);
}


// =====================================================
// RECEIVE MESSAGE
// =====================================================

async Task<string?> ReceiveMessageAsync(
    NetworkStream stream)
{
    byte[] lengthBuffer =
        new byte[sizeof(int)];

    bool lengthReceived =
        await ReadExactlyAsync(
            stream,
            lengthBuffer);

    if (!lengthReceived)
    {
        return null;
    }

    int messageLength =
        BitConverter.ToInt32(
            lengthBuffer,
            0);

    // Security / sanity check
    if (messageLength <= 0 ||
        messageLength > 1024 * 1024)
    {
        throw new InvalidOperationException(
            "Invalid message length.");
    }

    byte[] messageBuffer =
        new byte[messageLength];

    bool messageReceived =
        await ReadExactlyAsync(
            stream,
            messageBuffer);

    if (!messageReceived)
    {
        return null;
    }

    return Encoding.UTF8.GetString(
        messageBuffer);
}


// =====================================================
// READ EXACTLY N BYTES
// =====================================================

async Task<bool> ReadExactlyAsync(
    NetworkStream stream,
    byte[] buffer)
{
    int totalBytesRead = 0;

    while (totalBytesRead < buffer.Length)
    {
        int bytesRead =
            await stream.ReadAsync(
                buffer.AsMemory(
                    totalBytesRead,
                    buffer.Length - totalBytesRead));

        if (bytesRead == 0)
        {
            return false;
        }

        totalBytesRead += bytesRead;
    }

    return true;
}


// =====================================================
// BROADCAST ONLINE USERS
// =====================================================

async Task BroadcastOnlineUsersAsync()
{
    string onlineUsers =
        string.Join(
            "|",
            clients.Select(
                x => x.Username));

    string message =
        $"[ONLINE]{onlineUsers}";

    await BroadcastAsync(message);
}


// =====================================================
// CLIENT INFO
// =====================================================

class ClientInfo
{
    public TcpClient Client { get; set; } = null!;

    public string Username { get; set; } = "";
}