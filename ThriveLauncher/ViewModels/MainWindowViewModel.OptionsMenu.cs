namespace ThriveLauncher.ViewModels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LauncherBackend.Models;
using Microsoft.Extensions.Logging;
using Properties;
using ReactiveUI;
using SharedBase.Utilities;

/// <summary>
///   Properties and methods for the options menu portion of the window. Settings properties are in the
///   MainWindowViewModel.Settings.cs file.
/// </summary>
public partial class MainWindowViewModel
{
    private bool showSettingsPopup;

    private Task<string>? dehydrateCacheSizeTask;

    private IEnumerable<FolderInInstallFolder>? installedFolders;
    private IEnumerable<string>? temporaryFolderFiles;

    private bool showClearTemporaryPrompt;
    private string clearTemporaryPromptContent = string.Empty;
    private string clearTemporaryPromptCountContent = string.Empty;

    private bool showClearDehydratedPrompt;
    private string clearDehydratedPromptContent = string.Empty;
    private string clearDehydratedPromptCountContent = string.Empty;

    // Settings related file moving
    private bool hasPendingFileMoveOffer;
    private string fileMoveOfferTitle = string.Empty;
    private string fileMoveOfferContent = string.Empty;
    private string fileMoveOfferError = string.Empty;
    private bool hasFileMoveProgress;
    private float fileMoveProgress;
    private bool canAnswerFileMovePrompt;

    // Internal move variables, not exposed to the GUI
    private List<string>? fileMoveOfferFiles;
    private string? fileMoveOfferTarget;
    private Action? fileMoveFinishCallback;

    public bool ShowSettingsPopup
    {
        get => showSettingsPopup;
        private set
        {
            if (showSettingsPopup == value)
                return;

            this.RaiseAndSetIfChanged(ref showSettingsPopup, value);

            if (!showSettingsPopup)
            {
                TriggerSaveSettings();
            }
            else
            {
                StartSettingsViewTasks();
            }
        }
    }

    public IEnumerable<FolderInInstallFolder>? InstalledFolders
    {
        get => installedFolders;
        private set => this.RaiseAndSetIfChanged(ref installedFolders, value);
    }

    public IEnumerable<string>? TemporaryFolderFiles
    {
        get => temporaryFolderFiles;
        private set => this.RaiseAndSetIfChanged(ref temporaryFolderFiles, value);
    }

    public bool ShowClearTemporaryPrompt
    {
        get => showClearTemporaryPrompt;
        set => this.RaiseAndSetIfChanged(ref showClearTemporaryPrompt, value);
    }

    public string ClearTemporaryPromptContent
    {
        get => clearTemporaryPromptContent;
        private set => this.RaiseAndSetIfChanged(ref clearTemporaryPromptContent, value);
    }

    public string ClearTemporaryPromptCountContent
    {
        get => clearTemporaryPromptCountContent;
        private set => this.RaiseAndSetIfChanged(ref clearTemporaryPromptCountContent, value);
    }

    public bool ShowClearDehydratedPrompt
    {
        get => showClearDehydratedPrompt;
        set => this.RaiseAndSetIfChanged(ref showClearDehydratedPrompt, value);
    }

    public string ClearDehydratedPromptContent
    {
        get => clearDehydratedPromptContent;
        private set => this.RaiseAndSetIfChanged(ref clearDehydratedPromptContent, value);
    }

    public string ClearDehydratedPromptCountContent
    {
        get => clearDehydratedPromptCountContent;
        private set => this.RaiseAndSetIfChanged(ref clearDehydratedPromptCountContent, value);
    }

    public bool HasPendingFileMoveOffer
    {
        get => hasPendingFileMoveOffer;
        private set => this.RaiseAndSetIfChanged(ref hasPendingFileMoveOffer, value);
    }

    public bool CanAnswerFileMovePrompt
    {
        get => canAnswerFileMovePrompt;
        private set => this.RaiseAndSetIfChanged(ref canAnswerFileMovePrompt, value);
    }

    public string FileMoveOfferTitle
    {
        get => fileMoveOfferTitle;
        private set => this.RaiseAndSetIfChanged(ref fileMoveOfferTitle, value);
    }

    public string FileMoveOfferContent
    {
        get => fileMoveOfferContent;
        private set => this.RaiseAndSetIfChanged(ref fileMoveOfferContent, value);
    }

    public string FileMoveOfferError
    {
        get => fileMoveOfferError;
        private set => this.RaiseAndSetIfChanged(ref fileMoveOfferError, value);
    }

