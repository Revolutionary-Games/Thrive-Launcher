// The version select button as well as the play button
"use strict";

const assert = require("assert");
const {getPlatformForCurrentPlatform} = require("../version_info");

const {ComboBox} = require("../modal");

let playCallback = null;

// Buttons
const playButton = document.getElementById("playButton");

const playButtonText = document.getElementById("playText");

const playComboPopup = document.getElementById("playComboPopup");


const versionSelectPopupBackground = document.getElementById("playComboBackground");
const versionSelectPopup = document.getElementById("versionSelectPopup");

const devBuildIdentifier = -1;

let playComboAllChoices = null;

let versionInfo = null;
let extraVersions = [];

function createVersionSelectItem(version){
    const div = document.createElement("div");
    div.classList.add("ComboVersionSelect");
    div.classList.add("Clickable");

    let prefix = "";

    if(version.version.id == playButtonText.dataset.selectedID &&
        version.download.os == playButtonText.dataset.selectedDLOS){
        prefix = "[SELECTED] ";
    }

    div.textContent = prefix + version.version.getDescriptionString() + " " +
        version.download.getDescriptionString();

    div.addEventListener("click", function(){
        console.log("selected version:", version);

        playButtonText.dataset.selectedID = version.version.id;
        playButtonText.dataset.selectedDLOS = version.download.os;

        updatePlayButtonText();
        versionSelectCombo.hide();
    });

    return div;
}

const versionSelectCombo = new ComboBox(versionSelectPopupBackground, versionSelectPopup, {
    closeButton: playComboPopup,
    onClose: function(){
    },
    onOpen: function(){
        this.position(playButton);

        versionSelectPopup.innerHTML = "";

        // Add versions //
        for(const version of extraVersions){
            const div = createVersionSelectItem(version);
            versionSelectPopup.append(div);
        }

        for(const version of playComboAllChoices){

            const div = createVersionSelectItem(version);
            versionSelectPopup.append(div);
        }
    },
});

function isDevBuildSelected(){
    return playButtonText.dataset.selectedID == devBuildIdentifier &&
        playButtonText.dataset.selectedDLOS == devBuildIdentifier
}

//! Updates play button text
function updatePlayButtonText(){
    if(!isDevBuildSelected()){

        const version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

        assert(version);

        const download = versionInfo.getDownloadByOSID(version.id,
            playButtonText.dataset.selectedDLOS);

        assert(download);

        playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
            download.getDescriptionString();
    } else {
        playButtonText.textContent = "Play DevBuild " + getPlatformForCurrentPlatform().name;
    }
}

//! Called once version info is loaded
function updatePlayButton(versions){
    versionInfo = versions;

    playButtonText.textContent = "Processing Version Data...";

    const version = versionInfo.getRecommendedVersion();

    if(!version){
        playButtonText.textContent = "Latest version has invalid number";
        return;
    }

    const dl = versionInfo.getDownloadForPlatform(version.id);

    // Dump the other versions to be selected in the combo box thing //
    const options = versionInfo.getAllValidVersions();

    playComboAllChoices = options;

    // Sort the versions //
    playComboAllChoices.sort(function(a, b){
        // TODO: could use semver here (probably)
        if(a.version.releaseNum < b.version.releaseNum)
            return 1;
        if(a.version.releaseNum > b.version.releaseNum)
            return -1;

        return a.download.os < b.download.os;
    });

    console.log("All valid versions: " + options.length);

    // If this is null then we should let the user know that there was no
    // preferred version
    if(!dl){
        playButtonText.textContent = "Couldn't find recommended version for current platform";
        return;
    }

    // Verify retrieve logic
    assert(versionInfo.getCurrentPlatform().os === versionInfo.getPlatformByID(dl.os).os);

    // I don't think this is needed
    // playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
    //     dl.getDescriptionString();

    playButtonText.dataset.selectedID = version.id;
    playButtonText.dataset.selectedDLOS = dl.os;

    updatePlayButtonText();
}

playButtonText.addEventListener("click", () => {
    console.log("play clicked");

    playCallback();
});

module.exports.sendVersionInfoToPlayButton = updatePlayButton;
module.exports.playCallback = (callback) => {
    playCallback = callback;
};
module.exports.setPlayButtonText = (text) => {
    playButtonText.textContent = text;
};

module.exports.getSelectedVersion = () => {
    return {id: playButtonText.dataset.selectedID, os: playButtonText.dataset.selectedDLOS};
};
module.exports.setExtraVersions = (versions) => {
    extraVersions = versions;
};
module.exports.devBuildIdentifier = devBuildIdentifier;
