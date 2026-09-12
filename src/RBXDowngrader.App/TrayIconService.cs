using System.Diagnostics;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace RBXDowngrader;

public sealed class TrayIconService : IDisposable
{
    private readonly Window _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private bool _disposed;

    public TrayIconService(Window window)
    {
        _window = window;
        _menu = new Forms.ContextMenuStrip();

        var restoreItem = new Forms.ToolStripMenuItem("Restore");
        restoreItem.Click += (_, _) => Dispatch(Restore);
        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Dispatch(_window.Close);
        _menu.Items.Add(restoreItem);
        _menu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = LoadApplicationIcon(),
            Text = "RBXDowngrader",
            Visible = false
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatch(Restore);
    }

    public bool IsHiddenToTray => _notifyIcon.Visible && !_window.IsVisible;

    public void HideToTray()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _window.ShowInTaskbar = false;
        _window.Hide();
        _notifyIcon.Visible = true;
    }

    public void Restore()
    {
        if (_disposed)
            return;

        _notifyIcon.Visible = false;
        _window.ShowInTaskbar = true;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
    }

    private void Dispatch(Action action)
    {
        if (_disposed)
            return;

        _window.Dispatcher.BeginInvoke(action);
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                var icon = Icon.ExtractAssociatedIcon(executable);
                if (icon is not null)
                    return icon;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load tray icon: {ex.Message}");
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
