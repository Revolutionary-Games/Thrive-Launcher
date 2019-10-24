// Everything under the settings button
"use strict";

const path = require('path');

var {shell} = require('electron');
const {dialog} = require('electron').remote;
const win = remote.getCurrentWindow();

const { Modal, showGenericError} = require('./modal');
const {listInstalledVersions, deleteInstalledVersion} = require('./install_handler.js');
const { settings, saveSettings, setInstallPath, getInstallPath} = require('./settings.js');

var { installPath } = require('./settings.js');

const settingsModal = new Modal("settingsModal", "settingsModalDialog", {
    closeButton: "settingsClose"
});

// Used to skip callbacks on loading settings
let loadingSettings = false;

let listInstalledVersionsError = false;

let settingsButton = document.getElementById("settingsButton");

let listOfInstalledVersions = document.getElementById("listOfInstalledVersions");

let currentInstallDir = document.getElementById("currentInstallDir");

function updateInstalledVersions(){
    listOfInstalledVersions.innerHTML = "<li>Searching for files...</li>";

    listInstalledVersions().then((data) =>{
        listOfInstalledVersions.innerHTML = "";
        currentInstallDir.innerHTML = "Directory: " + getInstallPath();

        for(let key in data){

            const obj = data[key];

            let li = document.createElement("li");
            let span = document.createElement("span");            
            
            if(obj.valid){
                span.append(document.createTextNode(obj.name));

                let button = document.createElement("span");
                button.classList.add("VersionDeleteButton");
                button.append(document.createTextNode("DELETE"));

                button.addEventListener("click", function(event){

                    console.log("deleting release:", obj.name);

                    span.style.display = "none";

                    deleteInstalledVersion(obj.name).then(() =>{
                        
                        updateInstalledVersions();
                        
                    }).catch(err => {

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

    }).catch(err => {
        listInstalledVersionsError = true;
        listOfInstalledVersions.innerHTML = "";
        
        let li = document.createElement("li");
        li.textContent = err;
        listOfInstalledVersions.append(li);
    });    
}

settingsButton.addEventListener("click", function(event){

    settingsModal.show();

    updateInstalledVersions();
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
    const target = getInstallPath();
    console.log("Opening item:", target);
    shell.openItem(target);
});

let selectInstallLocation = document.getElementById("selectInstallLocation");

selectInstallLocation.addEventListener("click", function(event){
    dialog.showOpenDialog(win,
    {
        properties: ['openDirectory', 'promptToCreate']
    }, 
    function(path) {
        if(path == undefined){
            console.log("No folder selected");
        }
        else {
            setInstallPath(String(path));
            onSettingsChanged();
            currentInstallDir.innerHTML = "Directory: " + getInstallPath();
            updateInstalledVersions();
        }
    });
});

let enableWebContentCheckbox = document.getElementById("enableWebContentCheckbox");

enableWebContentCheckbox.addEventListener("change", function(event){

    if(loadingSettings)
        return;

    console.log("updating fetch news setting", event.target.checked);

    settings.fetchNewsFromWeb = event.target.checked;
    onSettingsChanged();
});

let hideLauncherOnPlayCheckbox = document.getElementById("hideLauncherOnPlay");

hideLauncherOnPlayCheckbox.addEventListener("change", function(event){

    if(loadingSettings)
        return;

    console.log("updating hide launcher setting", event.target.checked);
    settings.hideLauncherOnPlay = event.target.checked;
    onSettingsChanged();
});

module.exports.onSettingsLoaded = () =>{
    try{
        loadingSettings = true;

        enableWebContentCheckbox.checked = settings.fetchNewsFromWeb;
        hideLauncherOnPlayCheckbox.checked = settings.hideLauncherOnPlay;
        setInstallPath(settings.installDir);

    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
                         err);
    } finally{

        loadingSettings = false;
    }
};