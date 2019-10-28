// Everything under the settings button
"use strict";

const fs = require('fs-extra');
const path = require('path');

var {shell} = require('electron');
const {dialog} = require('electron').remote;
const win = remote.getCurrentWindow();

const {getVersionData} = require("./version_info.js");

const { Modal, showGenericError} = require('./modal');
const { listInstalledVersions, deleteInstalledVersion } = require('./install_handler.js');

const { settings, dataFolder, saveSettings, defaultInstallPath,
        installPath, setInstallPath, getInstallPath } 
        = require('./settings.js');

const settingsModal = new Modal("settingsModal", "settingsModalDialog", {
    closeButton: "settingsClose"
});

const movingFileModal = new Modal("movingFileModal", "movingFileModalDialog", {
    autoClose: false
});

var selectedDirectory = null;

var installedVersions = [];

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
        currentInstallDir.innerHTML = "Installation Directory: " + getInstallPath();

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

function getInstalledVersions(){

    const versions = getVersionData().versions;
    
    fs.readdir(getInstallPath(), function (err, filesPath) {
        if (err) throw err;
        for (var i = 0; i < filesPath.length; i++) {

            for(let ver of versions){
                for(let dl of ver.platforms){
                    if(filesPath[i].includes(dl.folderName)){
                        installedVersions = filesPath.map(function (filePath) {
                            return path.join(getInstallPath(), filePath);
                        });
                    }
                }
            }
        }
    });
}

function moveInstalledFiles(destination){

    listInstalledVersions().then((data) => {

        for(let key in data){

            const obj = data[key];
            
            const source = path.dirname(obj.path);

            if(obj.valid){
                movingFileModal.show();
                let content = document.getElementById("movingFileModalContent");
                content.innerHTML = "Moving files to: " + destination + " ...";
                content.append(document.createElement("br"));
                content.append(document.createTextNode("This may take several minutes, please be patient."))

                fs.move(path.join(source, obj.name), path.join(destination, obj.name))
                .then(() => {
                    console.log("Successfully moving: " + obj.name);
        
                    setInstallPath(destination);
                    saveSettings();
                    
                    movingFileModal.hide();
                    updateInstalledVersions();
                })
                .catch(err => {
                    dialog.showErrorBox("Error!", "Error: " + err);
                    movingFileModal.hide();
                })
            }
        }
    });
}

settingsButton.addEventListener("click", function(event){

    settingsModal.show();    

    getInstalledVersions();

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

function askToMoveFiles(){
    
    const options = {
        title: "Warning!",
        type: "warning",
        buttons: ['Yes', 'No'],
        message: "A Thrive version already exist in the current directory \n"
                + "Do you want to move the files into the selected location?"
    }

    dialog.showMessageBox(win, options, (response) => {
        if(response == 0){
            moveInstalledFiles(selectedDirectory);
        }
        if(response == 1){
            setInstallPath(selectedDirectory);
            onSettingsChanged();
            updateInstalledVersions();
        }
    })
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
            selectedDirectory = String(path);

            if(selectedDirectory != null){
                if (!Array.isArray(installedVersions) || !installedVersions.length) {
                    console.log("No files to be found");

                    setInstallPath(selectedDirectory);
                    onSettingsChanged();
                    updateInstalledVersions();
                }else{
                    askToMoveFiles();
                }
            }
        }
    });
});

// Button to reset the install location
let resetInstallLocation = document.getElementById("resetInstallLocation");

resetInstallLocation.addEventListener("click", function(event){

    if(getInstallPath() != defaultInstallPath){
        selectedDirectory = String(defaultInstallPath);
        askToMoveFiles();
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

        setInstallPath(settings.installDir);

    } catch(err){
        showGenericError("Failed to update settings widgets from saved settings, error: " +
                         err);
    } finally{

        loadingSettings = false;
    }
};
