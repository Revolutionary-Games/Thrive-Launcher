// Everything for handling the devcenter connection and devbuild fetching
"use strict";

const url = require("url");

const {devCenterURL} = require("./config");
const {settings, saveSettings} = require("../settings");
const {Modal, showGenericError} = require("../modal");

const noConnectionMessage = "Connect to DevCenter";


const launcherCheckAPI = url.resolve(devCenterURL, "/api/v1/launcher/status");

const defaultConnectionStatus = {
    connected: false,
    error: null,
    username: null,
    email: null,
    developer: false,
    build_of_the_day: null,
};

module.exports.status = Object.assign({}, defaultConnectionStatus);

// Variables for stuff
const devCenterModal = new Modal("devCenterModal", "devCenterModalDialog",
    {closeButton: "devCenterConnectionClose", autoClose: true});

const statusLabel = document.getElementById("devCenterStatusMessage");
const openModalButton = document.getElementById("devCenterPopupOpen");

function resetInfoInStatus(){
    module.exports.status.connected = false;
    module.exports.status.error = null;
    module.exports.status.username = null;
    module.exports.status.email = null;
    module.exports.status.developer = false;
    module.exports.status.build_of_the_day = null;
}

function resetTokenInSettings(){
    console.log("Clearing DevCenter access key");
    settings.devCenterKey = null;
    saveSettings();
}

function onNoDevCenterConnection(){
    statusLabel.innerText = "";
    openModalButton.innerText = noConnectionMessage;

    resetInfoInStatus();
}

// Checks if we currently have a devcenter connection and it is valid
function checkConnectionStatus(){
    if(!settings.devCenterKey){
        onNoDevCenterConnection();
        return;
    }

    statusLabel.innerText = "Loading...";
    openModalButton.innerText = "";

    // No valid token found
    // openModalButton.innerText = noConnectionMessage;
    fetch(launcherCheckAPI, {
        headers: {
            Authorization: settings.devCenterKey,
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 200){
                // Failed
                throw data.message;
            }

            return data;
        }).catch((error) => {
            console.log("Failed to parse response:", error);
            console.log(response);
            throw error;
        });
    }).then((data) => {

        statusLabel.innerText = "";

        resetInfoInStatus();
        module.exports.status.connected = true;
        module.exports.status.username = data.username;
        module.exports.status.email = data.email;
        module.exports.status.developer = data.developer;

        openModalButton.innerText = module.exports.status.username;

    }).catch((error) => {

        onNoDevCenterConnection();

        module.exports.status.error = `${error}`;

        showGenericError(`Failed to connect to ThriveDevCenter: ${error}`, () => {
            resetTokenInSettings();
        });
    });
}

// Init our callbacks
openModalButton.addEventListener("click", (e) => {
    e.preventDefault();
    devCenterModal.show();
});

$("#devCenterTabs").tabs();

module.exports.checkConnectionStatus = checkConnectionStatus;
