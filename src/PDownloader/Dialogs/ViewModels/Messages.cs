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

namespace PDownloader.Dialogs.ViewModels;

public partial class Messages : ObservableObject
{
    [ObservableProperty]
    private Models.Messages _model = new();

    [ObservableProperty]
    private ControlAppearance _closeButtonAppearance = ControlAppearance.Transparent;

    [ObservableProperty]
    private string _closeButtonText = string.Empty;

    [ObservableProperty]
    private bool _primaryButtonEnabled = false;

    [ObservableProperty]
    private ControlAppearance _primaryButtonAppearance = ControlAppearance.Transparent;

    [ObservableProperty]
    private string _primaryButtonText = string.Empty;

    [ObservableProperty]
    private bool _secondaryButtonEnabled = false;

    [ObservableProperty]
    private ControlAppearance _secondaryButtonAppearance = ControlAppearance.Transparent;

    [ObservableProperty]
    private string _secondaryButtonText = string.Empty;

    [ObservableProperty]
    private bool _imageVisible = false;

    [ObservableProperty]
    private SymbolRegular _imageView;

    public Messages()
    {

    }

    public Models.Messages.MessageResult MessageResult { get; set; }

    private void HandleButtonType()
    {
        CloseButtonAppearance = ControlAppearance.Secondary;

        PrimaryButtonEnabled = false;
        PrimaryButtonAppearance = ControlAppearance.Secondary;

        SecondaryButtonEnabled = false;
        SecondaryButtonAppearance = ControlAppearance.Secondary;

        switch (Model.MessageButtonType)
        {
            case Models.Messages.MessageButton.OK:
                CloseButtonAppearance = ControlAppearance.Primary;
                CloseButtonText = LanguageBase.GetLangValue("dialog_button_ok_title");
                break;

            case Models.Messages.MessageButton.OKCancel:
                PrimaryButtonEnabled = true;
                PrimaryButtonAppearance = ControlAppearance.Primary;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_ok_title");

                CloseButtonText = LanguageBase.GetLangValue("dialog_button_cancel_title");
                break;

            case Models.Messages.MessageButton.AbortRetryIgnore:
                PrimaryButtonEnabled = true;
                PrimaryButtonAppearance = ControlAppearance.Danger;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_abort_title");

                SecondaryButtonEnabled = true;
                SecondaryButtonAppearance = ControlAppearance.Primary;
                SecondaryButtonText = LanguageBase.GetLangValue("dialog_button_retry_title");

                CloseButtonAppearance = ControlAppearance.Caution;
                CloseButtonText = LanguageBase.GetLangValue("dialog_button_Ignore_title");
                break;

            case Models.Messages.MessageButton.YesNoCancel:
                PrimaryButtonEnabled = true;
                PrimaryButtonAppearance = ControlAppearance.Primary;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_yes_title");

                SecondaryButtonEnabled = true;
                SecondaryButtonText = LanguageBase.GetLangValue("dialog_button_no_title");

                CloseButtonText = LanguageBase.GetLangValue("dialog_button_cancel_title");
                break;

            case Models.Messages.MessageButton.YesNo:
                PrimaryButtonEnabled = true;
                PrimaryButtonAppearance = ControlAppearance.Primary;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_yes_title");

                CloseButtonText = LanguageBase.GetLangValue("dialog_button_no_title");
                break;

            case Models.Messages.MessageButton.RetryCancel:
                PrimaryButtonEnabled = true;
                PrimaryButtonAppearance = ControlAppearance.Primary;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_retry_title");

                CloseButtonText = LanguageBase.GetLangValue("dialog_button_cancel_title");
                break;

            case Models.Messages.MessageButton.CancelTryContinue:
                PrimaryButtonEnabled = true;
                PrimaryButtonText = LanguageBase.GetLangValue("dialog_button_cancel_title");

                SecondaryButtonEnabled = true;
                SecondaryButtonAppearance = ControlAppearance.Primary;
                SecondaryButtonText = LanguageBase.GetLangValue("dialog_button_try_title");

                CloseButtonAppearance = ControlAppearance.Info;
                CloseButtonText = LanguageBase.GetLangValue("dialog_button_continue_title");
                break;
        }
    }

    private void HandleImage()
    {
        ImageVisible = true;
        switch (Model.MessageImageType)
        {
            case Models.Messages.MessageImage.None:
                ImageVisible = false;
                break;

            case Models.Messages.MessageImage.Error:
                ImageView = SymbolRegular.ErrorCircle24;
                break;

            case Models.Messages.MessageImage.Hand:
                ImageView = SymbolRegular.HandLeft24;
                break;

            case Models.Messages.MessageImage.Stop:
                ImageView = SymbolRegular.Stop24;
                break;

            case Models.Messages.MessageImage.Question:
                ImageView = SymbolRegular.Question24;
                break;

            case Models.Messages.MessageImage.Exclamation:
                ImageView = SymbolRegular.BookExclamationMark24;
                break;

            case Models.Messages.MessageImage.Warning:
                ImageView = SymbolRegular.Warning24;
                break;

            case Models.Messages.MessageImage.Asterisk:
                ImageView = SymbolRegular.TextAsterisk20;
                break;

            case Models.Messages.MessageImage.Information:
                ImageView = SymbolRegular.Info24;
                break;
        }
    }

    public void SetModel(Models.Messages model)
    {
        Model = model;

        Model.PropertyChanged += Model_PropertyChanged;

        HandleButtonType();

        HandleImage();
    }

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Models.Messages.MessageButtonType))
        {
            HandleButtonType();
        }
        else if (e.PropertyName == nameof(Models.Messages.MessageImageType))
        {
            HandleImage();
        }
    }

    public void HandleButton(ContentDialogButton button)
    {
        switch (Model.MessageButtonType)
        {
            case Models.Messages.MessageButton.OK:
                if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.OK;
                }

                break;

            case Models.Messages.MessageButton.OKCancel:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.OK;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.Cancel;
                }

                break;

            case Models.Messages.MessageButton.AbortRetryIgnore:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.Abort;
                }
                else if (button == ContentDialogButton.Secondary)
                {
                    MessageResult = Models.Messages.MessageResult.Retry;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.Ignore;
                }

                break;

            case Models.Messages.MessageButton.YesNoCancel:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.Yes;
                }
                else if (button == ContentDialogButton.Secondary)
                {
                    MessageResult = Models.Messages.MessageResult.No;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.Cancel;
                }

                break;

            case Models.Messages.MessageButton.YesNo:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.Yes;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.No;
                }

                break;

            case Models.Messages.MessageButton.RetryCancel:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.Retry;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.Cancel;
                }

                break;

            case Models.Messages.MessageButton.CancelTryContinue:
                if (button == ContentDialogButton.Primary)
                {
                    MessageResult = Models.Messages.MessageResult.Cancel;
                }
                else if (button == ContentDialogButton.Secondary)
                {
                    MessageResult = Models.Messages.MessageResult.TryAgain;
                }
                else if (button == ContentDialogButton.Close)
                {
                    MessageResult = Models.Messages.MessageResult.Continue;
                }

                break;
        }
    }
}
