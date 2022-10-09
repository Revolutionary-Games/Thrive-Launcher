namespace ThriveLauncher.ViewModels;

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   DevCenter connection (and DevBuild) related functions of the window
/// </summary>
public partial class MainWindowViewModel
{
    // Devcenter features
    private bool showDevCenterStatusArea = true;
    private bool showDevCenterPopup;

    private int nextDevCenterOpenOverrideKeyIndex;

    // Initial startup / later checking we have devcenter connection variables
    private bool checkingDevCenterConnection;
    private bool devCenterKeyClearQueued;

    private bool hasDevCenterError;
    private bool unknownDevCenterError;
    private bool canRetryDevCenterConnection;

    // Setting up a connection view variables
    private bool checkingConnectionCode;
    private string devCenterConnectAttemptError = string.Empty;

    private string? devCenterConnectCode;

    private bool canFormDevCenterConnection;
    private string toBeFormedLinkUserName = string.Empty;
    private string toBeFormedLinkEmail = string.Empty;

    private bool formingDevCenterConnection;

    // Logged in view variables
    private string manuallyEnteredHashInput = string.Empty;

    private bool loggingOutDevCenterConnection;

    // Connection and other properties
    public bool HasDevCenterConnection => DevCenterConnection != null;

    public string DevCenterConnectedUser => DevCenterConnection?.Username ?? "error";

    public string DevCenterConnectedUserAndEmail => $"{DevCenterConnection?.Email} ({DevCenterConnectedUser})";

    public bool DevCenterConnectionIsDeveloper => DevCenterConnection?.IsDeveloper ?? false;

    public bool ShowDevCenterPopup
    {
        get => showDevCenterPopup;
        private set
        {
            if (showDevCenterPopup == value)
                return;

            this.RaiseAndSetIfChanged(ref showDevCenterPopup, value);

            if (!showDevCenterPopup)
            {
                // For now we save if we are logged in as we have multiple variables that need to be saved.
                // Maybe in the future a better design could be used to only save when necessary
                if (HasDevCenterConnection)
                {
                    TriggerSaveSettings();

                    // TODO: update selected build hash and type in settings
                }
            }
            else
            {
                // Reset connection setting up variables in case the user closed the popup while in the middle of that
                CheckingConnectionCode = false;
                DevCenterConnectAttemptError = string.Empty;
                DevCenterConnectCode = null;
                CanFormDevCenterConnection = false;
            }
        }
    }

    public bool ShowDevCenterStatusArea
    {
        get => showDevCenterStatusArea;
        private set => this.RaiseAndSetIfChanged(ref showDevCenterStatusArea, value);
    }

    public bool CheckingDevCenterConnection
    {
        get => checkingDevCenterConnection;
        private set => this.RaiseAndSetIfChanged(ref checkingDevCenterConnection, value);
    }

    public bool HasDevCenterError
    {
        get => hasDevCenterError;
        private set => this.RaiseAndSetIfChanged(ref hasDevCenterError, value);
    }

    public bool UnknownDevCenterError
    {
        get => unknownDevCenterError;
        private set => this.RaiseAndSetIfChanged(ref unknownDevCenterError, value);
    }

    public bool CanRetryDevCenterConnection
    {
        get => canRetryDevCenterConnection;
        private set => this.RaiseAndSetIfChanged(ref canRetryDevCenterConnection, value);
    }

    public bool CheckingConnectionCode
    {
        get => checkingConnectionCode;
        private set => this.RaiseAndSetIfChanged(ref checkingConnectionCode, value);
    }

    public string DevCenterConnectAttemptError
    {
        get => devCenterConnectAttemptError;
        private set => this.RaiseAndSetIfChanged(ref devCenterConnectAttemptError, value);
    }

    public string? DevCenterConnectCode
    {
        get => devCenterConnectCode;
        set
        {
            // A bit of usability in case the user accidentally adds blanks at the start or end
            value = value?.Trim();
            this.RaiseAndSetIfChanged(ref devCenterConnectCode, value);
        }
    }

    public bool CanFormDevCenterConnection
    {
        get => canFormDevCenterConnection;
        private set => this.RaiseAndSetIfChanged(ref canFormDevCenterConnection, value);
    }

    public string ToBeFormedLinkUserName
    {
        get => toBeFormedLinkUserName;
        private set => this.RaiseAndSetIfChanged(ref toBeFormedLinkUserName, value);
    }

