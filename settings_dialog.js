// Everything under the settings button
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const fsExtra = remote.require("fs-extra");
const path = require("path");
const rimraf = remote.require("rimraf");

const {shell, dialog} = remote;
const win = remote.getCurrentWindow();

const {Modal, showGenericError} = require("./modal");
const {listInstalledVersions, deleteInstalledVersion} = require("./install_handler.js");
const {calculateFolderSize} = require("./src/file_utils");
const {formatBytes} = require("./src/utils");

const {
    settings, saveSettings, resetSettings, defaultInstallPath, tmpDLFolder,
    getDehydrateCacheFolder,
} = require("./settings.js");

const settingsModal = new Modal("settingsModal", "settingsModalDialog",
    {closeButton: "settingsClose"});

const movingFileModal = new Modal("movingFileModal", "movingFileModalDialog",
    {autoClose: false});

// Used to skip callbacks on loading settings
let loadingSettings = false;

const settingsButton = document.getElementById("settingsButton");

const listOfInstalledVersions = document.getElementById("listOfInstalledVersions");

const currentInstallDir = document.getElementById("currentInstallDir");

const currentTmpDLFolder = document.getElementById("currentTmpDLFolder");

const listOfTemporaryDownloads = document.getElementById("listOfTemporaryDownloads");

const clearTemporaryDownloads = document.getElementById("clearTemporaryDownloads");

const currentDehydratedCacheFolder = document.getElementById("currentDehydratedCacheFolder");
const dehydratedCacheSize = document.getElementById("dehydratedCacheSize");
const clearDehydratedCache = document.getElementById("clearDehydratedCache");

function updateInstalledVersions(){
    listOfInstalledVersions.innerHTML = "<li>Searching for files...</li>";

    listInstalledVersions().then((data) => {
        listOfInstalledVersions.innerHTML = "";
        currentInstallDir.textContent = "Directory: " + settings.installPath;

        for(const key in data){

            const obj = data[key];

            const li = document.createElement("li");
            const span = document.createElement("span");

            if(obj.valid){

                span.append(document.createTextNode(obj.special + obj.name));

                const button = document.createElement("span");
                button.classList.add("VersionDeleteButton");
                button.append(document.createTextNode("DELETE"));

                button.addEventListener("click", function(){

                    console.log("deleting release:", obj.name);

                    span.style.display = "none";

                    deleteInstalledVersion(obj.name).then(() => {

                        updateInstalledVersions();

                    }).catch((err) => {

                        showGenericError("Failed to delete the version. " + err);
                        span.style.display = "";
                    });
                });

                span.append(button);

            } else {
                span.append(document.createTextNode("Unknown folder present: "));
                span.append(document.createTextNode(obj.path));
            }

            li.append(span);
            listOfInstalledVersions.append(li);
        }

    }).catch((err) => {
        listOfInstalledVersions.innerHTML = "";

        const li = document.createElement("li");
        li.textContent = "An error happened: " + err;
        listOfInstalledVersions.append(li);
    });
}

function updateTempDownloads(){
    currentTmpDLFolder.textContent = "Temporary downloads folder: " + tmpDLFolder;

    listOfTemporaryDownloads.innerHTML = "<li>Searching for files...</li>";

    if(!fs.existsSync(tmpDLFolder)){
        listOfTemporaryDownloads.innerHTML = "Temporary folder doesn't exist";
        return;
    }

    fs.readdir(tmpDLFolder, (err, files) => {
        if(err){
            listOfTemporaryDownloads.innerHTML = "Failed to read folder contents: " + err;
            return;
        }

        listOfTemporaryDownloads.innerHTML = "";

        for(const file of files){
            const li = document.createElement("li");

            li.append(document.createTextNode(file));

            listOfTemporaryDownloads.append(li);
        }
    });
}

function deleteTempFiles(){
    rimraf(tmpDLFolder, (error) => {
        if(error){
            showGenericError("Failed to delete temporary files, error: " + error);
        }

        updateTempDownloads();
    });
}

function updateDehydratedCache(){
    currentDehydratedCacheFolder.textContent =
        "DevBuilds file cache: " + getDehydrateCacheFolder();

    if(!fs.existsSync(getDehydrateCacheFolder())){
        dehydratedCacheSize.textContent = "Folder doesn't exist";
        return;
    }

    calculateFolderSize(getDehydrateCacheFolder()).then((size) => {
        dehydratedCacheSize.textContent = "Size: " + formatBytes(size);
    }).catch((error) => {
        dehydratedCacheSize.textContent = "Failed to read size: " + error;
    });
}

function deleteDehydratedFiles(){
    rimraf(getDehydrateCacheFolder(), (error) => {
        if(error){
            showGenericError("Failed to delete dehydrated files, error: " + error);
        }

        updateDehydratedCache();
    });
}

async function moveInstalledFiles(files, destination){
    movingFileModal.show();
    const content = document.getElementById("movingFileModalContent");

    content.textContent = "Moving files to: " + destination + " ...";
    content.append(document.createElement("br"));
    content.append(document.createTextNode("This may take several minutes, " +
        "please be patient."));

    await Promise.all(files.map((file) =>
        fsExtra.move(file, path.join(destination, path.basename(file))).then(() => {
            console.log("moved: " + path.basename(file));
        }))).
        then(() => {
            console.log("successfully moved all the files");

            settings.installPath = destination;
            onSettingsChanged();
            updateInstalledVersions();
            movingFileModal.hide();
        }).
        catch((err) => {
            movingFileModal.hide();
            showGenericError("Failed to move file(s): " + err.message);
        });
}

