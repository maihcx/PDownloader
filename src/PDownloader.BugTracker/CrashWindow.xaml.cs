// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using static PDownloader.BugTracker.ReportPopup;

namespace PDownloader.BugTracker;

public partial class CrashWindow : Window
{
    private readonly string? _logPath;
    private string _rawLog = string.Empty;

    // ── Palette ──────────────────────────────────────────────────────────────

    private static readonly SolidColorBrush BrushDefault = MakeBrush("#CBD5E1");
    private static readonly SolidColorBrush BrushHeader = MakeBrush("#F8FAFC");
    private static readonly SolidColorBrush BrushSeparator = MakeBrush("#1E3A5F");
    private static readonly SolidColorBrush BrushLabel = MakeBrush("#60A5FA");
    private static readonly SolidColorBrush BrushError = MakeBrush("#F87171");
    private static readonly SolidColorBrush BrushValue = MakeBrush("#34D399");
    private static readonly SolidColorBrush BrushStackAt = MakeBrush("#94A3B8");
    private static readonly SolidColorBrush BrushStackPath = MakeBrush("#475569");
    private static readonly SolidColorBrush BrushMeta = MakeBrush("#A78BFA");
    private static readonly SolidColorBrush BrushInner = MakeBrush("#FB923C");

    // ── Constructor ──────────────────────────────────────────────────────────

    public CrashWindow(string? logPath, string appName)
    {
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;

        Title = $"{appName} — Bug Tracker";
        _logPath = logPath;
        LoadLog();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        ApplyDwm(hwnd);
    }

    private static void ApplyDwm(IntPtr hwnd)
    {
        // Dark mode title bar
        int dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Caption màu khớp header: #0F1F35
        int caption = ToBgr(0x0F, 0x1F, 0x35);
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

        // Text trắng
        int text = ToBgr(0xF8, 0xFA, 0xFC);
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_TEXT_COLOR, ref text, sizeof(int));