    public string ToBeFormedLinkEmail
    {
        get => toBeFormedLinkEmail;
        private set => this.RaiseAndSetIfChanged(ref toBeFormedLinkEmail, value);
    }

    public bool FormingDevCenterConnection
    {
        get => formingDevCenterConnection;
        private set => this.RaiseAndSetIfChanged(ref formingDevCenterConnection, value);
    }

    public bool LoggingOutDevCenterConnection
    {
        get => loggingOutDevCenterConnection;
        private set => this.RaiseAndSetIfChanged(ref loggingOutDevCenterConnection, value);
    }

    public string ManuallyEnteredHashInput
    {
        get => manuallyEnteredHashInput;
        set => this.RaiseAndSetIfChanged(ref manuallyEnteredHashInput, value);
    }

    public DevCenterConnection? DevCenterConnection => devCenterClient.DevCenterConnection;

    public void OpenDevCenterConnectionMenu()
    {
        ShowDevCenterPopup = true;
    }

    public void CloseDevCenterMenuClicked()
    {
        ShowDevCenterPopup = false;
    }

    public void CheckDevCenterConnection()
    {
        if (CheckingDevCenterConnection)
        {
            logger.LogWarning("Already checking DevCenter connection, ignoring another attempt to start checking");
            return;
        }

        CheckingDevCenterConnection = true;
        HasDevCenterError = false;

        PerformDevCenterCheck();
    }

    public void RetryDevCenterConnectionCheck()
    {
        // In case this is open, hide it
        ShowDevCenterPopup = false;

        HasDevCenterError = false;

        // Give a bit of a delay here to play at least part of the popup dismiss animation
        Task.Run(() =>
        {
            Task.Delay(TimeSpan.FromMilliseconds(300)).Wait();

            Dispatcher.UIThread.Post(CheckDevCenterConnection);
        });
    }

    public void DismissDevCenterError()
    {
        HasDevCenterError = false;

        if (devCenterKeyClearQueued)
        {
            devCenterKeyClearQueued = false;

            logger.LogInformation("Resetting our DevCenter key due to queued dismiss");
            devCenterClient.ClearTokenInSettings();
        }
    }

    public void CheckConnectionCode()
    {
        if (string.IsNullOrWhiteSpace(DevCenterConnectCode))
        {
            DevCenterConnectAttemptError = Resources.EnterDevCenterConnectionCode;
            return;
        }

        if (CheckingConnectionCode)
        {
            logger.LogWarning("Already checking DevCenter code, ignoring another attempt");
            return;
        }

        CheckingConnectionCode = true;
        DevCenterConnectAttemptError = string.Empty;

        PerformDevCenterLinkCodeCheck(DevCenterConnectCode);
    }

    public void ConfirmConnectionForming()
    {
        if (string.IsNullOrWhiteSpace(DevCenterConnectCode))
        {
            DevCenterConnectAttemptError = Resources.EnterDevCenterConnectionCode;
            CanFormDevCenterConnection = false;
            return;
        }

        if (FormingDevCenterConnection)
        {
            logger.LogWarning("Already checking DevCenter code, ignoring another attempt");
            return;
        }

        FormingDevCenterConnection = true;
        DevCenterConnectAttemptError = string.Empty;

        PerformDevCenterLinking(DevCenterConnectCode);
    }

    public void CancelConnectionForming()
    {
        CanFormDevCenterConnection = false;
    }

    public void LogoutFromDevCenter()
    {
        if (LoggingOutDevCenterConnection)
        {
            logger.LogWarning("Already logging out the DevCenter connection, ignoring another attempt");
            return;
        }

        LoggingOutDevCenterConnection = true;

        PerformDevCenterLogout();
    }

    public void SelectBuildOfTheDay()
    {
        SelectedDevBuildTypeIsBuildOfTheDay = true;
    }

    public void SelectLatestBuild()
    {
        SelectedDevBuildTypeIsLatest = true;
    }

    public void SelectManuallySelectedBuild()
    {
        SelectedDevBuildTypeIsManuallySelected = true;
    }

    public void EnterManuallyEnteredHash()
    {
        if (!string.IsNullOrWhiteSpace(ManuallyEnteredHashInput))
        {
            // Trim here to allow the user some copy-pasting error
            var hash = ManuallyEnteredHashInput.Trim();

            ManuallySelectedBuildHash = hash;

            // Automatically select this to avoid user error where the user doesn't remember to select this
            SelectedDevBuildTypeIsManuallySelected = true;
        }
        else
        {
            // Invalid input, clear the hash
            ManuallySelectedBuildHash = null;

            SelectedDevBuildTypeIsBuildOfTheDay = true;
        }

        // Clear the entry
        ManuallyEnteredHashInput = string.Empty;
    }

