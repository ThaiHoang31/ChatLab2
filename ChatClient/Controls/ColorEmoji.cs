using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ChatClient.Controls;

// Local vector artwork: WPF renders the colors without depending on color-font support.
// Unicode still travels over TCP, so copying/storing a message preserves its text.
public static class ColorEmoji
{
    public static readonly string[] Symbols = ["😀", "😊", "😂", "😍", "😎", "😢", "😡", "😱", "❤️", "💚", "💙", "💜"];
    private static readonly Dictionary<string, ImageSource> Images = Symbols.ToDictionary(s => s, Draw);

    public static Image CreateImage(string symbol, double size) => new()
    {
        Source = Images[symbol], Width = size, Height = size, ToolTip = symbol
    };

    public static void SetText(TextBlock block, string text)
    {
        block.Inlines.Clear();
        var plainText = new StringBuilder();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            if (Images.ContainsKey(element))
            {
                if (plainText.Length > 0) { block.Inlines.Add(new Run(plainText.ToString())); plainText.Clear(); }
                block.Inlines.Add(new InlineUIContainer(CreateImage(element, 24)) { BaselineAlignment = BaselineAlignment.Center });
            }
            else plainText.Append(element);
        }
        if (plainText.Length > 0) block.Inlines.Add(new Run(plainText.ToString()));
    }

    private static ImageSource Draw(string symbol)
    {
        var group = new DrawingGroup();
        using (var d = group.Open())
        {
            d.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 32, 32));
            if (symbol is "❤️" or "💚" or "💙" or "💜")
            {
                Brush color = symbol switch { "💚" => Brushes.LimeGreen, "💙" => Brushes.DodgerBlue, "💜" => Brushes.MediumPurple, _ => Brushes.Crimson };
                d.DrawGeometry(color, null, Geometry.Parse("M16,29 C12,25 2,18 2,10 C2,1 12,0 16,8 C20,0 30,1 30,10 C30,18 20,25 16,29 Z"));
                d.DrawEllipse(Brushes.LightPink, null, new Point(8, 8), 3, 2);
            }
            else
            {
                d.DrawEllipse(symbol == "😡" ? Brushes.Coral : Brushes.Gold, new Pen(Brushes.DarkOrange, 1), new Point(16, 16), 14, 14);
                if (symbol == "😎")
                {
                    d.DrawRoundedRectangle(Brushes.MidnightBlue, null, new Rect(5, 9, 10, 7), 2, 2);
                    d.DrawRoundedRectangle(Brushes.MidnightBlue, null, new Rect(17, 9, 10, 7), 2, 2);
                    d.DrawLine(new Pen(Brushes.MidnightBlue, 2), new Point(5, 10), new Point(27, 10));
                }
                else if (symbol == "😍")
                {
                    d.DrawGeometry(Brushes.Crimson, null, Geometry.Parse("M10,16 L5,10 C3,5 10,5 10,9 C10,5 17,5 15,10 Z M23,16 L18,10 C16,5 23,5 23,9 C23,5 30,5 28,10 Z"));
                }
                else
                {
                    d.DrawEllipse(Brushes.SaddleBrown, null, new Point(11, 12), 1.7, 2.2);
                    d.DrawEllipse(Brushes.SaddleBrown, null, new Point(21, 12), 1.7, 2.2);
                }
                if (symbol is "😢" or "😂")
                    d.DrawGeometry(Brushes.DeepSkyBlue, null, Geometry.Parse("M7,14 C3,20 3,24 7,24 C11,24 11,20 7,14 Z"));
                if (symbol == "😱") d.DrawEllipse(Brushes.SaddleBrown, null, new Point(16, 23), 4, 5);
                else if (symbol is "😢" or "😡")
                    d.DrawGeometry(null, new Pen(Brushes.SaddleBrown, 2), Geometry.Parse("M10,25 Q16,18 22,25"));
                else
                {
                    d.DrawGeometry(Brushes.SaddleBrown, null, Geometry.Parse("M8,19 Q16,23 24,19 Q22,30 16,28 Q10,28 8,19 Z"));
                    d.DrawGeometry(Brushes.White, null, Geometry.Parse("M10,20 Q16,23 22,20 L21,23 L11,23 Z"));
                    d.DrawEllipse(Brushes.HotPink, null, new Point(16, 26), 3, 1.5);
                }
                if (symbol == "😊")
                {
                    d.DrawEllipse(Brushes.Salmon, null, new Point(6, 18), 2.5, 1.5);
                    d.DrawEllipse(Brushes.Salmon, null, new Point(26, 18), 2.5, 1.5);
                }
                if (symbol == "😡")
                {
                    d.DrawLine(new Pen(Brushes.SaddleBrown, 2), new Point(7, 7), new Point(13, 10));
                    d.DrawLine(new Pen(Brushes.SaddleBrown, 2), new Point(19, 10), new Point(25, 7));
                }
            }
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
