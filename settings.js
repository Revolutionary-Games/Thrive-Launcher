//
// Program settings functionality
//
"use strict";

const path = require('path');
const fs = require('fs');
const mkdirp = require('mkdirp');

var {remote} = require('electron');

module.exports.dataFolder = path.join(remote.app.getPath("appData"), "Revolutionary-Games",
                                      "Launcher");

module.exports.tmpDLFolder = path.join(remote.app.getPath("temp"),
                                       "Revolutionary-Games-Launcher");

module.exports.locallyCachedDLFile = path.join(module.exports.dataFolder,
                                               "saved_version_db_v2.json");

module.exports.setInstallPath = (directory) => {
    module.exports.installPath = directory;
    console.log("Install path set to:", module.exports.installPath);
};

module.exports.getInstallPath = () => {
    return String(module.exports.installPath);
};

// Make sure it exists. This simplifies a lot of code
mkdirp.sync(module.exports.dataFolder);

module.exports.settings = {
    fetchNewsFromWeb: true,
    hideLauncherOnPlay: true,
    installDir: path.join(module.exports.dataFolder, "Installed"),
};

const settingsFile = path.join(module.exports.dataFolder, "launcher_settings.json");

// Throws on error
module.exports.saveSettings = () => {

    this.settings.installDir = this.getInstallPath();
    fs.writeFileSync(settingsFile, JSON.stringify(module.exports.settings));
};

module.exports.loadSettings = () => {
    try{
        const data = fs.readFileSync(settingsFile);

        let newSettings = JSON.parse(data);

        Object.assign(module.exports.settings, newSettings);

    } catch(err){
        console.log("Failed to read settings file, using defaults, error:", err);
    }

    // Update controls
    require('./settings_dialog.js').onSettingsLoaded();
};