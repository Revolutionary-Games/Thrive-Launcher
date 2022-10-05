namespace ThriveLauncher.ViewModels;

using LauncherBackend.Models;
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

    public DevCenterConnection? DevCenterConnection => devCenterClient.DevCenterConnection;

    public void OpenDevCenterConnectionMenu()
    {
        ShowDevCenterPopup = true;
    }

    public void CloseDevCenterMenuClicked()
    {
        ShowDevCenterPopup = false;
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
    }
}
