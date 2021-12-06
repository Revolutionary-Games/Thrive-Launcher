// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const log = require("electron-log");

const {assert} = require("./utils");

const versionInfo = require("./version_info");
const retrieveNews = require("./retrieve_news");

const {Modal, showGenericError} = require("./modal");
const autoUpdateHandler = require("./auto_update_handler");
const {checkConnectionStatus} = require("./dev_center");
const {sendVersionInfoToPlayButton, playCallback, setStoreVersionAsSelected} =
    require("./version_select_button");
const {checkIfCompatible, performCompatibilityCheck} =
    require("./compatibility_check");
const {getLauncherKey} = require("./launcher_key");
const {loadVersionData} = require("./version_info_retriever");
const {playPressed} = require("./play_handler");
const {catchErrors} = require("./config");
const {storeInfo, applyHiddenElements, showThanksMessage} = require("./store_handler");
const {setLDPreload} = require("./thrive_runner");

const openpgp = require("openpgp");

if(catchErrors){
    log.catchErrors();
}

const titleBar = require("./title_bar");
titleBar.loadTitleBar();

log.info("Renderer.js script started");

autoUpdateHandler();

const parsedUrl = new URL(document.URL);
storeInfo.isStoreVersion = parsedUrl.searchParams.get("isStoreVersion") === "true";
storeInfo.store = parsedUrl.searchParams.get("store");

log.debug("Renderer detected store params:", storeInfo.isStoreVersion, storeInfo.store);
applyHiddenElements();

setLDPreload(parsedUrl.searchParams.get("ldPreload"));

//
// Settings thing
//
const {
    settings, loadSettings, setIgnoreAutoStart, isAutoStartEnabled,
} = require("./settings.js");

const {reportLatestVersion, loadSelectedVersion} = require("./remembered_version");
const {checkLauncherVersion} = require("./check_launcher_version");

// This loads settings in sync mode here
loadSettings();

// Start checking DevCenter token
checkConnectionStatus();

// Start checking hardware
checkIfCompatible();

setIgnoreAutoStart(parsedUrl.searchParams.get("ignoreAutoStart") === "true");

// Some other variables

const linksModal = new Modal("linksModal", "linksModalDialog", {closeButton: "linksClose"});

const constOldVersionErrorModal = new Modal("errorOldCantStartModal",
    "errorOldCantStartModalDialog", {autoClose: false});

// Parses version information from data and adds it to all the places
function onVersionDataReceived(data, unsigned = false){
    // Check launcher version //
    new Promise(function(resolve, reject){
        if(unsigned){
            versionInfo.parseData(data);
            resolve();
            return;
        }

        getLauncherKey().then((key) => {
            if(!key){

                reject(new Error("get launcher key is null"));
                return;
            }

            // Unpack and verify signature //
            openpgp.readCleartextMessage({cleartextMessage: data}).then((message) => {

                const options = {
                    message: message,
                    verificationKeys: key,
                };

                openpgp.verify(options).then((verified) => {

                    verified.signatures[0].verified.then(function(validity){
                        if(validity){
                            console.log("Version data signed by key id " +
                                verified.signatures[0].keyID.toHex());

                            versionInfo.parseData(verified.data);

                            resolve();

                        } else {
                            const msg = "Error verifying signature validity. " +
                                "Did the download get corrupted?";
                            showGenericError(msg, () => {

                                reject(msg);
                            });
                        }
                    });
                });

            }, (err) => {
                reject(err);
            });
        }, (err) => {
            reject(err);
        });
    }).then(() => {
        return checkLauncherVersion(versionInfo);
    }).then(() => {

        loadSelectedVersion();

        const latest = versionInfo.getRecommendedVersion();

        if(latest)
            reportLatestVersion(latest.id);

        sendVersionInfoToPlayButton(versionInfo);

        if(storeInfo.isStoreVersion && isAutoStartEnabled()){
            log.info("Auto starting store version");

            // Switch to the right version
            setStoreVersionAsSelected();

            // And act as if the user pressed the play button
            performCompatibilityCheck(playPressed);
        }

    }).catch((err) => {
        // Fail //
        constOldVersionErrorModal.show();

        log.error("Failed to load version info / problem while loading:", err);

        if(err){

            const text = document.getElementById("errorOldCantStartText");

            if(text){
                text.append(document.createElement("br"));
                text.append(document.createTextNode(" Error message: " + err));
            }
        }
    });
}

// Maybe this does something to the stuck downloading version info bug
// TODO: switch the callback to use a promise here
loadVersionData(onVersionDataReceived);

playCallback(() => {
    performCompatibilityCheck(playPressed);
});

const newsContent = document.getElementById("newsContent");

const devForumPosts = document.getElementById("devForumPosts");

//
// Starts loading the news and shows them once loaded
//
function loadNews(){

    retrieveNews.retrieveNews(function(news, devposts){

        assert(news);
        assert(devposts);

        if(news.error){

            newsContent.textContent = news.error;

        } else {

            assert(news.htmlNodes);

            newsContent.innerHTML = "";
            newsContent.append(news.htmlNodes);
        }

        if(devposts.error){

            devForumPosts.textContent = devposts.error;

        } else {

            assert(devposts.htmlNodes);

            devForumPosts.innerHTML = "";
            devForumPosts.append(devposts.htmlNodes);
        }

    });
}

// Clear news and start loading them

if(settings.fetchNewsFromWeb){

    newsContent.textContent = "Loading...";
    devForumPosts.textContent = "Loading...";

    loadNews();

} else {

    newsContent.textContent = "Web content is disabled.";
    devForumPosts.textContent = "Web content is disabled.";
}


// Links button
const linksButton = document.getElementById("linksButton");

linksButton.addEventListener("click", function(){

    linksModal.show();
});

// Settings dialog
require("./settings_dialog.js");

if(storeInfo.isStoreVersion){
    showThanksMessage();
}
