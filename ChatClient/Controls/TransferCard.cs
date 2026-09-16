using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChatClient.Controls;

public sealed class TransferCard : Border
{
    private readonly StackPanel _panel = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 100, Height = 6 };
    private readonly Button _cancel = new() { Content = "Hủy", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3, 10, 3) };
    private Button? _download;
    public Action? CancelAction { get; set; }

    public TransferCard(string title)
    {
        Background = Brushes.White;
        BorderBrush = Brushes.LightGray;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(14);
        Margin = new Thickness(0, 6, 0, 6);
        MaxWidth = 520;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = _panel;
        _panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        _panel.Children.Add(_status);
        _panel.Children.Add(_progress);
        _panel.Children.Add(_cancel);
        _cancel.Click += (_, _) => CancelAction?.Invoke();
    }
    public void Start(Action cancel)
    {
        CancelAction = cancel;
        _cancel.Visibility = Visibility.Visible;
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _status.Text = "Đang truyền...";
        if (_download != null) _download.IsEnabled = false;
    }
    public void Finish(string status)
    {
        _status.Text = status;
        _cancel.Visibility = _progress.Visibility = Visibility.Collapsed;
        CancelAction = null;
        if (_download != null) _download.IsEnabled = true;
    }
    public void ShowPreview(ImageSource image) => _panel.Children.Insert(1, new Image
    {
        Source = image, MaxWidth = 400, MaxHeight = 260, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8)
    });
    public void AddDownload(Func<Task> action)
    {
        _download = new Button { Content = "Lưu file...", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 4, 10, 4) };
        _download.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception ex) { Finish("Không lưu được: " + ex.Message); }
        };
        _panel.Children.Add(_download);
    }
    public IProgress<long> CreateProgress(long size) => new ThrottledProgress(bytes =>
    {
        if (_cancel.Visibility != Visibility.Visible) return;
        double percent = size == 0 ? 100 : 100.0 * bytes / size;
        _progress.Value = percent;
        _status.Text = $"{percent:F0}% • {FormatSize(bytes)} / {FormatSize(size)}" +
            (bytes == size ? " • Đang xác minh..." : "");
    }, Dispatcher);
    public static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1048576.0:F1} MB" : $"{bytes / 1024.0:F1} KB";

    // Avoid enqueueing a UI update for every 64 KB block, particularly with four workers.
    private sealed class ThrottledProgress(Action<long> update, System.Windows.Threading.Dispatcher dispatcher) : IProgress<long>
    {
        private readonly object _gate = new();
        private readonly Stopwatch _timer = Stopwatch.StartNew();
        private long _largest;
        public void Report(long value)
        {
            lock (_gate)
            {
                _largest = Math.Max(_largest, value);
                if (_timer.ElapsedMilliseconds < 100) return;
                _timer.Restart();
                long current = _largest;
                dispatcher.BeginInvoke(() => update(current));
            }
        }
    }
}
