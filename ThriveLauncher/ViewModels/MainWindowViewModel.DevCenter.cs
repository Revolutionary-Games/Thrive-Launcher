namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using DevCenterCommunication.Models;
using LauncherBackend.Models;
using LauncherBackend.Services;
using LauncherBackend.Utilities;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   DevCenter connection (and DevBuild) related functions of the window
/// </summary>
public partial class MainWindowViewModel
{
    // DevCenter features
    private bool showDevCenterStatusArea = true;
    private bool showDevCenterPopup;

    private int nextDevCenterOpenOverrideKeyIndex;

    // Initial startup / later checking we have DevCenter connection variables
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

    private bool refreshingLatestDevBuilds;
    private List<DevBuildLauncherDTO> latestAvailableDevBuilds = new();

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

    public bool RefreshingLatestDevBuilds
    {
        get => refreshingLatestDevBuilds;
        private set => this.RaiseAndSetIfChanged(ref refreshingLatestDevBuilds, value);
    }

    public List<DevBuildLauncherDTO> LatestAvailableDevBuilds
    {
        get => latestAvailableDevBuilds;
        private set => this.RaiseAndSetIfChanged(ref latestAvailableDevBuilds, value);
    }

    public DevCenterConnection? DevCenterConnection => devCenterClient.DevCenterConnection;

    public ReactiveCommand<int, Unit> DevCenterKeyCommand { get; }

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
        // ReSharper disable MethodSupportsCancellation
        backgroundExceptionNoticeDisplayer.HandleTask(Task.Run(() =>
        {
            Task.Delay(TimeSpan.FromMilliseconds(300)).Wait();

            Dispatcher.UIThread.Post(CheckDevCenterConnection);
        }));

        // ReSharper restore MethodSupportsCancellation
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
            logger.LogWarning("Already forming devcenter connection with code, ignoring another attempt");
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

            // Make the primary thing to play selector also update
            UpdateSelectedVersionForDevBuild();
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

    public void SelectManualBuild(string buildHash)
    {
        ManuallySelectedBuildHash = buildHash;
        SelectedDevBuildTypeIsManuallySelected = true;
        ManuallyEnteredHashInput = string.Empty;

        // Ensure DevBuild is selected to play after pressing a "select" button even if it was already selected
        UpdateSelectedVersionForDevBuild();
    }

    public void RefreshLatestDevBuildsList()
    {
        if (RefreshingLatestDevBuilds)
        {
            logger.LogWarning("Already refreshing latest DevBuilds list, ignoring another attempt");
            return;
        }

        RefreshingLatestDevBuilds = true;

        PerformDevBuildListRefresh();
    }

    public void VisitBuildPage(long buildId)
    {
        URLUtilities.OpenURLInBrowser(LauncherConstants.DevCenterBuildInfoPagePrefix.ToString() + buildId);
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

        // Make a version be selected (but only if version info is already loaded
        if (ThriveVersionInformation != null)
        {
            NotifyChangesToAvailableVersions();
        }
        else
        {
            this.RaisePropertyChanged(nameof(AvailableThriveVersions));
        }
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

    private async void PerformDevBuildListRefresh()
    {
        // TODO: offset support
        var result = await devCenterClient.FetchLatestBuilds(0);

        Dispatcher.UIThread.Post(() =>
        {
            RefreshingLatestDevBuilds = false;

            if (result is not { Count: >= 1 })
            {
                logger.LogInformation("Didn't get latest builds");
                LatestAvailableDevBuilds = new List<DevBuildLauncherDTO>();
            }
            else
            {
                LatestAvailableDevBuilds = result;
            }
        });
    }
}
