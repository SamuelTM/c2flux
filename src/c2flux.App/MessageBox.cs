using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace c2flux
{
    // shortcut: a plain OK box until AppDialogs (Ant-style dialogs) is ported
    // in phase 5.3.
    public static class MessageBox
    {
        public static Task ShowAsync(Window owner, string message)
        {
            Button ok = new Button { Content = LocalizationService.GetText("Common.OK"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, Classes = { "ant" } };
            Window dialog = new Window
            {
                Title = AppConstants.ApplicationName,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(16),
                    Spacing = 16,
                    MaxWidth = 480,
                    Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, ok },
                },
            };
            ok.Click += (_, _) => dialog.Close();
            return dialog.ShowDialog(owner);
        }
    }
}
