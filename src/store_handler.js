"use strict";

const log = require("electron-log");

const {hideElement} = require("./utils");
const {Modal} = require("./modal");
const {settings, settingsFile} = require("./settings");
const {onSettingsChanged} = require("./settings");
const config = require("./config");

const info = {
    store: null,
    isStoreVersion: false,
};

// If updating these also update the links in index.html
const itchPage = "https://revolutionarygames.itch.io/thrive";
const steamPage = "https://store.steampowered.com/app/1779200";

const thanksModal = new Modal("storeVersionThanksModal",
    "storeVersionThanksModalDialog",
    {
        closeButton: ["thanksModalClose", "closeThanksMessage"], autoClose: false,
        onClose: onThanksClosed,
    });

const thanksAutoStartGame = document.getElementById("thanksAutoStartGame");
const thanksAutoCloseLauncher = document.getElementById("thanksAutoCloseLauncher");
const thanksCloseLauncherAfterStarting = document.
    getElementById("thanksCloseLauncherAfterStarting");
const dontThankAgain = document.getElementById("dontThankAgain");

const thanksWarningLauncherNotAvailable = document.
    getElementById("thanksWarningLauncherNotAvailable");


thanksAutoStartGame.addEventListener("change", () => updateLauncherUnavailableWarning());
thanksAutoCloseLauncher.addEventListener("change", () => updateLauncherUnavailableWarning());
thanksCloseLauncherAfterStarting.addEventListener("change",
    () => updateLauncherUnavailableWarning());

function isSteamVersion(){
    return info.isStoreVersion && info.store === "steam";
}

function getStylizedName(){
    if(!info.isStoreVersion){
        return "Not Store Version";
    }

    if(isSteamVersion()){
        return "Steam";
    } else if(info.store === "itch"){
        return "itch.io";
    }

    return info.store;
}

function applyHiddenElements(){
    if(!info.isStoreVersion){
        // Hide options that apply only in store version
        hideElement("storeVersionSettings");
        hideElement("installedVersionsNoteForStore");
        return;
    }

    hideElement("donateLink");

    if(isSteamVersion()){
        hideElement("patreonLink");
        hideElement("thriveItchLink");

        if(config.hideMainWebsiteInSteam){
            hideElement("mainSiteLink");
        }

        if(config.hideDevBuildsInSteam){
            hideElement("devCenterStatus");
            hideElement("dehydratedCacheOptions");
        }
    }
}

function setThanksStoreLink(){
    const element = document.getElementById("storeVersionThanksSpecificContent");

    element.innerText = "";

    const link = document.createElement("a");

    if(info.store === "steam"){
        link.innerText = "from Steam";
        link.href = steamPage;
    } else if(info.store === "itch"){
        link.innerText = "from itch.io";
        link.href = itchPage;
    }

    element.appendChild(link);
}

function showThanksMessage(){
    if(settings.thanksDialogDismissed)
        return;

    thanksAutoStartGame.checked = settings.autoStartStoreVersion;
    thanksAutoCloseLauncher.checked = settings.closeLauncherAfterGameExit;
    thanksCloseLauncherAfterStarting.checked = settings.closeLauncherOnGameStart;
    dontThankAgain.checked = true;

    setThanksStoreLink();

    thanksModal.show();
    updateLauncherUnavailableWarning();
}

function getInaccessibleLauncherWarning(){
    return "You have selected options that make " +
        "the launcher inaccessible in the future. To get access again you must delete " +
        `the launcher configuration file at ${settingsFile} manually.`;
}

function updateLauncherUnavailableWarning(){
    if(thanksAutoStartGame.checked &&
        thanksAutoCloseLauncher.checked &&
        thanksCloseLauncherAfterStarting.checked){
        thanksWarningLauncherNotAvailable.style.display = "block";

        thanksWarningLauncherNotAvailable.innerText = getInaccessibleLauncherWarning();
    } else {
        thanksWarningLauncherNotAvailable.style.display = "none";
    }
}

function onThanksClosed(){
    log.info("Closing thanks modal");

    settings.autoStartStoreVersion = thanksAutoStartGame.checked;
    settings.closeLauncherAfterGameExit = thanksAutoCloseLauncher.checked;
    settings.closeLauncherOnGameStart = thanksCloseLauncherAfterStarting.checked;
    settings.thanksDialogDismissed = dontThankAgain.checked;

    onSettingsChanged();
}

exports.storeInfo = info;
exports.isSteamVersion = isSteamVersion;
exports.getStylizedName = getStylizedName;
exports.applyHiddenElements = applyHiddenElements;
exports.showThanksMessage = showThanksMessage;
exports.getInaccessibleLauncherWarning = getInaccessibleLauncherWarning;
