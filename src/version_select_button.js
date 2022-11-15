// The version select button as well as the play button
"use strict";

const log = require("electron-log");

const {assert} = require("./utils");
const {getCurrentlySelected, setCurrentlySelectedVersion} = require("./remembered_version");
const {storeInfo, getStylizedName, isSteamVersion} = require("./store_handler");
const {hideDevBuildsInSteam} = require("./config");

const {
    getPlatformForCurrentPlatform,
    getVersionByID,
    getCurrentPlatform,
} = require("./version_info");

const {settings} = require("./settings.js");
const {ComboBox} = require("./modal");

let playCallback = null;

// Buttons
const playButton = document.getElementById("playButton");

const playButtonText = document.getElementById("playText");

const playComboPopup = document.getElementById("playComboPopup");


const versionSelectPopupBackground = document.getElementById("playComboBackground");
const versionSelectPopup = document.getElementById("versionSelectPopup");

const devBuildIdentifier = -1;
const storeBuildIdentifier = -2;

let playComboAllChoices = null;

let versionInfo = null;
let extraVersions = [];

const storeVersionObject = {
    version: {
        store: true,
        id: storeBuildIdentifier,
        getDescriptionString: () => getStylizedName() + " version",
    },
    download: {
        os: storeBuildIdentifier,
        getDescriptionString: () => "",
        folderName: "BundledThrive",
    },
};

function createVersionSelectItem(version){
    const div = document.createElement("div");
    div.classList.add("ComboVersionSelect");
    div.classList.add("Clickable");

    // Hide 32-bit releases if on a 64-bit OS
    if(getCurrentPlatform().arch === "x64" && settings.hide32bit && version.win32On64Bit){
        div.classList.add("Hidden");
    }

    let prefix = "";

    // These have to be these not exact compares as the dataset stores everything as strings
    // noinspection EqualityComparisonWithCoercionJS
    if(version.version.id == playButtonText.dataset.selectedID &&
        version.download.os == playButtonText.dataset.selectedDLOS){
        prefix = "[SELECTED] ";
    }

    div.textContent = prefix + version.version.getDescriptionString() + " " +
        version.download.getDescriptionString();

    div.addEventListener("click", function(){
        log.log("selected version:", version);

        playButtonText.dataset.selectedID = version.version.id;
        playButtonText.dataset.selectedDLOS = version.download.os;

        setCurrentlySelectedVersion(playButtonText.dataset.selectedID,
            playButtonText.dataset.selectedDLOS);

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
        if(storeInfo.isStoreVersion){
            const div = createVersionSelectItem(storeVersionObject);
            versionSelectPopup.append(div);
        }

        for(const version of extraVersions){
            const div = createVersionSelectItem(version);
            versionSelectPopup.append(div);
        }

        if(!storeInfo.isStoreVersion || settings.storeVersionShowExternalVersions){
            for(const version of playComboAllChoices){

                const div = createVersionSelectItem(version);
                versionSelectPopup.append(div);
            }
        }
    },
});

function isDevBuildSelected(){
    return playButtonText.dataset.selectedID == devBuildIdentifier &&
        playButtonText.dataset.selectedDLOS == devBuildIdentifier;
}

function isStoreVersionSelected(){
    return playButtonText.dataset.selectedID == storeBuildIdentifier &&
        playButtonText.dataset.selectedDLOS == storeBuildIdentifier;
}

//! Updates play button text
function updatePlayButtonText(){
    if(isDevBuildSelected()){
        playButtonText.textContent = "Play DevBuild " + getPlatformForCurrentPlatform().name;

    } else if(isStoreVersionSelected()){
        playButtonText.textContent = "Play " + getStylizedName() + " Version";

    } else {
        const version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

        assert(version);

        const download = versionInfo.getDownloadByOSID(version.id,
            playButtonText.dataset.selectedDLOS);

        assert(download);

        playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
            download.getDescriptionString();
    }
}

function isValidVersion(selected){
    if(getVersionByID(selected.selectedVersion) &&
        (!storeInfo.isStoreVersion || settings.storeVersionShowExternalVersions)){
        return true;
    }

    // Dataset has everything as strings in it
    // noinspection EqualityComparisonWithCoercionJS
    if(selected.selectedVersion == devBuildIdentifier &&
        selected.selectedOS == devBuildIdentifier){

        if(isSteamVersion() && hideDevBuildsInSteam)
            return false;

        return true;
    }

    // Dataset has everything as strings in it
    // noinspection EqualityComparisonWithCoercionJS
    if(selected.selectedVersion == storeBuildIdentifier &&
        selected.selectedOS == storeBuildIdentifier && storeInfo.isStoreVersion){
        return true;
    }

    return false;
}

//! Called once version info is loaded
function updatePlayButton(versions){
    versionInfo = versions;
    refreshVersionList();
}

function setStoreVersionAsSelected(){
    log.debug("force set store version as selected");
    playButtonText.dataset.selectedID = "" + storeBuildIdentifier;
    playButtonText.dataset.selectedDLOS = "" + storeBuildIdentifier;

    updatePlayButtonText();
    versionSelectCombo.hide();
}

function refreshVersionList(){
    playButtonText.textContent = "Processing Version Data...";

    const version = versionInfo.getRecommendedVersion();

    if(!version){
        playButtonText.textContent = "Latest version has invalid number";
        return;
    }

    const dl = versionInfo.getDownloadForPlatform(version.id);

    // Dump the other versions to be selected in the combo box thing //
    const options = versionInfo.getAllValidVersions();

    log.debug("All valid versions: " + options.length);

    playComboAllChoices = options;

    // Sort the versions //
    playComboAllChoices.sort(function(a, b){
        // https://stackoverflow.com/a/55466325
        const cmp = a.version.releaseNum.localeCompare(b.version.releaseNum, undefined, {
            numeric: true,
        });

        if(cmp !== 0)
            return -cmp;

        return a.download.os < b.download.os;
    });

    // Restore last selected version (if there is one)
    const selected = getCurrentlySelected();

    let playLatest = true;

    if(selected.selectedVersion){
        // Version is valid or it is the devbuild
        if(isValidVersion(selected)){
            playLatest = false;
            playButtonText.dataset.selectedID = selected.selectedVersion;
        } else {
            log.info("Selected version is no longer valid");
            selected.selectedVersion = null;
            selected.selectedOS = null;
        }
    }

    if(playLatest && storeInfo.isStoreVersion){
        playLatest = false;
        selected.selectedVersion = storeBuildIdentifier;
        selected.selectedOS = storeBuildIdentifier;
        playButtonText.dataset.selectedID = selected.selectedVersion;
        playButtonText.dataset.selectedDLOS = selected.selectedOS;
    }

    if(playLatest){
        // If this is null then we should let the user know that there was no
        // preferred version
        if(!dl){
            playButtonText.textContent = "Couldn't find recommended version for current" +
                " platform";
            return;
        }

        // Verify retrieve logic
        assert(versionInfo.getCurrentPlatform().os === versionInfo.getPlatformByID(dl.os).os);

        playButtonText.dataset.selectedID = version.id;
    }

    if(selected.selectedOS){
        playButtonText.dataset.selectedDLOS = selected.selectedOS;
    } else {
        playButtonText.dataset.selectedDLOS = dl.os;
    }

    updatePlayButtonText();
}

playButtonText.addEventListener("click", () => {
    log.debug("play clicked");

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
module.exports.storeBuildIdentifier = storeBuildIdentifier;
module.exports.refreshVersionList = refreshVersionList;
module.exports.storeVersionObject = storeVersionObject;
module.exports.setStoreVersionAsSelected = setStoreVersionAsSelected;
