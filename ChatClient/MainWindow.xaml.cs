using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatClient.Controls;
using ChatLab;
using Microsoft.Win32;

namespace ChatClient;

public partial class MainWindow : Window
{
    private const string Host = "127.0.0.1";
    private TcpClient? _client;
    private string _username = "", _token = "";
    private CancellationTokenSource _session = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly string _previewDirectory = Path.Combine(Path.GetTempPath(), "ChatLab", Guid.NewGuid().ToString("N"));

    public MainWindow()
    {
        InitializeComponent();
        SetConnected(false);
        EmojiChoices.Children.Clear();
        foreach (string emoji in ColorEmoji.Symbols)
        {
            var button = new Button { Content = ColorEmoji.CreateImage(emoji, 28), Tag = emoji,
                Padding = new Thickness(7), Margin = new Thickness(2), ToolTip = emoji };
            button.Click += Emoji_Click;
            EmojiChoices.Children.Add(button);
        }
        EmojiButton.Content = ColorEmoji.CreateImage("😊", 26);
    }

    private void SetConnected(bool connected)
    {
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        DisconnectButton.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        UsernameTextBox.IsEnabled = !connected;
        SendButton.IsEnabled = MessageTextBox.IsEnabled = EmojiButton.IsEnabled = connected;
        ImageButton.IsEnabled = FileButton.IsEnabled = connected && _token.Length > 0;
        if (!connected) StatusTextBlock.Text = "Disconnected";
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        string name = UsernameTextBox.Text.Trim();
        if (name.Length is < 1 or > 40 || name.IndexOfAny(['[', ']', '|']) >= 0 || name.Any(char.IsControl) ||
            new[] { "NAME", "TOKEN", "ONLINE", "SERVER", "ATTACHMENT" }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("Tên dài 1–40 ký tự, không chứa [, ], | hoặc ký tự điều khiển; không dùng NAME, TOKEN, ONLINE, SERVER, ATTACHMENT.");
            return;
        }
        ConnectButton.IsEnabled = false;
        var client = new TcpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.ConnectAsync(Host, 5000, timeout.Token);
            _client = client;
            _session = new CancellationTokenSource();
            _username = name;
            _token = "";
            await SendMessageAsync(client.GetStream(), name, _session.Token);
            SetConnected(true);
            StatusTextBlock.Text = "Connecting as " + name;
            _ = ReceiveMessagesAsync(client, _session.Token);
        }
        catch (Exception ex)
        {
            client.Dispose();
            Disconnect();
            MessageBox.Show("Không kết nối được: " + ex.Message);
        }
        finally { ConnectButton.IsEnabled = true; }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e) => Disconnect();

    private void Disconnect()
    {
        _session.Cancel();
        _client?.Dispose();
        _client = null;
        _token = "";
        SetConnected(false);
        OnlineUsersPanel.Children.Clear();
    }

    private async Task ReceiveMessagesAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            while (!ct.IsCancellationRequested)
            {
                byte[] header = new byte[4];
                await stream.ReadExactlyAsync(header, ct);
                int length = BitConverter.ToInt32(header);
                if (length <= 0 || length > 1024 * 1024) throw new InvalidDataException("Invalid message length.");
                byte[] bytes = new byte[length];
                await stream.ReadExactlyAsync(bytes, ct);
                ProcessIncomingMessage(Encoding.UTF8.GetString(bytes));
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_client, client))
            {
                Disconnect();
                AddSystemMessage("Mất kết nối: " + ex.Message);
            }
        }
    }

    private void ProcessIncomingMessage(string message)
    {
        if (message.StartsWith("[NAME]"))
        {
            _username = message[6..];
            UsernameTextBox.Text = _username;
            StatusTextBlock.Text = "Connected as " + _username;
        }
        else if (message.StartsWith("[TOKEN]"))
        {
            _token = message[7..];
            SetConnected(true);
        }
        else if (message.StartsWith("[ONLINE]"))
        {
            OnlineUsersPanel.Children.Clear();
            foreach (string name in message[8..].Split('|', StringSplitOptions.RemoveEmptyEntries))
                OnlineUsersPanel.Children.Add(new TextBlock { Text = "● " + name, Foreground = Brushes.LightGreen,
                    Margin = new Thickness(0, 4, 0, 4) });
        }
        else if (message.StartsWith("[SERVER]")) AddSystemMessage(message[8..]);
        else if (message.StartsWith("[ATTACHMENT]"))
        {
            var item = JsonSerializer.Deserialize<Attachment>(message[12..])
                ?? throw new InvalidDataException("Invalid attachment.");
            ShowAttachment(item);
        }
        else if (message.StartsWith('[') && message.IndexOf(']') is int end && end > 1)
        {
            string name = message[1..end];
            var bubble = new MessageBubble();
            bubble.SetMessage(name, message[(end + 1)..], name == _username);
            AddChatControl(bubble);
        }
    }

    private async Task SendMessageAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);
        if (data.Length > 1024 * 1024) throw new InvalidDataException("Tin nhắn quá dài.");
        await _sendLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(BitConverter.GetBytes(data.Length), ct);
            await stream.WriteAsync(data, ct);
        }
        finally { _sendLock.Release(); }
    }

    private async Task SendChatMessageAsync()
    {
        var client = _client;
        if (client == null || string.IsNullOrWhiteSpace(MessageTextBox.Text)) return;
        string text = MessageTextBox.Text.Trim();
        MessageTextBox.Clear();
        try { await SendMessageAsync(client.GetStream(), text, _session.Token); }
        catch (Exception ex) { AddSystemMessage("Gửi thất bại: " + ex.Message); }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendChatMessageAsync();
    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SendChatMessageAsync(); }
    }
    private void EmojiButton_Click(object sender, RoutedEventArgs e) => EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string emoji }) return;
        int caret = MessageTextBox.CaretIndex;
        MessageTextBox.Text = MessageTextBox.Text.Insert(caret, emoji);
        MessageTextBox.CaretIndex = caret + emoji.Length;
        MessageTextBox.Focus();
        EmojiPopup.IsOpen = false;
    }

    private async void ImageButton_Click(object sender, RoutedEventArgs e) => await ChooseUploadAsync(true);
    private async void FileButton_Click(object sender, RoutedEventArgs e) => await ChooseUploadAsync(false);

    private async Task ChooseUploadAsync(bool image)
    {
        if (_token.Length == 0) return;
        var dialog = new OpenFileDialog { Filter = image ? "Ảnh|*.png;*.jpg;*.jpeg;*.gif;*.bmp" : "Tất cả file|*.*" };
        if (dialog.ShowDialog() != true) return;
        var card = new TransferCard("Gửi " + Path.GetFileName(dialog.FileName));
        AddChatControl(card);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        card.CancelAction = cancel.Cancel;
        string token = _token;
        try
        {
            long size = new FileInfo(dialog.FileName).Length;
            if (image)
            {
                if (size > TransferProtocol.MaxImageSize) throw new InvalidDataException("Ảnh tối đa 20 MB.");
                card.ShowPreview(await Task.Run(() => LoadPreview(dialog.FileName), cancel.Token));
            }
            // The event handler yields here; another image/file can be sent immediately.
            await Task.Run(() => TransferProtocol.UploadAsync(Host, token, dialog.FileName, image,
                card.CreateProgress(size), cancel.Token), cancel.Token);
            card.Finish("Đã gửi lên server • " + TransferCard.FormatSize(size));
        }
        catch (OperationCanceledException) { card.Finish("Đã hủy."); }
        catch (Exception ex) { card.Finish("Gửi thất bại: " + ex.Message); }
    }

    private void ShowAttachment(Attachment item)
    {
        var card = new TransferCard($"{item.Sender} • {item.Name} • {TransferCard.FormatSize(item.Size)}");
        card.Finish("Sẵn sàng tải • 4 kết nối song song");
        card.AddDownload(async () =>
        {
            var dialog = new SaveFileDialog { FileName = Path.GetFileName(item.Name), Filter = "Tất cả file|*.*" };
            if (dialog.ShowDialog() == true) await DownloadAsync(item, dialog.FileName, card, false);
        });
        AddChatControl(card);
        if (item.IsImage) _ = PreviewAsync(item, card);
    }

    private async Task PreviewAsync(Attachment item, TransferCard card)
    {
        try
        {
            Directory.CreateDirectory(_previewDirectory);
            string path = Path.Combine(_previewDirectory, Guid.NewGuid().ToString("N"));
            await DownloadAsync(item, path, card, true);
        }
        catch (Exception ex) { card.Finish("Không xem được ảnh: " + ex.Message); }
    }

    private async Task DownloadAsync(Attachment item, string path, TransferCard card, bool preview)
    {
        if (_token.Length == 0) { card.Finish("Hãy kết nối lại trước khi tải."); return; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(_session.Token);
        card.Start(cancel.Cancel);
        string token = _token;
        try
        {
            await Task.Run(() => TransferProtocol.DownloadAsync(Host, token, item, path,
                card.CreateProgress(item.Size), cancel.Token), cancel.Token);
            if (preview) card.ShowPreview(await Task.Run(() => LoadPreview(path), cancel.Token));
            card.Finish(preview ? "Ảnh đã nhận • SHA-256 OK" : "Đã lưu: " + path + " • SHA-256 OK");
        }
        catch (OperationCanceledException) { card.Finish("Đã hủy."); }
        catch (Exception ex) { card.Finish("Tải thất bại: " + ex.Message); }
        finally { if (preview && File.Exists(path)) File.Delete(path); }
    }

    private static BitmapImage LoadPreview(string path)
    {
        using var file = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 480;
        image.StreamSource = file;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void AddSystemMessage(string message)
    {
        var bubble = new MessageBubble();
        bubble.SetSystemMessage(message);
        AddChatControl(bubble);
    }
    private void AddChatControl(UIElement control)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;
        ChatPanel.Children.Add(control);
        ChatScrollViewer.ScrollToEnd();
    }
    protected override void OnClosed(EventArgs e)
    {
        Disconnect();
        base.OnClosed(e);
    }
}
