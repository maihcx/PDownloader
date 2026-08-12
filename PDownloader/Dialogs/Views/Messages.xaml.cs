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
/// Interaction logic for Messages.xaml
/// </summary>
public partial class Messages : ContentDialog, IDialogWithResult<ViewModels.Messages>, IDialogWithModel<Models.Messages>
{
    public ViewModels.Messages ViewModel { get; }

    public ViewModels.Messages? Result { get; private set; }

    public Messages(ContentDialogHost? contentPresenter) : base(contentPresenter)
    {
        ViewModel = new ViewModels.Messages();
        DataContext = this;

        InitializeComponent();
    }

    protected override async void OnButtonClick(ContentDialogButton button)
    {
        ViewModel.HandleButton(button);
        Result = ViewModel;

        base.OnButtonClick(button);
    }

    public void SetModel(Models.Messages? model)
    {
        if (model != null)
        {
            ViewModel.SetModel(model);
        }
    }
}
