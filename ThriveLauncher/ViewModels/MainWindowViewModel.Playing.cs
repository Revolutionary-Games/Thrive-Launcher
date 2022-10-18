namespace ThriveLauncher.ViewModels;

using System.Linq;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;

/// <summary>
///   This partial class handles everything related to the setting up and playing Thrive (and the related popup)
/// </summary>
public partial class MainWindowViewModel
{
    private bool currentlyPlaying;

    public bool CanPressPlayButton => !CurrentlyPlaying && !string.IsNullOrEmpty(SelectedVersionToPlay);

    public bool CurrentlyPlaying
    {
        get => currentlyPlaying;
        private set
        {
            if (currentlyPlaying == value)
                return;

            this.RaiseAndSetIfChanged(ref currentlyPlaying, value);
            this.RaisePropertyChanged(nameof(CanPressPlayButton));
        }
    }

    public void TryToPlayThrive()
    {
        if (CurrentlyPlaying)
        {
            // Disallow starting again
            ShowNotice(Resources.CannotPlayThriveTitle, Resources.ThriveCurrentlyRunning);
            return;
        }

        var versionToPlay = SelectedVersionToPlay;

        var version = AvailableThriveVersions.Where(t => t.VersionObject.VersionName == versionToPlay)
            .Select(t => t.VersionObject)
            .FirstOrDefault();

        if (version == null)
        {
            logger.LogInformation("No version to play found, selected: {VersionToPlay}", versionToPlay);
            ShowNotice(Resources.CannotPlayThriveTitle, Resources.NoVersionSelected);
            return;
        }

        logger.LogInformation("Starting playing Thrive {VersionName}", version.VersionName);
        CurrentlyPlaying = true;

        // TODO:
    }
}
