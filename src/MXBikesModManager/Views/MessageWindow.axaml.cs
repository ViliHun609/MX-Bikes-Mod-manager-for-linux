using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MXBikesModManager.Views;

public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
    }

    public static MessageWindow CreateInfo(string title, string message)
    {
        var window = new MessageWindow { Title = title };
        window.MessageText.Text = message;
        window.OkButton.Content = "OK";
        window.CancelButton.IsVisible = false;
        return window;
    }

    public static MessageWindow CreateConfirm(string title, string message, string confirmText)
    {
        var window = new MessageWindow { Title = title };
        window.MessageText.Text = message;
        window.OkButton.Content = confirmText;
        window.CancelButton.IsVisible = true;
        return window;
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
