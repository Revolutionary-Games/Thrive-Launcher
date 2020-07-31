// Remembers the user selected game version if it isn't the latest
"use strict";

const remote = require("electron").remote;

const path = require("path");
const fs = remote.require("fs");

const {dataFolder} = require("../settings");


const selectedVersionFile = path.join(dataFolder, "selected_version.json");

const defaultValues = {
    selectedVersion: null,
    selectedOS: null,
};

const currentlySelected = Object.assign({}, defaultValues);
let latestVersion = 0;


function saveSelectedVersion(){
    fs.writeFile(selectedVersionFile, JSON.stringify(currentlySelected), (error) => {
        if(error)
            console.error("Failed to save selected version:", error);
    });
}

function loadSelectedVersion(){
    try{
        const data = fs.readFileSync(selectedVersionFile);

        const newValues = JSON.parse(data);

        Object.assign(currentlySelected, newValues);

    } catch(error){
        console.log("Failed to read selected version file, error:", error);
    }
}

function setCurrentlySelectedVersion(version, selectedOS){
    if(currentlySelected.selectedVersion === version &&
        currentlySelected.selectedOS === selectedOS)
        return;

    // Detection of latest version
    if(version === latestVersion){
        currentlySelected.selectedVersion = null;
    } else {
        currentlySelected.selectedVersion = version;
    }

    currentlySelected.selectedOS = selectedOS;

    saveSelectedVersion();
}

function reportLatestVersion(version){
    latestVersion = "" + version;
}

function getCurrentlySelected(){
    return currentlySelected;
}

exports.saveSelectedVersion = saveSelectedVersion;
exports.setCurrentlySelectedVersion = setCurrentlySelectedVersion;
exports.reportLatestVersion = reportLatestVersion;
exports.getCurrentlySelected = getCurrentlySelected;
exports.loadSelectedVersion = loadSelectedVersion;