settingsButton.addEventListener("click", function(){
    settingsModal.show();

    updateInstalledVersions();
    updateTempDownloads();
    updateDehydratedCache();
});

$("#settingsTabs").tabs();

clearTemporaryDownloads.addEventListener("click", function(){

    deleteTempFiles();
});

// This is bugged inside tabs
// $("#enableWebContentCheckbox").checkboxradio();

// Helper for saving
function onSettingsChanged(){
    try{
        saveSettings();
    } catch(err){
        showGenericError("Failed to save settings, error: " + err);
    }
}

const browseFilesButton = document.getElementById("browseFilesButton");

browseFilesButton.addEventListener("click", function(){
    const target = settings.installPath;
    console.log("Opening item:", target);

    if(!fs.existsSync(target)){
        showGenericError("Target folder (" + target + ") does not exist");
    } else {
        shell.openItem(target);
    }
});

function changeInstallLocation(directory){

    listInstalledVersions().then((data) => {

        const files = [];

        for(const key in data){

            const obj = data[key];

            if(obj.valid){
                files.push(obj.path);
            }
        }

        if(!Array.isArray(files) || !files.length){
            console.log("No files found");

            settings.installPath = directory;
            onSettingsChanged();
            updateInstalledVersions();

            return;
        }

        const options = {
            title: "Warning!",
            type: "warning",
            buttons: ["Yes", "No"],
            message: "A Thrive version already exist in the current directory \n" +
                "Do you want to move the files into the selected location?",
        };

        dialog.showMessageBox(win, options).then((response) => {
            if(response.response === 0){
                if(settings.installPath != directory){
                    moveInstalledFiles(files, directory);
                }
            } else if(response.response === 1){
                settings.installPath = directory;
                onSettingsChanged();
                updateInstalledVersions();
            } else {
                showGenericError("Unknown dialog response");
            }
        });
    });
}

// Button to select the install location
const selectInstallLocation = document.getElementById("selectInstallLocation");

selectInstallLocation.addEventListener("click", function(){
    dialog.showOpenDialog(win, {properties: ["openDirectory", "promptToCreate"]}).
        then((result) => {
            if(result.canceled)
                return;

            if(result.filePaths.length != 1){
                showGenericError("A single folder wasn't selected");
            } else {
                changeInstallLocation(result.filePaths[0]);
            }
        });
});

// Button to reset the install location
const resetInstallLocation = document.getElementById("resetInstallLocation");

resetInstallLocation.addEventListener("click", function(){
    // "Disables" the button when the install path is
    // at the default install location
    if(settings.installPath != defaultInstallPath){
        changeInstallLocation(String(defaultInstallPath));
    }
});

// All settings reset option
const resetAllSettingsButton = document.getElementById("resetAllSettingsButton");

resetAllSettingsButton.addEventListener("click", function(){
    resetSettings();
});

const enableWebContentCheckbox = document.getElementById("enableWebContentCheckbox");

enableWebContentCheckbox.addEventListener("change", function(event){
    if(loadingSettings)
        return;

    console.log("updating fetch news setting", event.target.checked);

    settings.fetchNewsFromWeb = event.target.checked;
    onSettingsChanged();
});

// Button to hide the window if the game is launched
const hideLauncherOnPlayCheckbox = document.getElementById("hideLauncherOnPlay");

hideLauncherOnPlayCheckbox.addEventListener("change", function(event){
    if(loadingSettings)
        return;

    console.log("updating hide launcher setting", event.target.checked);
    settings.hideLauncherOnPlay = event.target.checked;
    onSettingsChanged();
});

// --single-process flag option
const enableSingleProcessLaunch = document.getElementById("enableSingleProcessLaunch");

enableSingleProcessLaunch.addEventListener("change", function(event){
    if(loadingSettings)
        return;

    settings.launchOptionSingleProcess = event.target.checked;
    onSettingsChanged();
});

// --no-sandbox flag option
const disableGUISandbox = document.getElementById("disableGUISandbox");

disableGUISandbox.addEventListener("change", function(event){
    if(loadingSettings)
        return;

    settings.launchOptionNoGUISandbox = event.target.checked;
    onSettingsChanged();
});

// --disable-gpu flag option
const disableGUIGPU = document.getElementById("disableGUIGPU");

disableGUIGPU.addEventListener("change", function(event){
    if(loadingSettings)
        return;

    settings.launchOptionNoGUIGPU = event.target.checked;
    onSettingsChanged();
});

clearDehydratedCache.addEventListener("click", () => {
    deleteDehydratedFiles();
});

module.exports.onSettingsLoaded = () => {
    try{
        loadingSettings = true;

        enableWebContentCheckbox.checked = settings.fetchNewsFromWeb;
        hideLauncherOnPlayCheckbox.checked = settings.hideLauncherOnPlay;
        enableSingleProcessLaunch.checked = settings.launchOptionSingleProcess;
        disableGUISandbox.checked = settings.launchOptionNoGUISandbox;
        disableGUIGPU.checked = settings.launchOptionNoGUIGPU;

        console.log("Install path set to: " + settings.installPath);

    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
            err);
    } finally{

        loadingSettings = false;
    }
};
