// Everything for handling the devcenter connection and devbuild fetching
"use strict";

const url = require("url");
const {getCurrentPlatform} = require("./version_info");

const {devCenterURL} = require("./config");
const {settings, saveSettings} = require("./settings");
const {Modal, showGenericError} = require("./modal");
const {setExtraVersions, devBuildIdentifier} = require("./version_select_button");

const noConnectionMessage = "Connect to DevCenter";


const launcherCheckAPI = url.resolve(devCenterURL, "/api/v1/launcher/status");
const launcherTestTokenAPI = url.resolve(devCenterURL, "/api/v1/launcher/check_link");
const launcherFormConnectionAPI = url.resolve(devCenterURL, "/api/v1/launcher/link");
const launcherFindAPI = url.resolve(devCenterURL, "/api/v1/launcher/find");
const launcherBuildsListAPI = url.resolve(devCenterURL, "/api/v1/launcher/builds");
const launcherSearchAPI = url.resolve(devCenterURL, "/api/v1/launcher/search");
const launcherDownloadBuildAPI = url.resolve(devCenterURL,
    "/api/v1/launcher/builds/download/");
const launcherDownloadDehydratedAPI = url.resolve(devCenterURL,
    "/api/v1/launcher/dehydrated/download");

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
const buildTypeBOTD = document.getElementById("devcenterBuildTypeBOTD");
const buildTypeLatest = document.getElementById("devcenterBuildTypeLatest");
const buildTypeManual = document.getElementById("devcenterBuildTypeManual");
const selectedBuildHash = document.getElementById("selectedBuildHash");
const manuallyEnteredHash = document.getElementById("manuallyEnteredHash");
const selectManualInputHash = document.getElementById("selectManualInputHash");
const latestDevBuildsList = document.getElementById("latestDevBuildsList");
const refreshLatestBuilds = document.getElementById("refreshLatestBuilds");

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

        let extraMessage = "";

        if(module.exports.status.error.includes("TypeError: Failed to fetch")){
            extraMessage = " (connection error)";
        }

        showGenericError(`Failed to connect to ThriveDevCenter: ${error}` +
            extraMessage);
    });
}