    /// <summary>
    ///   "secret" key sequence to activate the DevCenter dialog
    /// </summary>
    /// <param name="keyIndex">Index number of the key in the sequence that was pressed</param>
    public void DevCenterViewActivation(int keyIndex)
    {
        if (nextDevCenterOpenOverrideKeyIndex == keyIndex)
        {
            ++nextDevCenterOpenOverrideKeyIndex;

            if (nextDevCenterOpenOverrideKeyIndex >= TotalKeysInDevCenterActivationSequence)
            {
                OpenDevCenterConnectionMenu();
                nextDevCenterOpenOverrideKeyIndex = 0;
            }
        }
        else
        {
            // User failed to type the sequence
            nextDevCenterOpenOverrideKeyIndex = 0;
        }
    }

    private void OnDevCenterConnectionStatusChanged()
    {
        this.RaisePropertyChanged(nameof(DevCenterConnection));
        this.RaisePropertyChanged(nameof(HasDevCenterConnection));
        this.RaisePropertyChanged(nameof(DevCenterConnectedUser));
        this.RaisePropertyChanged(nameof(DevCenterConnectedUserAndEmail));
        this.RaisePropertyChanged(nameof(DevCenterConnectionIsDeveloper));
        this.RaisePropertyChanged(nameof(AvailableThriveVersions));

        // Make a version be selected (but only if version info is already loaded
        if (ThriveVersionInformation != null)
            OnVersionInfoLoaded();
    }

    private async void PerformDevCenterCheck()
    {
        var result = await devCenterClient.CheckDevCenterConnection();

        Dispatcher.UIThread.Post(() =>
        {
            CheckingDevCenterConnection = false;

            if (result == DevCenterResult.Success)
            {
                logger.LogInformation("We are now connected to the DevCenter");
                HasDevCenterError = false;
            }
            else
            {
                logger.LogInformation("DevCenter connection failed");
                HasDevCenterError = true;

                switch (result)
                {
                    case DevCenterResult.ConnectionFailure:
                        CanRetryDevCenterConnection = true;
                        break;
                    case DevCenterResult.InvalidKey:
                        devCenterKeyClearQueued = true;
                        CanRetryDevCenterConnection = false;
                        break;
                    case DevCenterResult.DataError:
                    default:
                        UnknownDevCenterError = true;
                        CanRetryDevCenterConnection = true;
                        break;
                }
            }

            OnDevCenterConnectionStatusChanged();
        });
    }

    private async void PerformDevCenterLinkCodeCheck(string code)
    {
        var (result, error) = await devCenterClient.CheckLinkCode(code);

        Dispatcher.UIThread.Post(() =>
        {
            CheckingConnectionCode = false;

            if (result == null)
            {
                error ??= "Unknown error";
                CanFormDevCenterConnection = false;
                DevCenterConnectAttemptError = string.Format(Resources.DevCenterConnectAttemptError, error);
            }
            else
            {
                logger.LogInformation("We can form a connection to the DevCenter next");
                CanFormDevCenterConnection = true;

                ToBeFormedLinkUserName = result.Username;
                ToBeFormedLinkEmail = result.Email;
            }
        });
    }

    private async void PerformDevCenterLinking(string code)
    {
        var result = await devCenterClient.FormConnection(code);

        Dispatcher.UIThread.Post(() =>
        {
            FormingDevCenterConnection = false;
            CanFormDevCenterConnection = false;

            if (result != DevCenterResult.Success)
            {
                logger.LogInformation("Failed to link to the DevCenter");
                DevCenterConnectAttemptError = string.Format(Resources.DevCenterConnectAttemptError,
                    Resources.LinkFormingFailedError);
            }
            else
            {
                logger.LogInformation("A new connection has been formed, refreshing our DevCenter status");
                CheckDevCenterConnection();
            }
        });
    }

    private async void PerformDevCenterLogout()
    {
        await devCenterClient.Logout();

        Dispatcher.UIThread.Post(() =>
        {
            LoggingOutDevCenterConnection = false;

            logger.LogInformation("We have logged out (or at least attempted), refreshing our DevCenter status");
            CheckDevCenterConnection();
            ShowDevCenterPopup = false;
        });
    }
}
