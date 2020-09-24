//
// Program settings functionality
//
"use strict";

const remote = require("electron").remote;

const path = require("path");
const fs = remote.require("fs");
const mkdirp = remote.require("mkdirp");
const {getOSArch} = require("./utils");

module.exports.dataFolder = path.join(remote.app.getPath("appData"), "Revolutionary-Games",
    "Launcher");

module.exports.tmpDLFolder = path.join(remote.app.getPath("temp"),
    "Revolutionary-Games-Launcher");

module.exports.locallyCachedDLFile = path.join(module.exports.dataFolder,
    "saved_version_db_v2.json");

module.exports.defaultInstallPath = path.join(module.exports.dataFolder, "Installed");

module.exports.getDevBuildFolder = () => path.join(module.exports.settings.installPath,
    "devbuild");

// TODO: make this configurable
module.exports.getDehydrateCacheFolder = () => path.join(module.exports.dataFolder,
    "DehydratedCache");

// Make sure it exists. This simplifies a lot of code
mkdirp.sync(module.exports.dataFolder);

const defaultSettings = {
    fetchNewsFromWeb: true,
    hideLauncherOnPlay: true,
    hide32bit: getOSArch() === "x64",
    launchOptionSingleProcess: false,
    launchOptionNoGUISandbox: false,
    launchOptionNoGUIGPU: false,
    installPath: module.exports.defaultInstallPath,
    devCenterKey: null,
    selectedDevBuildType: null,
    manuallySelectedBuildHash: null,
};

module.exports.settings = Object.assign({}, defaultSettings);

const settingsFile = path.join(module.exports.dataFolder, "launcher_settings.json");

function updateSettingsDialog(){
    // Update controls
    require("./settings_dialog.js").onSettingsLoaded();
}

// Throws on error
module.exports.saveSettings = () => {

    fs.writeFileSync(settingsFile, JSON.stringify(module.exports.settings));
};

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
