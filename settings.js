// Everything under the settings button
const path = require('path');
const fs = require('fs');
const mkdirp = require('mkdirp');

var {remote} = require('electron');

const { Modal, ComboBox, showGenericError} = require('./modal');

const settingsModal = new Modal("settingsModal", "settingsModalDialog", {
    closeButton: "settingsClose"
});

let settingsButton = document.getElementById("settingsButton");

settingsButton.addEventListener("click", function(event){

    settingsModal.show();
});


$("#settingsTabs").tabs();

// This is bugged inside tabs
// $("#enableWebContentCheckbox").checkboxradio();

// Helper for saving
function onSettingsChanged(){
    try{
        module.exports.saveSettings();
    } catch(err){
        showGenericError("Failed to save settings, error: " + err);
    }
}

// Used to skip callbacks on loading settings
let loadingSettings = false;

let browseFilesButton = document.getElementById("browseFilesButton");

browseFilesButton.addEventListener("click", function(event){

    
});

let enableWebContentCheckbox = document.getElementById("enableWebContentCheckbox");

enableWebContentCheckbox.addEventListener("change", function(event){

    if(loadingSettings)
        return;

    console.log("updating fetch news setting", event.target.checked);

    module.exports.settings.fetchNewsFromWeb = event.target.checked;
    onSettingsChanged();
});


//
// Exported part
//

module.exports.dataFolder = path.join(remote.app.getPath("appData"), "Revolutionary-Games",
                                      "Launcher");

module.exports.tmpDLFolder = path.join(remote.app.getPath("temp"),
                                       "Revolutionary-Games-Launcher");

module.exports.installPath = path.join(module.exports.dataFolder, "Installed");

module.exports.locallyCachedDLFile = path.join(module.exports.dataFolder,
                                               "saved_version_db_v2.json");

// Make sure it exists. This simplifies a lot of code
mkdirp.sync(module.exports.dataFolder);

module.exports.settings = {
    fetchNewsFromWeb: true
};

const settingsFile = path.join(module.exports.dataFolder, "launcher_settings.json");

// Throws on error
module.exports.saveSettings = () => {

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
    try{
        loadingSettings = true;

        enableWebContentCheckbox.checked = module.exports.settings.fetchNewsFromWeb;
        
    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
                         err);
    } finally{

        loadingSettings = false;
    }
};


