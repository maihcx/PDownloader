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

using CommunityToolkit.Mvvm.Messaging.Messages;

namespace PDownloader.Services;

public interface IDialogWithResult<TResult>
{
    TResult? Result { get; }
}

public interface IDialogWithModel
{
    void SetModel(object? model);
}

public class GenericMessage<T> : ValueChangedMessage<T>
{
    public GenericMessage(T value) : base(value) { }
}

public static class MessengerService
{
    private static readonly ISnackbarService GlobalSnackbar = App.GetRequiredService<ISnackbarService>();

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance)
    {
        ShowSnackbar(title, content, controlAppearance, null, default);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, TimeSpan timeSpan = default)
    {
        ShowSnackbar(title, content, controlAppearance, null, timeSpan);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null)
    {
        ShowSnackbar(title, content, controlAppearance, icon, default);
    }

    public static async void ShowSnackbar(string title, string content, ControlAppearance controlAppearance, IconElement? icon = null, TimeSpan timeSpan = default)
    {
        GlobalSnackbar.Show(LanguageBase.GetLangValue(title), LanguageBase.GetLangValue(content), controlAppearance, icon, timeSpan);
    }

    public static async Task<TResult?> ShowDialogAsync<TDialog, TResult>(object? model = null, ContentDialogHost? dialogHost = null, Func<TDialog, Task>? onShowing = null) where TDialog : ContentDialog, IDialogWithResult<TResult>
    {
        IContentDialogService service = App.GetRequiredService<IContentDialogService>();

        dialogHost ??= service.GetDialogHostEx();

        if (Activator.CreateInstance(typeof(TDialog), dialogHost) is not TDialog dialog)
        {
            throw new InvalidOperationException($"Cannot create instance of type {typeof(TDialog).FullName}.");
        }

        if (dialog is IDialogWithModel modelDialog)
        {
            if (onShowing != null)
            {
                await onShowing(dialog);
            }
            modelDialog.SetModel(model);
        }
        else if (model != null)
        {
            if (onShowing != null)
            {
                await onShowing(dialog);
            }
            dialog.DataContext = model;
        }

        await dialog.ShowAsync();
        return dialog.Result;
    }
}
