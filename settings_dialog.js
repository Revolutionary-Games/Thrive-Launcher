// Everything under the settings button
"use strict";

var {shell} = require('electron');

const { Modal, ComboBox, showGenericError} = require('./modal');
const {listInstalledVersions} = require('./install_handler.js');
const { settings, saveSettings, installPath } = require('./settings.js');

const settingsModal = new Modal("settingsModal", "settingsModalDialog", {
    closeButton: "settingsClose"
});

// Used to skip callbacks on loading settings
let loadingSettings = false;


let settingsButton = document.getElementById("settingsButton");

let listOfInstalledVersions = document.getElementById("listOfInstalledVersions");

settingsButton.addEventListener("click", function(event){

    settingsModal.show();

    listOfInstalledVersions.innerHTML = "<li>Searching for files...</li>";

    listInstalledVersions().then((data) =>{
        listOfInstalledVersions.innerHTML = "";

        for(let key of data){
            let li = document.createElement("li");
            li.textContent = key;
            listOfInstalledVersions.append(li);
        }

    }).catch(err => {
        listOfInstalledVersions.innerHTML = "";
        
        let li = document.createElement("li");
        li.textContent = "An error happened: " + err;
        listOfInstalledVersions.append(li);
    });
});


$("#settingsTabs").tabs();

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

let browseFilesButton = document.getElementById("browseFilesButton");

browseFilesButton.addEventListener("click", function(event){
    const target = installPath;
    console.log("Opening item:", target);
    shell.openItem(target);
});

let enableWebContentCheckbox = document.getElementById("enableWebContentCheckbox");

enableWebContentCheckbox.addEventListener("change", function(event){

    if(loadingSettings)
        return;

    console.log("updating fetch news setting", event.target.checked);

    settings.fetchNewsFromWeb = event.target.checked;
    onSettingsChanged();
});

module.exports.onSettingsLoaded = () =>{
    try{
        loadingSettings = true;

        enableWebContentCheckbox.checked = settings.fetchNewsFromWeb;
        
    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
                         err);
    } finally{

        loadingSettings = false;
    }
};

