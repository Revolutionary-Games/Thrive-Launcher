namespace ThriveLauncher.ViewModels;

using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
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

    private bool checkingDevCenterConnection;
    private bool devCenterKeyClearQueued;

    private bool hasDevCenterError;
    private bool unknownDevCenterError;
    private bool canRetryDevCenterConnection;

    public bool HasDevCenterConnection => DevCenterConnection != null;

    public string DevCenterConnectedUser => DevCenterConnection?.Username ?? "error";

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
                // TODO: save settings only if devbuild type or latest build was changed
                // For now we save if we are logged in
                if (HasDevCenterConnection)
                    TriggerSaveSettings();
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
}
