using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatClient.Controls;

namespace ChatClient;

public partial class MainWindow : Window
{
    private TcpClient? _client;

    private string _username = "";

    private bool _isConnected = false;


    // =====================================================
    // CONSTRUCTOR
    // =====================================================

    public MainWindow()
    {
        InitializeComponent();

        StatusTextBlock.Text =
            "Disconnected";

        ConnectButton.Visibility =
            Visibility.Visible;

        DisconnectButton.Visibility =
            Visibility.Collapsed;

        SendButton.IsEnabled = false;

        MessageTextBox.IsEnabled = false;
    }


    // =====================================================
    // CONNECT
    // =====================================================

    private async void ConnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isConnected)
        {
            return;
        }

        try
        {
            string username =
                UsernameTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show(
                    "Please enter a username.",
                    "ChatLab",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                UsernameTextBox.Focus();

                return;
            }

            _client =
                new TcpClient();

            await _client.ConnectAsync(
                "127.0.0.1",
                5000);

            NetworkStream stream =
                _client.GetStream();

            // =============================================
            // SEND USERNAME USING FRAMED MESSAGE
            // =============================================

            await SendMessageAsync(
                stream,
                username);

            _isConnected = true;

            _username = username;

            StatusTextBlock.Text =
                $"Connecting as {_username}...";

            ConnectButton.Visibility =
                Visibility.Collapsed;

            DisconnectButton.Visibility =
                Visibility.Visible;

            UsernameTextBox.IsEnabled =
                false;

            SendButton.IsEnabled =
                true;

            MessageTextBox.IsEnabled =
                true;

            MessageTextBox.Focus();

            ClearOnlineUsers();

            AddSystemMessage(
                "Connecting to server...");

            // =============================================
            // START RECEIVE LOOP
            // =============================================

            _ = ReceiveMessagesAsync();
        }
        catch (Exception ex)
        {
            _client?.Close();

            _client = null;

            _isConnected = false;

            MessageBox.Show(
                $"Could not connect to server.\n\n{ex.Message}",
                "Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }


    // =====================================================
    // DISCONNECT BUTTON
    // =====================================================

    private async void DisconnectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await DisconnectAsync(
            showSystemMessage: true);
    }


    // =====================================================
    // DISCONNECT
    // =====================================================

    private async Task DisconnectAsync(
        bool showSystemMessage)
    {
        if (!_isConnected &&
            _client == null)
        {
            return;
        }

        _isConnected = false;

        try
        {
            if (_client != null)
            {
                _client.Close();

                _client.Dispose();

                _client = null;
            }
        }
        catch
        {
        }

        StatusTextBlock.Text =
            "Disconnected";

        ConnectButton.Visibility =
            Visibility.Visible;

        DisconnectButton.Visibility =
            Visibility.Collapsed;

        UsernameTextBox.IsEnabled =
            true;

        SendButton.IsEnabled =
            false;

        MessageTextBox.IsEnabled =
            false;

        MessageTextBox.Clear();

        ClearOnlineUsers();

        if (showSystemMessage)
        {
            AddSystemMessage(
                "You have disconnected from the server.");
        }

        await Task.CompletedTask;
    }


    // =====================================================
    // SEND BUTTON
    // =====================================================

    private async void SendButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SendChatMessageAsync();
    }


    // =====================================================
    // SEND CHAT MESSAGE
    // =====================================================

    private async Task SendChatMessageAsync()
    {
        if (!_isConnected ||
            _client == null)
        {
            return;
        }

        string message =
            MessageTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            NetworkStream stream =
                _client.GetStream();

            await SendMessageAsync(
                stream,
                message);

            // Server will broadcast
            // the message back to us.

            MessageTextBox.Clear();

            MessageTextBox.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to send message.\n\n{ex.Message}",
                "ChatLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            await DisconnectAsync(
                showSystemMessage: true);
        }
    }


    // =====================================================
    // RECEIVE LOOP
    // =====================================================

    private async Task ReceiveMessagesAsync()
    {
        if (_client == null)
        {
            return;
        }

        TcpClient client =
            _client;

        try
        {
            NetworkStream stream =
                client.GetStream();

            while (_isConnected)
            {
                string? message =
                    await ReceiveMessageAsync(
                        stream);

                if (message == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        ProcessIncomingMessage(
                            message);
                    });
            }
        }
        catch
        {
            // Connection closed.
        }

        // Only handle unexpected disconnect.
        if (_isConnected)
        {
            await Dispatcher.InvokeAsync(
                async () =>
                {
                    await DisconnectAsync(
                        showSystemMessage: true);
                });
        }
    }


    // =====================================================
    // PROCESS MESSAGE
    // =====================================================

    private void ProcessIncomingMessage(
        string message)
    {
        // =================================================
        // SERVER ASSIGNED NAME
        // =================================================

        if (message.StartsWith(
            "[NAME]",
            StringComparison.Ordinal))
        {
            string serverUsername =
                message.Substring(
                    "[NAME]".Length);

            if (!string.IsNullOrWhiteSpace(
                serverUsername))
            {
                _username =
                    serverUsername;

                UsernameTextBox.Text =
                    _username;

                StatusTextBlock.Text =
                    $"Connected as {_username}";
            }

            return;
        }


        // =================================================
        // ONLINE USERS
        // =================================================

        if (message.StartsWith(
            "[ONLINE]",
            StringComparison.Ordinal))
        {
            string users =
                message.Substring(
                    "[ONLINE]".Length);

            string[] onlineUsers =
                users.Split(
                    '|',
                    StringSplitOptions
                        .RemoveEmptyEntries);

            UpdateOnlineUsers(
                onlineUsers);

            return;
        }


        // =================================================
        // SERVER MESSAGE
        // =================================================

        if (message.StartsWith(
            "[SERVER]",
            StringComparison.Ordinal))
        {
            string serverMessage =
                message.Substring(
                    "[SERVER]".Length);

            AddSystemMessage(
                serverMessage);

            return;
        }


        // =================================================
        // NORMAL CHAT MESSAGE
        // =================================================

        if (message.StartsWith("["))
        {
            int closingBracket =
                message.IndexOf(']');

            if (closingBracket > 1)
            {
                string username =
                    message.Substring(
                        1,
                        closingBracket - 1);

                string content =
                    message.Substring(
                        closingBracket + 1);

                bool isMine =
                    string.Equals(
                        username,
                        _username,
                        StringComparison.OrdinalIgnoreCase);

                AddMessageBubble(
                    username,
                    content,
                    isMine);

                return;
            }
        }


        // =================================================
        // UNKNOWN MESSAGE
        // =================================================

        AddSystemMessage(
            message);
    }


    // =====================================================
    // UPDATE ONLINE USERS
    // =====================================================

    private void UpdateOnlineUsers(
        IEnumerable<string> users)
    {
        OnlineUsersPanel.Children.Clear();

        List<string> userList =
            users
                .Where(
                    x => !string.IsNullOrWhiteSpace(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (userList.Count == 0)
        {
            TextBlock emptyText =
                new TextBlock
                {
                    Text = "No one online",

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                114,
                                118,
                                125)),

                    FontSize = 13
                };

            OnlineUsersPanel.Children.Add(
                emptyText);

            return;
        }

        foreach (string username
                 in userList)
        {
            StackPanel userRow =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    Margin =
                        new Thickness(
                            0,
                            4,
                            0,
                            4)
                };

            // Green online dot

            Border onlineDot =
                new Border
                {
                    Width = 8,

                    Height = 8,

                    CornerRadius =
                        new CornerRadius(4),

                    Background =
                        new SolidColorBrush(
                            Color.FromRgb(
                                35,
                                165,
                                90)),

                    Margin =
                        new Thickness(
                            2,
                            0,
                            10,
                            0),

                    VerticalAlignment =
                        VerticalAlignment.Center
                };


            // Username

            TextBlock usernameText =
                new TextBlock
                {
                    Text = username,

                    Foreground =
                        Brushes.White,

                    FontSize = 13,

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            userRow.Children.Add(
                onlineDot);

            userRow.Children.Add(
                usernameText);

            OnlineUsersPanel.Children.Add(
                userRow);
        }
    }


    // =====================================================
    // CLEAR ONLINE USERS
    // =====================================================

    private void ClearOnlineUsers()
    {
        OnlineUsersPanel.Children.Clear();

        TextBlock text =
            new TextBlock
            {
                Text = "Not connected",

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            114,
                            118,
                            125)),

                FontSize = 13
            };

        OnlineUsersPanel.Children.Add(
            text);
    }


    // =====================================================
    // ADD MESSAGE BUBBLE
    // =====================================================

    private void AddMessageBubble(
        string username,
        string message,
        bool isMine)
    {
        HideWelcomePanel();

        MessageBubble bubble =
            new MessageBubble();

        bubble.SetMessage(
            username,
            message,
            isMine);

        ChatPanel.Children.Add(
            bubble);

        ScrollChatToBottom();
    }


    // =====================================================
    // ADD SYSTEM MESSAGE
    // =====================================================

    private void AddSystemMessage(
        string message)
    {
        HideWelcomePanel();

        MessageBubble bubble =
            new MessageBubble();

        bubble.SetSystemMessage(
            message);

        ChatPanel.Children.Add(
            bubble);

        ScrollChatToBottom();
    }


    // =====================================================
    // HIDE WELCOME
    // =====================================================

    private void HideWelcomePanel()
    {
        WelcomePanel.Visibility =
            Visibility.Collapsed;
    }


    // =====================================================
    // AUTO SCROLL
    // =====================================================

    private void ScrollChatToBottom()
    {
        ChatScrollViewer.ScrollToEnd();
    }


    // =====================================================
    // ENTER TO SEND
    // =====================================================

    private async void MessageTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            await SendChatMessageAsync();
        }
    }


    // =====================================================
    // EMOJI BUTTON
    // =====================================================

    private void EmojiButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        EmojiPopup.IsOpen =
            !EmojiPopup.IsOpen;
    }


    // =====================================================
    // SELECT EMOJI
    // =====================================================

    private void Emoji_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        string emoji =
            button.Tag?.ToString() ?? "";

        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        int caretIndex =
            MessageTextBox.CaretIndex;

        MessageTextBox.Text =
            MessageTextBox.Text.Insert(
                caretIndex,
                emoji);

        MessageTextBox.CaretIndex =
            caretIndex + emoji.Length;

        MessageTextBox.Focus();

        EmojiPopup.IsOpen =
            false;
    }


    // =====================================================
    // SEND FRAMED MESSAGE
    // =====================================================

    private async Task SendMessageAsync(
        NetworkStream stream,
        string message)
    {
        byte[] messageBytes =
            Encoding.UTF8.GetBytes(
                message);

        byte[] lengthBytes =
            BitConverter.GetBytes(
                messageBytes.Length);

        await stream.WriteAsync(
            lengthBytes);

        await stream.WriteAsync(
            messageBytes);
    }


    // =====================================================
    // RECEIVE FRAMED MESSAGE
    // =====================================================

    private async Task<string?> ReceiveMessageAsync(
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

    private async Task<bool> ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer)
    {
        int totalBytesRead = 0;

        while (totalBytesRead <
               buffer.Length)
        {
            int bytesRead =
                await stream.ReadAsync(
                    buffer.AsMemory(
                        totalBytesRead,
                        buffer.Length -
                        totalBytesRead));

            if (bytesRead == 0)
            {
                return false;
            }

            totalBytesRead +=
                bytesRead;
        }

        return true;
    }


    // =====================================================
    // WINDOW CLOSED
    // =====================================================

    protected override void OnClosed(
        EventArgs e)
    {
        try
        {
            _isConnected = false;

            _client?.Close();

            _client?.Dispose();

            _client = null;
        }
        catch
        {
        }

        base.OnClosed(e);
    }
}