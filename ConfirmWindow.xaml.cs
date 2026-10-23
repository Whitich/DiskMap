using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DiskMap.Util;

namespace DiskMap;

public partial class ConfirmWindow : Window
{
    public ConfirmWindow(string message, string? confirmText)
    {
        InitializeComponent();
        MessageText.Text = message;
        ConfirmButton.Content = confirmText ?? Loc.T("L.Confirm.Continue");
        Loaded += (_, _) => CancelButton.Focus();
    }

    public static bool Show(Window owner, string message, string? confirmText = null)
    {
        var dialog = new ConfirmWindow(message, confirmText) { Owner = owner };
        return dialog.ShowDialog() == true;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int enabled = 1;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // eski Windows sürümlerinde yoksay
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