function formConnection(code){
    linkMessage.innerText = "Forming connection...";

    fetch(launcherFormConnectionAPI, {
        method: "post",
        body: JSON.stringify({
            code: code,
        }),
        headers: {
            "Content-Type": "application/json",
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
        method: "post",
        body: JSON.stringify({
            code: code,
        }),
        headers: {
            "Content-Type": "application/json",
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

async function getCurrentDevBuild(){
    if(settings.selectedDevBuildType === "botd" || settings.selectedDevBuildType === null){
        return "aa";
    } else if(settings.selectedDevBuildType === "latest"){
        return "bb";
    } else if(settings.selectedDevBuildType === "manual"){
        return "cc";
    } else {
        throw "Unknown devbuild type";
    }
}

function getDevBuildPlatform(){
    const platform = getCurrentPlatform();

    if(platform.os === "win32" && platform.arch === "x64"){
        return "Windows Desktop";
    } else if(platform.os === "win32" && platform.arch === "ia32"){
        return "Windows Desktop (32-bit)";
    } else if(platform.os === "linux" && platform.arch === "x64"){
        return "Linux/X11";
    } else {
        return `Unknown platform for devbuilds: ${platform.os} arch: ${platform.arch} `;
    }
}

function getCurrentDevBuildVersion(){
    return {
        devbuild: true,
        getDevBuildInfo: getCurrentDevBuild,
        version: {
            id: devBuildIdentifier,
            getDescriptionString: () => "DevBuild",
        },
        download: {
            os: devBuildIdentifier,
            getDescriptionString: () => "",
            folderName: "devbuild",
        },
    };
}

// Returns a type string for the currently selected devbuild type for display in the play popup
function getCurrentDevBuildType(){
    if(settings.selectedDevBuildType === "botd" || settings.selectedDevBuildType === null){
        return "botd";
    } else if(settings.selectedDevBuildType === "latest"){
        return "latest";
    } else if(settings.selectedDevBuildType === "manual"){
        return "manual (" + settings.manuallySelectedBuildHash + ")";
    }

    throw "Unknown devbuild type";
}

async function queryFindAPI(type){
    return fetch(launcherFindAPI, {
        method: "post",
        body: JSON.stringify({
            type: type,
            platform: getDevBuildPlatform(),
        }),
        headers: {
            Authorization: settings.devCenterKey,
            "Content-Type": "application/json",
        },
        credentials: "omit",
    }).then((response) => {
        return response.json().then((data) => {
            if(response.status !== 200){
                throw data.message || `Invalid response from server (${response.status})`;
            }

            return data;
        });
    });
}

async function querySearchAPI(hash){
    return fetch(launcherSearchAPI, {
        method: "post",
        body: JSON.stringify({
            devbuild_hash: hash,
            platform: getDevBuildPlatform(),
        }),
        headers: {
            Authorization: settings.devCenterKey,
            "Content-Type": "application/json",
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
        if(data.result.length < 1)
            throw "No builds found for current platform with a matching hash";

        // Find the most promising entry
        let mostPromising = null;

        for(const entry of data.result){
            if(mostPromising === null){
                mostPromising = entry;
                continue;
            }

            if(mostPromising.anonymous && !entry.anonymous){
                mostPromising = entry;
                continue;
            }

            if(!mostPromising.verified && entry.verified){
                mostPromising = entry;
            }
        }

        if(mostPromising)
            return mostPromising;

        return data.result[0];
    });
}

// Retrieves info from the devcenter about the build we want to play
async function fetchDevBuildInfo(){
    if(settings.selectedDevBuildType === "botd" || settings.selectedDevBuildType === null){
        return queryFindAPI("botd");
    } else if(settings.selectedDevBuildType === "latest"){
        return queryFindAPI("latest");
    } else if(settings.selectedDevBuildType === "manual"){
        if(!settings.manuallySelectedBuildHash)
            throw "No manually selected hash found. Please set it and try again.";

        return querySearchAPI(settings.manuallySelectedBuildHash);
    } else {
        throw "Unknown selected build type";
    }
}

// Gets download info for a build. Returns an object of download_url and dl_hash
async function getDownloadForBuild(build){
    return fetch(launcherDownloadBuildAPI + build.id, {
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
    });
}

// Gets download urls for dehydrated objects based on on their hashes
// Returns an array of objects with download_url properties
async function getDownloadForDehydrated(objectHashes){
    return fetch(launcherDownloadDehydratedAPI, {
        method: "post",
        body: JSON.stringify({
            objects: objectHashes.map((i) => {
                return {sha3: i};
            }),
        }),
        headers: {
            Authorization: settings.devCenterKey,
            "Content-Type": "application/json",
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
        if(!data.downloads || data.downloads.length < 1)
            throw "No downloads found for the objects";

        return data.downloads;
    });
}

// Queries the devcenter for the latest builds
function fetchLatestBuilds(offset = 0){
    latestDevBuildsList.innerText = "Fetching latest builds...";

    return fetch(launcherBuildsListAPI, {
        method: "post",
        body: JSON.stringify({
            platform: getDevBuildPlatform(),
            offset: offset,
            page_size: 75,
        }),
        headers: {
            Authorization: settings.devCenterKey,
            "Content-Type": "application/json",
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
        if(!data.result || data.result.length < 1){
            latestDevBuildsList.innerText = "No builds found";
            return;
        }

        if(data.next_offset){
            console.log("Next offset to fetch more builds:", data.next_offset);

            // TODO: handle pagination
        }

        latestDevBuildsList.innerHTML = "";

        const list = document.createElement("ul");

        for(const build of data.result){
            const item = document.createElement("li");

            const link = document.createElement("a");
            link.classList.add("DevBuildLink");
            link.innerText = build.build_hash;
            link.addEventListener("click", () => {
                setSelectedSpecificHash(build.build_hash);
            });

            item.append(link);

            const unsafe = !build.verified && build.anonymous ? "unsafe " : "";
            const BODT = build.build_of_the_day ? "BODT " : "";
            const description = build.description ? "desc: " +
                build.description.substring(0, 80) : "";

            const infoText = document.createTextNode(` (${build.id}, ${build.branch}) ` +
                `${unsafe}${BODT}` + description);

            item.append(infoText);
            list.append(item);
        }

        latestDevBuildsList.append(list);

    }).catch((error) => {
        latestDevBuildsList.innerText = "Error: " + error;
    });
}

// Send the extra build types to the version list object
function sendExtraBuildTypes(){

    if(!module.exports.status.connected){
        setExtraVersions([]);
        return;
    }

    setExtraVersions([getCurrentDevBuildVersion()]);
}

let applyingSettings = false;

function updateDevBuildTypeFromSettings(){
    applyingSettings = true;

    buildTypeBOTD.checked = false;
    buildTypeLatest.checked = false;
    buildTypeManual.checked = false;

    if(settings.selectedDevBuildType === "botd" || settings.selectedDevBuildType === null){
        buildTypeBOTD.checked = true;
    } else if(settings.selectedDevBuildType === "latest"){
        buildTypeLatest.checked = true;
    } else if(settings.selectedDevBuildType === "manual"){
        buildTypeManual.checked = true;
    }

    applyingSettings = false;
}

function onSelectedDevBuildTypeChanged(){
    if(applyingSettings)
        return;

    let newValue = null;

    if(buildTypeLatest.checked){
        newValue = "latest";
    } else if(buildTypeManual.checked){
        newValue = "manual";
    } else {
        // Default option if nothing is selected for some reason
        newValue = "botd";
    }

    if(settings.selectedDevBuildType !== newValue){
        settings.selectedDevBuildType = newValue;
        saveSettings();
    }
}

function setSelectedSpecificHash(value){
    if(value === settings.manuallySelectedBuildHash)
        return;

    if(value){
        settings.manuallySelectedBuildHash = value;
    } else {
        settings.manuallySelectedBuildHash = null;
    }

    console.log("Specifically selected hash is now:", settings.manuallySelectedBuildHash);

    updateSpecificallySelectedDevBuildHash();
    saveSettings();
}

function updateSpecificallySelectedDevBuildHash(){
    selectedBuildHash.innerText = settings.manuallySelectedBuildHash || "none";
}

buildTypeBOTD.addEventListener("change", onSelectedDevBuildTypeChanged);
buildTypeLatest.addEventListener("change", onSelectedDevBuildTypeChanged);
buildTypeManual.addEventListener("change", onSelectedDevBuildTypeChanged);

// Init our callbacks
openModalButton.addEventListener("click", (e) => {
    e.preventDefault();
    devCenterModal.show();
    updateSpecificallySelectedDevBuildHash();
    updateDevBuildTypeFromSettings();
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

selectManualInputHash.addEventListener("click", () => {
    setSelectedSpecificHash(manuallyEnteredHash.value);
});

refreshLatestBuilds.addEventListener("click", () => {
    fetchLatestBuilds();
});

$("#devCenterTabs").tabs();

module.exports.checkConnectionStatus = checkConnectionStatus;
module.exports.getCurrentDevBuildType = getCurrentDevBuildVersion;
module.exports.getDevBuildPlatform = getDevBuildPlatform;
module.exports.getCurrentDevBuildType = getCurrentDevBuildType;
module.exports.fetchDevBuildInfo = fetchDevBuildInfo;
module.exports.getDownloadForBuild = getDownloadForBuild;
module.exports.getCurrentDevBuildVersion = getCurrentDevBuildVersion;
module.exports.getDownloadForDehydrated = getDownloadForDehydrated;
