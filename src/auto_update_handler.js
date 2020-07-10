// Handle the electron builder auto-update
"use strict";

const {ipcRenderer} = require("electron");

const {Modal} = require("../modal");

const updaterModal = new Modal("autoUpdateNotificationModal",
    "autoUpdateNotificationModalDialog", {closeButton: "autoUpdateNotificationClose"});

const updateInfoText = document.getElementById("autoUpdateNotification");

const restartButton = document.getElementById("autoUpdateRestartButton");

$(restartButton).button();

restartButton.addEventListener("click", () => {
    ipcRenderer.send("restartAndUpdate");
});

module.exports = function(){
    // Setup the auto update listeners
    ipcRenderer.on("updateAvailable", () => {
        ipcRenderer.removeAllListeners("updateAvailable");
        updateInfoText.innerText = "A new update is available. Downloading now...";
        updaterModal.show();
    });

    ipcRenderer.on("updateDownloaded", () => {
        ipcRenderer.removeAllListeners("updateDownloaded");
        updateInfoText.innerText =
            "Update Downloaded. It will be installed on restart. Restart now?";
        updaterModal.show();
        restartButton.style.display = "block";
    });
};
