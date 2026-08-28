using System.Windows.Input;

namespace CryptoScanner.UI;

public partial class MainWindow
{
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            var window = new AltseasonWindow { Owner = this };
            window.Show();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
