using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChatClient.Controls;

public partial class MessageBubble : UserControl
{
    public MessageBubble()
    {
        InitializeComponent();
    }


    // =====================================================
    // SET NORMAL MESSAGE
    // =====================================================

    public void SetMessage(
        string username,
        string message,
        bool isMine)
    {
        if (isMine)
        {
            ShowMyMessage(
                username,
                message);
        }
        else
        {
            ShowOtherMessage(
                username,
                message);
        }
    }


    // =====================================================
    // MY MESSAGE
    // =====================================================

    private void ShowMyMessage(
        string username,
        string message)
    {
        OtherMessageContainer.Visibility =
            Visibility.Collapsed;

        MyMessageContainer.Visibility =
            Visibility.Visible;

        ColorEmoji.SetText(MyMessageText, message);

        MyUsernameText.Text =
            username;

        MyTimestampText.Text =
            DateTime.Now.ToString(
                "HH:mm");
    }


    // =====================================================
    // OTHER USER MESSAGE
    // =====================================================

    private void ShowOtherMessage(
        string username,
        string message)
    {
        MyMessageContainer.Visibility =
            Visibility.Collapsed;

        OtherMessageContainer.Visibility =
            Visibility.Visible;

        OtherUsernameText.Text =
            username;

        ColorEmoji.SetText(OtherMessageText, message);

        OtherTimestampText.Text =
            DateTime.Now.ToString(
                "HH:mm");

        OtherAvatarText.Text =
            GetInitial(username);
    }


    // =====================================================
    // SYSTEM MESSAGE
    // =====================================================

    public void SetSystemMessage(
        string message)
    {
        OtherMessageContainer.Visibility =
            Visibility.Collapsed;

        MyMessageContainer.Visibility =
            Visibility.Collapsed;

        Border systemBorder =
            new Border
            {
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            243,
                            244,
                            246)),

                CornerRadius =
                    new CornerRadius(12),

                Padding =
                    new Thickness(
                        12,
                        6,
                        12,
                        6),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                Margin =
                    new Thickness(
                        0,
                        8,
                        0,
                        8)
            };

        TextBlock text =
            new TextBlock
            {
                Text = message,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            107,
                            114,
                            128)),

                FontSize = 12,

                TextAlignment =
                    TextAlignment.Center
            };

        systemBorder.Child =
            text;

        RootGrid.Children.Add(
            systemBorder);
    }


    // =====================================================
    // GET AVATAR INITIAL
    // =====================================================

    private static string GetInitial(
        string username)
    {
        if (string.IsNullOrWhiteSpace(
            username))
        {
            return "U";
        }

        return username
            .Trim()
            .Substring(0, 1)
            .ToUpperInvariant();
    }
}
