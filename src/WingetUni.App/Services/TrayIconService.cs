using System.Drawing;
using System.IO;
using WinForms = System.Windows.Forms;

namespace WingetUni.App.Services;

/// <summary>
/// The notification area icon. It keeps the application reachable while its window is hidden,
/// and it is where an "updates are available" notice appears.
/// </summary>
/// <remarks>
/// Every type is written through a <c>WinForms</c> alias. The project also builds WPF, which
/// defines types with the same names, so aliasing keeps the two toolkits from being confused.
/// </remarks>
public sealed class TrayIconService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIconService(string title, Action open, Action exit)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(exit);

        _menu = new WinForms.ContextMenuStrip();
        _menu.Items.Add("Open", null, (_, _) => open());
        _menu.Items.Add(new WinForms.ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = title,
            Visible = true,
            ContextMenuStrip = _menu,
        };

        _icon.DoubleClick += (_, _) => open();
    }

    /// <summary>
    /// The application's own icon, embedded as a resource. Falls back to the generic system icon
    /// if it cannot be read, so a broken asset leaves the tray usable rather than empty.
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/WingetUni.ico"));

            if (resource?.Stream is { } stream)
            {
                using (stream)
                {
                    return new Icon(stream);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            // Fall through to the clone of the system icon below.
        }

        // Cloned because NotifyIcon takes ownership of the icon it is given and disposes it,
        // and SystemIcons.Application is a shared instance that must not be disposed.
        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>Shows a balloon telling the user how many updates the last check found.</summary>
    public void ShowUpdateNotice(int count)
    {
        var message = count == 1
            ? "1 update is available."
            : $"{count} updates are available.";

        _icon.ShowBalloonTip(6000, "Winget Uni", message, WinForms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
