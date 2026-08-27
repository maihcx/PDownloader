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

namespace PDownloader.Dialogs.Views;

/// <summary>
/// Interaction logic for AddLink.xaml
/// </summary>
public partial class AddLink : ContentDialog, IDialogWithResult<ViewModels.AddLink>
{
    public ViewModels.AddLink ViewModel { get; }

    public ViewModels.AddLink? Result { get; private set; }

    public AddLink(ContentDialogHost? contentPresenter) : base(contentPresenter)
    {
        ViewModel = new ViewModels.AddLink();
        DataContext = this;

        InitializeComponent();

        (new Task(() =>
        {
            Thread.Sleep(200);
            Dispatcher.Invoke(() =>
            {
                txtLink.Focus();
            });
        })).Start();
    }

    protected override async void OnButtonClick(ContentDialogButton button)
    {
        if (button == ContentDialogButton.Primary)
        {
            tblErrorResp.Visibility = Visibility.Collapsed;

            Control? _firstInvalidControl = null;
            bool isValid = true;

            foreach (Wpf.Ui.Controls.TextBox child in FindVisualChildren<Wpf.Ui.Controls.TextBox>(this))
            {
                BindingExpression? binding = child.GetBindingExpression(Wpf.Ui.Controls.TextBox.TextProperty);
                binding?.UpdateSource();

                if (binding?.ResolvedSourcePropertyName is string prop)
                {
                    var error = ((IDataErrorInfo)ViewModel)[prop];
                    if (!string.IsNullOrEmpty(error))
                    {
                        _firstInvalidControl ??= child;
                        isValid = false;
                    }
                }
            }

            if (!isValid && _firstInvalidControl != null)
            {
                _firstInvalidControl.Focus();
                return;
            }

            Result = ViewModel;
        }

        base.OnButtonClick(button);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        if (depObj == null)
        {
            yield break;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
            if (child is T t)
            {
                yield return t;
            }

            foreach (T childOfChild in FindVisualChildren<T>(child))
            {
                yield return childOfChild;
            }
        }
    }
}