    public float FileMoveProgress
    {
        get => fileMoveProgress;
        private set
        {
            HasFileMoveProgress = true;
            this.RaiseAndSetIfChanged(ref fileMoveProgress, value);
        }
    }

    public bool HasFileMoveProgress
    {
        get => hasFileMoveProgress;
        private set => this.RaiseAndSetIfChanged(ref hasFileMoveProgress, value);
    }

    public Task<string> DehydrateCacheSize =>
        dehydrateCacheSizeTask ?? throw new InvalidOperationException("Constructor not ran");

    public void OpenSettings()
    {
        ShowSettingsPopup = !ShowSettingsPopup;
    }

    public void CloseSettingsClicked()
    {
        ShowSettingsPopup = !ShowSettingsPopup;
    }

    public void OpenLogsFolder()
    {
        FileUtilities.OpenFolderInPlatformSpecificViewer(launcherPaths.PathToLogFolder);
    }

    public void OpenFileBrowserToInstalled()
    {
        var folder = ThriveInstallationPath;

        if (!Directory.Exists(folder))
        {
            ShowNotice(Resources.FolderNotFound, Resources.ThriveInstallFolderNotFound);
            return;
        }

        FileUtilities.OpenFolderInPlatformSpecificViewer(folder);
    }

    public void ResetInstallLocation()
    {
        SetInstallPathTo(launcherPaths.PathToDefaultThriveInstallFolder);
    }

    public void SetInstallPathTo(string folder)
    {
        folder = folder.Replace('\\', '/');

        if (Settings.ThriveInstallationPath == folder)
            return;

        if (string.IsNullOrWhiteSpace(folder))
        {
            logger.LogError("Cannot set install path to empty");
            return;
        }

        logger.LogInformation("Setting Thrive install path to {Folder}", folder);

        var rawFiles = thriveInstaller.DetectInstalledThriveFolders();

        var installedVersions = rawFiles.ToList();

        this.RaisePropertyChanging(nameof(ThriveInstallationPath));

        // If it is the default path, then we want to actually clear the option to null
        if (launcherPaths.PathToDefaultThriveInstallFolder == folder)
        {
            logger.LogInformation("Resetting install path to default value");
            Settings.ThriveInstallationPath = null;
        }
        else
        {
            logger.LogInformation("Setting install path to {Folder}", folder);
            Settings.ThriveInstallationPath = folder;
        }

        void OnFinished()
        {
            Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(ThriveInstallationPath)));

            TriggerSaveSettings();

