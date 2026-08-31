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
using System.Windows;

namespace PDownloader.BugTracker;

public partial class ReportPopup : Window
{
    public enum ReportAction
    {
        None,
        Facebook,
        GitHub
    }

    public ReportAction SelectedAction { get; private set; }
        = ReportAction.None;

    private bool _closed
    {
        get
        {
            return SelectedAction != ReportAction.None;
        }
    }

    public ReportPopup()
    {
        InitializeComponent();
        Deactivated += (_, _) =>
        {
            if (!_closed)
            {
                Close();
            }
        };
    }

    private void Facebook_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ReportAction.Facebook;

        Process.Start(new ProcessStartInfo
        {
            FileName = "https://www.facebook.com/MaiXuan.HuynhOR/",
            UseShellExecute = true
        });

        Close();
    }

    private void GitHub_Click(object sender, RoutedEventArgs e)
    {
        SelectedAction = ReportAction.GitHub;

        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/maihcx/PDownloader/issues/new/choose",
            UseShellExecute = true
        });

        Close();
    }
}