        // Border màu accent
        int border = ToBgr(0x1E, 0x3A, 0x5F);
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));

        // Mica backdrop
        int mica = NativeMethods.DWMSBT_MAINWINDOW;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));

        // Extend frame — cần thiết để Mica render vào client area
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    // DWM dùng 0x00BBGGRR
    private static int ToBgr(byte r, byte g, byte b)
        => b << 16 | g << 8 | r;

    // ── Close button handler (không dùng SystemCommands nữa) ──────────────

    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    // ── Load & render ────────────────────────────────────────────────────────

    private void LoadLog()
    {
        if (_logPath == null || !File.Exists(_logPath))
        {
            ShowPlainError("Log file not found.\nPlease check the path passed as argument.");
            LogPathText.Text = _logPath ?? "(no path provided)";
            OpenFolderBtn.IsEnabled = false;
            DeleteBtn.IsEnabled = false;
            return;
        }

        try
        {
            _rawLog = File.ReadAllText(_logPath);
        }
        catch (Exception ex)
        {
            ShowPlainError($"Could not read log file:\n{ex.Message}");
            return;
        }

        LogPathText.Text = _logPath;
        RenderLog(_rawLog);
        BuildMetaBar(_rawLog);
    }

    private void ShowPlainError(string message)
    {
        LogBlock.Inlines.Clear();
        LogBlock.Inlines.Add(new Run(message) { Foreground = BrushError });
    }

    // ── Syntax-highlighted render ─────────────────────────────────────────────

    private void RenderLog(string log)
    {
        LogBlock.Inlines.Clear();

        foreach (string rawLine in log.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            RenderLine(line);
            LogBlock.Inlines.Add(new Run("\n"));
        }
    }

    private void RenderLine(string line)
    {
        if (line.StartsWith('─') || (line.StartsWith('-') && line.Length > 20 && line.Trim('-').Length == 0))
        {
            LogBlock.Inlines.Add(new Run(line) { Foreground = BrushSeparator });
            return;
        }

        if (line.StartsWith("PDownloader") || line.StartsWith("App Crash") || line.StartsWith("Crash Report"))
        {
            LogBlock.Inlines.Add(new Run(line) { Foreground = BrushHeader, FontWeight = FontWeights.SemiBold });
            return;
        }

        if (TrySplitLabel(line, out string label, out string value) && IsMetaLabel(label))
        {
            LogBlock.Inlines.Add(new Run(label + " : ") { Foreground = BrushLabel });
            LogBlock.Inlines.Add(new Run(value) { Foreground = BrushValue });
            return;
        }

        if (line.TrimStart().StartsWith("Exception :") || line.TrimStart().StartsWith("Exception:"))
        {
            string indent = GetIndent(line);
            int colon = line.IndexOf(':');
            string rest = line[(colon + 1)..].Trim();
            LogBlock.Inlines.Add(new Run(indent + "Exception : ") { Foreground = BrushLabel });
            LogBlock.Inlines.Add(new Run(rest) { Foreground = BrushError, FontWeight = FontWeights.SemiBold });
            return;
        }

        if (line.TrimStart().StartsWith("Message :") || line.TrimStart().StartsWith("Message:"))
        {
            string indent = GetIndent(line);
            int colon = line.IndexOf(':');
            string rest = line[(colon + 1)..].Trim();
            LogBlock.Inlines.Add(new Run(indent + "Message   : ") { Foreground = BrushLabel });
            LogBlock.Inlines.Add(new Run(rest) { Foreground = BrushDefault });
            return;
        }

        if (line.TrimStart().StartsWith("StackTrace"))
        {
            string indent = GetIndent(line);
            LogBlock.Inlines.Add(new Run(indent + "StackTrace:") { Foreground = BrushLabel });
            return;
        }

        if (line.TrimStart().StartsWith("at "))
        {
            RenderStackFrame(line);
            return;
        }

        if (line.TrimStart().StartsWith("─ InnerException:") || line.TrimStart().StartsWith("- InnerException:"))
        {
            string indent = GetIndent(line);
            LogBlock.Inlines.Add(new Run(indent + "─ InnerException:")
            { Foreground = BrushInner, FontWeight = FontWeights.SemiBold });
            return;
        }

        if (line.StartsWith('[') && line.Contains(']'))
        {
            int end = line.IndexOf(']');
            string tag = line[..(end + 1)];
            string rest = line[(end + 1)..];
            LogBlock.Inlines.Add(new Run(tag) { Foreground = BrushMeta });
            LogBlock.Inlines.Add(new Run(rest) { Foreground = BrushDefault });
            return;
        }

        LogBlock.Inlines.Add(new Run(line) { Foreground = BrushDefault });
    }

    private void RenderStackFrame(string line)
    {
        string indent = GetIndent(line);
        string trimmed = line.TrimStart();

        int inIdx = trimmed.LastIndexOf(" in ", StringComparison.Ordinal);
        if (inIdx >= 0)
        {
            string method = trimmed[..inIdx];
            string filePart = trimmed[(inIdx + 4)..];

            LogBlock.Inlines.Add(new Run(indent) { Foreground = BrushStackAt });
            LogBlock.Inlines.Add(new Run(method) { Foreground = BrushStackAt });
            LogBlock.Inlines.Add(new Run(" in ") { Foreground = BrushSeparator });
            LogBlock.Inlines.Add(new Run(filePart) { Foreground = BrushStackPath });
        }
        else
        {
            LogBlock.Inlines.Add(new Run(line) { Foreground = BrushStackAt });
        }
    }

    // ── Meta bar ──────────────────────────────────────────────────────────────

    private void BuildMetaBar(string log)
    {
        var fields = new List<(string Label, string Value)>();

        foreach (string rawLine in log.Split('\n'))
        {
            string line = rawLine.Trim();
            if (TrySplitLabel(line, out string label, out string value) && IsMetaLabel(label))
            {
                fields.Add((label.Trim(), value.Trim()));
            }
        }

        if (_logPath != null && File.Exists(_logPath))
        {
            DateTime created = File.GetCreationTime(_logPath);
            fields.Insert(0, ("Time", created.ToString("yyyy-MM-dd HH:mm:ss")));
        }

        foreach ((string? label, string? value) in fields)
        {
            var pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x29, 0x3B)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = label + ": ",
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)),
                FontSize = 11,
            });
            sp.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
                FontSize = 11,
            });

            pill.Child = sp;
            MetaPanel.Children.Add(pill);
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void Copy_Click(object? sender, RoutedEventArgs? e)
    {
        if (!string.IsNullOrEmpty(_rawLog))
        {
            Clipboard.SetText(_rawLog);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_logPath != null && File.Exists(_logPath))
        {
            Process.Start("explorer.exe", $"/select,\"{_logPath}\"");
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_logPath == null || !File.Exists(_logPath))
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            "Delete this log file?\n" + _logPath,
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            File.Delete(_logPath);
            LogBlock.Inlines.Clear();
            LogBlock.Inlines.Add(new Run("Log file deleted.") { Foreground = BrushValue });
            LogPathText.Text = "(deleted)";
            DeleteBtn.IsEnabled = false;
            OpenFolderBtn.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Report_Click(object sender, RoutedEventArgs e)
    {
        var popup = new ReportPopup();
        popup.Owner = this;

        popup.ShowDialog();

        switch (popup.SelectedAction)
        {
            case ReportAction.Facebook:
            case ReportAction.GitHub:
                Copy_Click(null, null);
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TrySplitLabel(string line, out string label, out string value)
    {
        int colon = line.IndexOf(':');
        if (colon <= 0 || colon >= line.Length - 1)
        {
            label = value = string.Empty;
            return false;
        }

        label = line[..colon];
        value = line[(colon + 1)..].Trim();
        return true;
    }

    private static bool IsMetaLabel(string label)
    {
        string t = label.Trim();
        return t is "Source" or "OS" or "Runtime" or "Version"
                  or "Time" or "App" or "Machine" or "User";
    }

    private static string GetIndent(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        return line[..i];
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[0..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}