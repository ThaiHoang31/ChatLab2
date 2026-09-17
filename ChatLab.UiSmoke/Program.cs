using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ChatClient;
using ChatClient.Controls;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var window = new MainWindow();
        var content = (FrameworkElement)window.Content;
        ((FrameworkElement)window.FindName("WelcomePanel")).Visibility = Visibility.Collapsed;
        var panel = (StackPanel)window.FindName("ChatPanel");
        var bubble = new MessageBubble();
        bubble.SetMessage("Alice", "Emoji nhiều sắc màu: " + string.Concat(ColorEmoji.Symbols), false);
        panel.Children.Add(bubble);
        var own = new MessageBubble();
        own.SetMessage("Bob", "Đang gửi file, vẫn gửi được ảnh! 😊 ❤️", true);
        panel.Children.Add(own);
        var file = new TransferCard("Bạn • bài-thực-hành.zip • 512 MB", isMine: true);
        file.Start(() => { });
        panel.Children.Add(file);
        // Simulate the server echo before/after upload acknowledgement: no second card.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(MainWindow).GetField("_username", flags)!.SetValue(window, "Bob");
        var showAttachment = typeof(MainWindow).GetMethod("ShowAttachment", flags)!;
        int countBeforeEcho = panel.Children.Count;
        foreach (bool isImage in new[] { false, true })
            showAttachment.Invoke(window, [new ChatLab.Attachment("own", "same-name", 10, isImage, "Bob", "hash")]);
        if (panel.Children.Count != countBeforeEcho || file.HorizontalAlignment != HorizontalAlignment.Right)
            throw new Exception("Outgoing attachments must keep one right-aligned card.");
        showAttachment.Invoke(window, [new ChatLab.Attachment("other", "received.zip", 10, false, "Alice", "hash")]);
        if (panel.Children.Count != countBeforeEcho + 1 ||
            ((TransferCard)panel.Children[panel.Children.Count - 1]).HorizontalAlignment != HorizontalAlignment.Left)
            throw new Exception("Incoming attachment must remain on the left.");
        var image = new TransferCard("Alice • ảnh.png");
        image.ShowPreview(((Image)ColorEmoji.CreateImage("😍", 160)).Source);
        image.Finish("Ảnh đã nhận • SHA-256 OK");
        image.AddDownload(() => Task.CompletedTask, imageOnly: true);
        panel.Children.Add(image);
        content.Measure(new Size(1100, 700));
        content.Arrange(new Rect(0, 0, 1100, 700));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1100, 700, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        string output = Path.GetFullPath("artifacts/ui-smoke.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(output)) encoder.Save(stream);
        if (((FrameworkElement)window.FindName("ImageButton")).ActualHeight < 10)
            throw new Exception("Image button layout failed.");
        Console.WriteLine("PASS: WPF window, colored emoji, attachment cards rendered: " + output);
        window.Close();
    }
}