            // Refresh the list of installed versions
            StartSettingsViewTasks();
        }

        // Offer to move over the already installed versions
        if (installedVersions.Count > 0)
        {
            OfferFileMove(installedVersions, ThriveInstallationPath, Resources.MoveVersionsTitle,
                Resources.MoveVersionsExplanation, OnFinished);
        }
        else
        {
            OnFinished();
        }
    }

    public void ResetTemporaryLocation()
    {
        SetTemporaryLocationTo(launcherPaths.PathToTemporaryFolder);
    }

    public void SetTemporaryLocationTo(string folder)
    {
        folder = folder.Replace('\\', '/');

        if (Settings.TemporaryDownloadsFolder == folder)
            return;

        if (string.IsNullOrWhiteSpace(folder))
        {
            logger.LogError("Cannot set temporary folder to empty path");
            return;
        }

        logger.LogInformation("Setting temporary downloads folder to {Folder}", folder);

        this.RaisePropertyChanging(nameof(TemporaryDownloadsFolder));

        // If it is the default path, then we want to actually clear the option to null
        if (launcherPaths.PathToTemporaryFolder == folder)
        {
            logger.LogInformation("Resetting temporary path to default value");
            Settings.TemporaryDownloadsFolder = null;
        }
        else
        {
            logger.LogInformation("Setting temporary path to {Folder}", folder);
            Settings.TemporaryDownloadsFolder = folder;
        }

        this.RaisePropertyChanged(nameof(TemporaryDownloadsFolder));

        // Refresh the list of temporary files
        StartSettingsViewTasks();
    }

    public void PromptClearTemporaryLocation()
    {
        var count = thriveInstaller.ListFilesInTemporaryFolder().Count();

        Dispatcher.UIThread.Post(() =>
        {
            // Just directly, delete if there's nothing to prompt about
            if (count < 1)
            {
                AcceptClearTemporaryFiles();
                return;
            }

            ShowClearTemporaryPrompt = true;
            ClearTemporaryPromptContent = string.Format(Resources.ClearTemporaryFilesPromptExplanation,
                TemporaryDownloadsFolder);
            ClearTemporaryPromptCountContent = string.Format(Resources.ClearTemporaryFilesCount, count);
        });
    }

    public void AcceptClearTemporaryFiles()
    {
        if (!Directory.Exists(TemporaryDownloadsFolder))
        {
            logger.LogInformation("Temporary folder doesn't exist so there's nothing to do");
            return;
        }

        logger.LogInformation("Confirmed deletion of temporary folder at {TemporaryDownloadsFolder}",
            TemporaryDownloadsFolder);

        try
        {
            Directory.Delete(TemporaryDownloadsFolder, true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to recursively delete {TemporaryDownloadsFolder}", TemporaryDownloadsFolder);
            ShowNotice(Resources.DeleteErrorTitle,
                string.Format(Resources.DeleteErrorExplanation, TemporaryDownloadsFolder, e.Message));
            return;
        }

        // Refresh temporary files list
        StartSettingsViewTasks();

        ShowClearTemporaryPrompt = false;
    }

    public void CancelClearTemporaryFiles()
    {
        ShowClearTemporaryPrompt = false;
    }

    public void ResetDehydratedCacheLocation()
    {
        SetDehydrateCachePathTo(launcherPaths.PathToDefaultDehydrateCacheFolder);
    }

    public void SetDehydrateCachePathTo(string folder)
    {
        folder = folder.Replace('\\', '/');

        if (Settings.DehydratedCacheFolder == folder)
            return;

        if (string.IsNullOrWhiteSpace(folder))
        {
            logger.LogError("Cannot set dehydrate folder to empty");
            return;
        }

        logger.LogInformation("Setting dehydrated cache path to {Folder}", folder);

        var rawFiles = thriveInstaller.ListFilesInDehydrateCache();

        var movableFiles = rawFiles.ToList();

        this.RaisePropertyChanging(nameof(DehydratedCacheFolder));

        // If it is the default path, then we want to actually clear the option to null
        if (launcherPaths.PathToDefaultDehydrateCacheFolder == folder)
        {
            logger.LogInformation("Resetting dehydrate cache path to default value");
            Settings.DehydratedCacheFolder = null;
        }
        else
        {
            logger.LogInformation("Setting dehydrate cache path to {Folder}", folder);
            Settings.DehydratedCacheFolder = folder;
        }

        void OnFinished()
        {
            Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(DehydratedCacheFolder)));

            TriggerSaveSettings();

            RefreshDehydratedCacheSize();
        }

        // Offer to move the already existing files
        if (movableFiles.Count > 0)
        {
            OfferFileMove(movableFiles, DehydratedCacheFolder, Resources.MoveFilesTitle,
                string.Format(Resources.MoveDehydratedCacheExplanation, movableFiles.Count), OnFinished);
        }
        else
        {
            OnFinished();
        }
    }

    public void PromptClearDehydratedCacheLocation()
    {
        // If the cache location is the default, clear without warning
        if (DehydratedCacheFolder == launcherPaths.PathToDefaultDehydrateCacheFolder)
        {
            logger.LogInformation("Auto-accepting dehydrated clear as the folder is the default one");
            AcceptClearDehydratedCache();
            return;
        }

        var count = thriveInstaller.ListFilesInDehydrateCache().Count();

        Dispatcher.UIThread.Post(() =>
        {
            // Just directly, delete if there's nothing to prompt about
            if (count < 1)
            {
                AcceptClearDehydratedCache();
                return;
            }

            ShowClearDehydratedPrompt = true;
            ClearDehydratedPromptContent = string.Format(Resources.ClearDehydratedFilesPromptExplanation,
                TemporaryDownloadsFolder);
            ClearDehydratedPromptCountContent = string.Format(Resources.ClearDehydratedFilesCount, count);
        });
    }

    public void AcceptClearDehydratedCache()
    {
        logger.LogInformation("Confirmed clear of dehydrated cache at {DehydratedCacheFolder}",
            DehydratedCacheFolder);

        // TODO: if this takes too long on the main thread, move this to a background task (optionally with
        // a progress bar)
        foreach (var file in thriveInstaller.ListFilesInDehydrateCache())
        {
            logger.LogInformation("Deleting {File}", file);

            try
            {
                if (Directory.Exists(file))
                {
                    Directory.Delete(file, true);
                }
                else
                {
                    File.Delete(file);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete {File}", file);
                ShowNotice(Resources.DeleteErrorTitle,
                    string.Format(Resources.DeleteErrorExplanation, file, e.Message));
                break;
            }
        }

        ShowClearDehydratedPrompt = false;
        RefreshDehydratedCacheSize();
    }

    public void CancelClearDehydratedCache()
    {
        ShowClearDehydratedPrompt = false;
    }

    public void PerformFileMove()
    {
        CanAnswerFileMovePrompt = false;

        var task = new Task(() =>
        {
            if (fileMoveOfferFiles == null || string.IsNullOrEmpty(fileMoveOfferTarget))
            {
                logger.LogError("Files to move has not been set correctly");

                ShowNotice(Resources.InternalErrorTitle, Resources.InternalErrorExplanation);
                return;
            }

            logger.LogDebug("Performing file move after offer accepted");

            var total = fileMoveOfferFiles.Count;
            var processed = 0;

            foreach (var file in fileMoveOfferFiles)
            {
                ++processed;

                // Skip if doesn't exist (to allow multiple tries to succeed if one attempt failed
                if (!File.Exists(file) && !Directory.Exists(file))
                    continue;

                try
                {
                    MoveFile(file, fileMoveOfferTarget);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to move files");

                    Dispatcher.UIThread.Post(() =>
                    {
                        FileMoveOfferError = e.Message;
                        CanAnswerFileMovePrompt = true;
                    });

                    return;
                }

                var progress = (float)processed / total;
                Dispatcher.UIThread.Post(() => FileMoveProgress = progress);
            }

            Dispatcher.UIThread.Post(() =>
            {
                HasPendingFileMoveOffer = false;
                fileMoveFinishCallback?.Invoke();
                fileMoveFinishCallback = null;
            });
        });

        task.Start();
    }

    public void CancelFileMove()
    {
        CanAnswerFileMovePrompt = false;
        HasPendingFileMoveOffer = false;

        fileMoveFinishCallback?.Invoke();
        fileMoveFinishCallback = null;
    }

    public bool DeleteVersion(string versionFolderName)
    {
        var target = Path.Join(ThriveInstallationPath, versionFolderName);

        if (!Directory.Exists(target))
        {
            logger.LogError("Can't delete non-existent folder: {Target}", target);

            ShowNotice(Resources.DeleteErrorTitle, Resources.DeleteErrorDoesNotExist);
            return false;
        }

        logger.LogInformation("Deleting version: {Target}", target);

        try
        {
            Directory.Delete(target, true);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to delete {Target}", target);
            ShowNotice(Resources.DeleteErrorTitle, string.Format(Resources.DeleteErrorExplanation, target, e.Message));
            return false;
        }

        // Refresh the version list if it might be visible
        if (ShowSettingsPopup)
        {
            StartSettingsViewTasks();
        }

        return true;
    }

    public void ForgetRememberedVersion()
    {
        if (string.IsNullOrEmpty(settingsManager.RememberedVersion))
        {
            logger.LogDebug("Remembered version already is null");
            return;
        }

        logger.LogInformation("Forgetting remembered version (was: {RememberedVersion}) due to user request",
            settingsManager.RememberedVersion);
        settingsManager.RememberedVersion = null;
    }

    public void ResetAllSettings()
    {
        logger.LogInformation("Resetting all settings");
        logger.LogDebug("Previous settings (note may include DevCenter access token!): {Settings}",
            JsonSerializer.Serialize(settingsManager.Settings));

        settingsManager.Reset();

        // Make sure the default language is actually set to the translation system
        Languages.SetLanguage(languagePlaceHolderIfNotSelected);

        // Notify all settings changed
        this.RaisePropertyChanged(nameof(DisableThriveVideos));
        this.RaisePropertyChanged(nameof(ShowWebContent));
        this.RaisePropertyChanged(nameof(HideLauncherOnPlay));
        this.RaisePropertyChanged(nameof(Hide32BitVersions));
        this.RaisePropertyChanged(nameof(CloseLauncherAfterGameExit));
        this.RaisePropertyChanged(nameof(CloseLauncherOnGameStart));
        this.RaisePropertyChanged(nameof(StoreVersionShowExternalVersions));
        this.RaisePropertyChanged(nameof(EnableStoreVersionSeamlessMode));
        this.RaisePropertyChanged(nameof(SelectedLauncherLanguage));
        this.RaisePropertyChanged(nameof(ThriveInstallationPath));
        this.RaisePropertyChanged(nameof(DehydratedCacheFolder));
        this.RaisePropertyChanged(nameof(TemporaryDownloadsFolder));
        this.RaisePropertyChanged(nameof(DevCenterKey));
        this.RaisePropertyChanged(nameof(SelectedDevBuildType));
        this.RaisePropertyChanged(nameof(ManuallySelectedBuildHash));
        this.RaisePropertyChanged(nameof(ForceOpenGlMode));
        this.RaisePropertyChanged(nameof(DisableThriveVideos));

        // Reset some extra stuff
        RefreshDehydratedCacheSize();
        StartSettingsViewTasks();
    }

    private void OfferFileMove(List<string> filesToMove, string newFolder, string popupTitle, string popupText,
        Action onFinished)
    {
        if (HasPendingFileMoveOffer)
            throw new InvalidOperationException("File move offer already pending");

        HasPendingFileMoveOffer = true;
        CanAnswerFileMovePrompt = true;
        FileMoveOfferError = string.Empty;
        HasFileMoveProgress = false;

        fileMoveOfferFiles = filesToMove;
        fileMoveOfferTarget = newFolder;

        FileMoveOfferTitle = popupTitle;
        FileMoveOfferContent = popupText;

        fileMoveFinishCallback = onFinished;
    }

    private void MoveFile(string file, string targetFolder, bool overwrite = false)
    {
        Directory.CreateDirectory(targetFolder);

        var target = Path.Join(targetFolder, Path.GetFileName(file));

        logger.LogInformation("Moving {File} -> {Target}", file, target);

        bool isFolder = Directory.Exists(file);

        if (isFolder)
        {
            logger.LogDebug("Moved file is a folder");
        }
        else
        {
            logger.LogDebug("Moved file is a file");
        }

        bool tryCopyAndMove = false;

        try
        {
            if (isFolder)
            {
                if (overwrite && Directory.Exists(target))
                {
                    logger.LogInformation("Deleting existing folder to move over it: {Target}", target);
                    Directory.Delete(target);
                }

                Directory.Move(file, target);
            }
            else
            {
                File.Move(file, target, overwrite);
            }
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "Can't move using move, trying copy and delete instead");
            tryCopyAndMove = true;
        }

        if (tryCopyAndMove)
        {
            if (isFolder)
            {
                Directory.CreateDirectory(target);
                CopyHelpers.CopyFoldersRecursivelyWithSymlinks(file, target, overwrite);
                Directory.Delete(file);
            }
            else
            {
                File.Copy(file, target, overwrite);
                File.Delete(file);
            }
        }
    }

    private void StartSettingsViewTasks()
    {
        if (DehydrateCacheSize.Status == TaskStatus.Created)
            DehydrateCacheSize.Start();

        // ReSharper disable MethodSupportsCancellation
        backgroundExceptionNoticeDisplayer.HandleTask(Task.Run(() =>
        {
            var installed = thriveInstaller.ListFoldersInThriveInstallFolder().ToList();

            Dispatcher.UIThread.Post(() => InstalledFolders = installed);
        }));

        backgroundExceptionNoticeDisplayer.HandleTask(Task.Run(() =>
        {
            var files = thriveInstaller.ListFilesInTemporaryFolder().ToList();

            Dispatcher.UIThread.Post(() => TemporaryFolderFiles = files);
        }));

        // ReSharper restore MethodSupportsCancellation
    }

    private void TriggerSaveSettings()
    {
        Dispatcher.UIThread.Post(PerformSave);
    }

    private async void PerformSave()
    {
        // TODO: only save if there are settings changes

        if (!await settingsManager.Save())
        {
            ShowNotice(Resources.SettingsSaveFailedTitle, Resources.SettingsSaveFailedMessage);
        }
        else
        {
            logger.LogInformation("Settings have been saved");
        }
    }

    private void CreateSettingsTabTasks()
    {
        dehydrateCacheSizeTask ??= new Task<string>(() => ComputeDehydrateCacheSizeDisplayString().Result);
    }

    private void RefreshDehydratedCacheSize()
    {
        dehydrateCacheSizeTask = null;
        CreateSettingsTabTasks();

        StartSettingsViewTasks();

        this.RaisePropertyChanged(nameof(DehydrateCacheSize));
    }

    private async Task<string> ComputeDehydrateCacheSizeDisplayString()
    {
        var calculateTask = new Task<long>(() =>
            FileUtilities.CalculateFolderSize(DehydratedCacheFolder));
        calculateTask.Start();

        await calculateTask.WaitAsync(CancellationToken.None);
        var size = calculateTask.Result;

        return string.Format(Resources.SizeInMiB, Math.Round((float)size / GlobalConstants.MEBIBYTE, 1));
    }
}
