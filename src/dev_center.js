// Everything for handling the devcenter connection and devbuild fetching
"use strict";

const url = require("url");

const {devCenterURL} = require("./config");
const {settings, saveSettings} = require("../settings");
const {Modal, showGenericError} = require("../modal");

const noConnectionMessage = "Connect to DevCenter";


const launcherCheckAPI = url.resolve(devCenterURL, "/api/v1/launcher/status");
const launcherTestTokenAPI = url.resolve(devCenterURL, "/api/v1/launcher/check_link");
const launcherFormConnectionAPI = url.resolve(devCenterURL, "/api/v1/launcher/link");

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
const loginButton = document.getElementById("loginToDevCenter");
const loginCode = document.getElementById("loginLinkCode");
const linkMessage = document.getElementById("devCenterLinkCheckMessage");
const linkStatusContainer = document.getElementById("devCenterLinkCheckStatus");
const retryButton = document.getElementById("retryLoginCode");
const disconnectButton = document.getElementById("disconnectFromDevCenter");
const disconnectInfo = document.getElementById("devCenterDisconnectStatus");
const connectedDetails = document.getElementById("devCenterConnectedUserDetails");

const connectedContent = [
    document.getElementById("devCenterConnectedContent"),
    document.getElementById("devCenterConnectedDevBuilds"),
];

const disconnectedContent = [
    document.getElementById("devCenterLoginContent"),
    document.getElementById("devCenterBuildsLoginMessage"),
];

function resetInfoInStatus(){
    module.exports.status.connected = false;
    module.exports.status.error = null;
    module.exports.status.username = null;
    module.exports.status.email = null;
    module.exports.status.developer = false;
    module.exports.status.build_of_the_day = null;
    connectedDetails.innerText = "";
}

function resetTokenInSettings(){
    console.log("Clearing DevCenter access key");
    settings.devCenterKey = null;
    saveSettings();
}

// Updates the content blocks in the devcenter info popup based on if connection is good or not
function updateConnectionPopupVisibleItems(){
    if(module.exports.status.connected){
        connectedContent.forEach((value) => {
            value.style.display = "block";
        });
        disconnectedContent.forEach((value) => {
            value.style.display = "none";
        });
    } else {
        connectedContent.forEach((value) => {
            value.style.display = "none";
        });
        disconnectedContent.forEach((value) => {
            value.style.display = "block";
        });
    }
}

function updateCurrentUserDetails(){
    connectedDetails.innerText = "";

    connectedDetails.append(document.createTextNode("Connected as: " +
        module.exports.status.email + " (" + module.exports.status.username + ")"));

    connectedDetails.append(document.createElement("br"));

    if(module.exports.status.developer){
        connectedDetails.append(document.createTextNode("You are a developer"));
    } else {
        connectedDetails.append(document.createTextNode("Thank you for your support!"));
    }
}

function onNoDevCenterConnection(){
    statusLabel.innerText = "";
    linkMessage.innerText = "";
    openModalButton.innerText = noConnectionMessage;

    if(settings.devCenterKey){
        retryButton.style.display = "inline-block";
    } else {
        retryButton.style.display = "none";
    }

    resetInfoInStatus();
    updateConnectionPopupVisibleItems();
    sendExtraBuildTypes();
}

// Checks if we currently have a devcenter connection and it is valid
function checkConnectionStatus(){
    if(!settings.devCenterKey){
        onNoDevCenterConnection();
        return;
    }

    statusLabel.innerText = "Loading...";
    openModalButton.innerText = "";

    fetch(launcherCheckAPI, {
        headers: {
            Authorization: settings.devCenterKey,
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 200){
                // Failed
                const error = data.message ||
                    `Invalid response from server (${response.status})`;

                // Handle this way to not reset the token
                if(response.status >= 500)
                    throw error;

                // If we get here the server told us the token is not good
                onNoDevCenterConnection();

                module.exports.status.error = `${error}`;

                showGenericError(`Failed to connect to ThriveDevCenter: ${error}`, () => {
                    resetTokenInSettings();
                    onNoDevCenterConnection();
                });

                return null;
            }

            return data;
        }).catch((error) => {
            console.log("Failed to parse response:", error);
            console.log(response);
            throw error;
        });
    }).then((data) => {

        if(!data)
            return;

        resetInfoInStatus();
        module.exports.status.connected = true;
        module.exports.status.username = data.username;
        module.exports.status.email = data.email;
        module.exports.status.developer = data.developer;

        if(data.developer){
            statusLabel.innerText = "Howdy,";
        } else {
            statusLabel.innerText = "Thank you,";
        }

        openModalButton.innerText = module.exports.status.username ||
            module.exports.status.email;

        retryButton.style.display = "none";

        updateConnectionPopupVisibleItems();
        updateCurrentUserDetails();
        sendExtraBuildTypes();

    }).catch((error) => {

        onNoDevCenterConnection();

        module.exports.status.error = `${error}`;

        showGenericError(`Failed to connect to ThriveDevCenter: ${error}`);
    });
}

