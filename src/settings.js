//
// Program settings functionality
//
"use strict";

const remote = require("@electron/remote");

const path = require("path");
const fs = remote.require("fs");
const mkdirp = remote.require("mkdirp");

const {showGenericError} = require("./modal");

module.exports.dataFolder = path.join(remote.app.getPath("appData"), "Revolutionary-Games",
    "Launcher");

module.exports.tmpDLFolder = path.join(remote.app.getPath("temp"),
    "Revolutionary-Games-Launcher");

module.exports.locallyCachedDLFile = path.join(module.exports.dataFolder,
    "saved_version_db_v2.json");

module.exports.defaultInstallPath = path.join(module.exports.dataFolder, "Installed");

module.exports.getDevBuildFolder = () => path.join(module.exports.settings.installPath,
    "devbuild");

module.exports.defaultDehydratedCacheFolder = path.join(module.exports.dataFolder,
    "DehydratedCache");

module.exports.getDehydrateCacheFolder = () => module.exports.settings.cacheFolderPath;

// Make sure it exists. This simplifies a lot of code
mkdirp.sync(module.exports.dataFolder);

const defaultSettings = {
    fetchNewsFromWeb: true,
    hideLauncherOnPlay: true,
    hide32bit: true,
    launchOptionSingleProcess: false,
    launchOptionNoGUISandbox: false,
    launchOptionNoGUIGPU: false,
    installPath: module.exports.defaultInstallPath,
    cacheFolderPath: module.exports.defaultDehydratedCacheFolder,
    temporaryFolder: module.exports.tmpDLFolder,
    devCenterKey: null,
    selectedDevBuildType: null,
    manuallySelectedBuildHash: null,
    beginningKeptGameOutput: 100,
    lastKeptGameOutput: 900,
    closeLauncherAfterGameExit: false,
    closeLauncherOnGameStart: false,
    storeVersionShowExternalVersions: false,
    thanksDialogDismissed: false,
    autoStartStoreVersion: false,
};

module.exports.settings = Object.assign({}, defaultSettings);

const settingsFile = path.join(module.exports.dataFolder, "launcher_settings.json");

function updateSettingsDialog(){
    // Update controls
    require("./settings_dialog.js").onSettingsLoaded();
}

// Throws on error
function saveSettings(){
    fs.writeFileSync(settingsFile, JSON.stringify(module.exports.settings));
}

// Helper for saving
function onSettingsChanged(){
    try{
        saveSettings();
    } catch(err){
        showGenericError("Failed to save settings, error: " + err);
    }
}

module.exports.saveSettings = saveSettings;
module.exports.onSettingsChanged = onSettingsChanged;

module.exports.loadSettings = () => {
    try{
        const data = fs.readFileSync(settingsFile);

        const newSettings = JSON.parse(data);

        Object.assign(module.exports.settings, newSettings);

    } catch(err){
        console.log("Failed to read settings file, using defaults, error:", err);
    }

    updateSettingsDialog();
};

module.exports.resetSettings = () => {
    // Clear properties
    for(const variableKey in module.exports.settings){
        if(Object.prototype.hasOwnProperty.call(module.exports.settings, variableKey)){
            delete module.exports.settings[variableKey];
        }
    }

    // Assign defaults
    Object.assign(module.exports.settings, defaultSettings);
    module.exports.saveSettings();

    updateSettingsDialog();

    console.log("Settings reset to defaults", module.exports.settings);
};

module.exports.settingsFile = settingsFile;
