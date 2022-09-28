// Handle the electron builder auto-update
"use strict";

const {ipcRenderer} = require("electron");

const restartButton = document.getElementById("autoUpdateRestartButton");

$(restartButton).button();

module.exports = function(){
    // Setup the auto update listeners
    ipcRenderer.on("updateAvailable", () => {
        ipcRenderer.removeAllListeners("updateAvailable");
    });

    ipcRenderer.on("updateDownloaded", () => {
        ipcRenderer.removeAllListeners("updateDownloaded");
    });
};
