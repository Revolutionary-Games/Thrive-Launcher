// Everything under the settings button
"use strict";

const fsExtra = require('fs-extra');
const path = require('path');

var {shell} = require('electron');
const {dialog} = require('electron').remote;
const win = remote.getCurrentWindow();

const { Modal, showGenericError} = require('./modal');
const { listInstalledVersions, deleteInstalledVersion } = require('./install_handler.js');

const { settings, saveSettings, defaultInstallPath } = require('./settings.js');

const settingsModal = new Modal("settingsModal", "settingsModalDialog", {
    closeButton: "settingsClose"
});

const movingFileModal = new Modal("movingFileModal", "movingFileModalDialog", {
    autoClose: false
});

// Used to skip callbacks on loading settings
let loadingSettings = false;

let settingsButton = document.getElementById("settingsButton");

let listOfInstalledVersions = document.getElementById("listOfInstalledVersions");

let currentInstallDir = document.getElementById("currentInstallDir");

function updateInstalledVersions(){
    listOfInstalledVersions.innerHTML = "<li>Searching for files...</li>";

    listInstalledVersions().then((data) =>{
        listOfInstalledVersions.innerHTML = "";
        currentInstallDir.innerHTML = "Installation Directory: " + settings.installPath;

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
        listOfInstalledVersions.innerHTML = "";
        
        let li = document.createElement("li");
        li.textContent = "An error happened: " + err;
        listOfInstalledVersions.append(li);
    });    
}

async function moveInstalledFiles(files, destination){
    
    movingFileModal.show();
    let content = document.getElementById("movingFileModalContent");
    content.innerHTML = "Moving files to: " + destination + " ...";
    content.append(document.createElement("br"));
    content.append(document.createTextNode("This may take several minutes, please be patient."));

    await Promise.all(files.map(file =>
        fsExtra.move(file, path.join(destination, path.basename(file))).then(() => { console.log("moved: " + path.basename(file)) } )))
        .then(() =>{
            console.log("successfully moved all the files");
        })
        .catch(err => {
            movingFileModal.hide();
            showGenericError("Failed to move file(s): " + err.message);
        });

    settings.installPath = destination;
    onSettingsChanged();
    updateInstalledVersions();
    movingFileModal.hide();
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
    const target = settings.installPath;
    console.log("Opening item:", target);
    shell.openItem(target);
});

function askToMoveFiles(selectedDirectory){

    listInstalledVersions().then((data) => {

        let files = [];

        for(let key in data){

            const obj = data[key];

            if(obj.valid){
                files.push(obj.path);
            }
        }

        if (!Array.isArray(files) || !files.length) {
            console.log("No files found");
    
            settings.installPath = selectedDirectory;
            onSettingsChanged();
            updateInstalledVersions();

            return;
        }

        const options = {
            title: "Warning!",
            type: "warning",
            buttons: ['Yes', 'No'],
            message: "A Thrive version already exist in the current directory \n"
                    + "Do you want to move the files into the selected location?"
        }

        dialog.showMessageBox(win, options, (response) => {
            if(response == 0){
                if(settings.installPath != selectedDirectory){
                    moveInstalledFiles(files, selectedDirectory);
                }
            }
            if(response == 1){
                settings.installPath = selectedDirectory;
                onSettingsChanged();
                updateInstalledVersions();
            }
        });
    });
}

// Button to select the install location
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
            askToMoveFiles(String(path));
        }
    });
});

// Button to reset the install location
let resetInstallLocation = document.getElementById("resetInstallLocation");

resetInstallLocation.addEventListener("click", function(event){

    // "Disables" the button when the install path is
    // at the default install location
    if(settings.installPath != defaultInstallPath){
        askToMoveFiles(String(defaultInstallPath));
    }
});

let enableWebContentCheckbox = document.getElementById("enableWebContentCheckbox");

enableWebContentCheckbox.addEventListener("change", function(event){

    if(loadingSettings)
        return;

    console.log("updating fetch news setting", event.target.checked);

    settings.fetchNewsFromWeb = event.target.checked;
    onSettingsChanged();
});

// Button to hide the window if the game is launched
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

        console.log("Install path set to: " + settings.installPath);

    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
                         err);
    } finally{

        loadingSettings = false;
    }
};
