"use strict";

const log = require("electron-log");

const {hideElement} = require("./utils");
const {Modal} = require("./modal");
const {settings, onSettingsChanged, settingsFile} = require("./settings");

const info = {
    store: null,
    isStoreVersion: false,
};

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

function getStylizedName(){
    if(!info.isStoreVersion){
        return "Not Store Version";
    }

    if(info.store === "steam"){
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

    if(info.store === "steam"){
        hideElement("patreonLink");
        hideElement("thriveItchLink");
    }
}

function showThanksMessage(){
    if(settings.thanksDialogDismissed)
        return;

    thanksAutoStartGame.checked = settings.autoStartStoreVersion;
    thanksAutoCloseLauncher.checked = settings.closeLauncherAfterGameExit;
    thanksCloseLauncherAfterStarting.checked = settings.closeLauncherOnGameStart;
    dontThankAgain.checked = true;

    thanksModal.show();
    updateLauncherUnavailableWarning();
}

function updateLauncherUnavailableWarning(){
    if(thanksAutoStartGame.checked &&
        thanksAutoCloseLauncher.checked &&
        thanksCloseLauncherAfterStarting.checked){
        thanksWarningLauncherNotAvailable.style.display = "block";

        thanksWarningLauncherNotAvailable.innerText = "You have selected options that make " +
            "the launcher inaccessible in the future. To get access again you must delete " +
            `the launcher configuration file at ${settingsFile} manually.`;
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
exports.getStylizedName = getStylizedName;
exports.applyHiddenElements = applyHiddenElements;
exports.showThanksMessage = showThanksMessage;