function formConnection(code){
    linkMessage.innerText = "Forming connection...";

    fetch(launcherFormConnectionAPI, {
        method: "post",
        headers: {
            Authorization: code,
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 201){
                throw data.message || `Invalid response from server (${response.status})`;
            }

            linkMessage.innerText = "Connection formed. Reading data...";
            return data;
        });
    }).then((data) => {
        // Code is now taken by us
        settings.devCenterKey = data.code;
        saveSettings();

        checkConnectionStatus();
    }).catch((error) => {
        linkMessage.innerText = `Error linking: ${error}`;
    });
}

function checkLinkCode(code){
    linkMessage.innerText = "Checking code...";
    linkStatusContainer.innerText = "";

    fetch(launcherTestTokenAPI, {
        headers: {
            Authorization: code,
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 200){
                throw data.message || `Invalid response from server (${response.status})`;
            }

            return data;
        });
    }).then((data) => {

        linkMessage.innerText = "Code is valid. Confirm link:";

        const ul = document.createElement("ul");

        for(const prop of ["username", "email"]){
            const item = document.createElement("li");
            item.append(document.createTextNode(`${prop}: ${data[prop]}`));
            ul.append(item);
        }

        linkStatusContainer.append(ul);

        const accept = document.createElement("span");
        accept.classList.add("BottomButton");
        accept.style.height = "30px";
        accept.innerText = "Looks good";

        accept.addEventListener("click", () => {
            linkStatusContainer.innerText = "";
            formConnection(code);
        });

        linkStatusContainer.append(accept);

        const decline = document.createElement("span");
        decline.classList.add("BottomButton");
        decline.style.height = "30px";
        decline.style.marginLeft = "4px";
        decline.innerText = "Cancel";

        decline.addEventListener("click", () => {
            linkMessage.innerText = "";
            linkStatusContainer.innerText = "";
        });

        linkStatusContainer.append(decline);

    }).catch((error) => {
        linkMessage.innerText = `Error checking code: ${error}`;
    });
}

function disconnect(){
    disconnectInfo.innerText = "Disconnecting...";

    fetch(launcherCheckAPI, {
        method: "delete",
        headers: {
            Authorization: settings.devCenterKey,
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 200){
                throw data.message || `Invalid response from server (${response.status})`;
            }

            return data;
        });
    }).then((data) => {
        if(!data.success)
            throw "Reply success was false";

        resetInfoInStatus();
        onNoDevCenterConnection();
        updateConnectionPopupVisibleItems();

        settings.devCenterKey = null;
        saveSettings();

    }).catch((error) => {
        module.exports.status.error = `${error}`;
        showGenericError(`Failed to disconnect: ${error}`);
    });
}

// Send the extra build types to the version list object
function sendExtraBuildTypes(){

}

// Init our callbacks
openModalButton.addEventListener("click", (e) => {
    e.preventDefault();
    devCenterModal.show();
});

loginButton.addEventListener("click", () => {
    if(!loginCode.value){
        showGenericError("Link code is empty");
        return;
    }

    checkLinkCode(loginCode.value);
});

retryButton.addEventListener("click", () => {
    if(!settings.devCenterKey){
        showGenericError("No previous connection code / it has been cleared.");
        return;
    }

    checkConnectionStatus();
});

disconnectButton.addEventListener("click", () => {
    disconnect();
});

$("#devCenterTabs").tabs();

module.exports.checkConnectionStatus = checkConnectionStatus;
