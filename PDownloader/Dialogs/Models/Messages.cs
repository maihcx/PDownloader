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

namespace PDownloader.Dialogs.Models;

public partial class Messages : ObservableObject
{
    public enum MessageButton
    {
        OK,
        OKCancel,
        AbortRetryIgnore,
        YesNoCancel,
        YesNo,
        RetryCancel,
        CancelTryContinue
    }

    public enum MessageImage
    {
        None,
        Error,
        Hand,
        Stop,
        Question,
        Exclamation,
        Warning,
        Asterisk,
        Information
    }

    public enum MessageOption
    {
        None,
        DefaultDesktopOnly,
        RightAlign,
        RtlReading,
        ServiceNotification,
    }

    public enum MessageResult
    {
        None,
        OK,
        Cancel,
        Abort,
        Retry,
        Ignore,
        Yes,
        No,
        TryAgain,
        Continue
    }

    public Messages()
    {
        OnMessageTitleKeyChanged(MessageTitleKey);
        OnMessageContentKeyChanged(MessageContentKey);
    }

    [ObservableProperty]
    private MessageButton _messageButtonType = MessageButton.OK;

    [ObservableProperty]
    private MessageImage _messageImageType = MessageImage.None;

    [ObservableProperty]
    private MessageOption _messageOptionType = MessageOption.None;

    [ObservableProperty]
    private string _messageTitleKey = string.Empty;

    partial void OnMessageTitleKeyChanged(string value)
    {
        MessageTitle = LanguageBase.GetLangValue(value);
    }

    [ObservableProperty]
    private string _messageTitle = string.Empty;

    [ObservableProperty]
    private string _messageContentKey = string.Empty;

    partial void OnMessageContentKeyChanged(string value)
    {
        MessageContent = LanguageBase.GetLangValue(value);
    }

    [ObservableProperty]
    private string _messageContent = string.Empty;
}
