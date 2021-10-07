// Handles playing Thrive
// TODO: this is now the hugest file, perhaps this could be chopped up?
"use strict";

const log = require("electron-log");
const remote = require("@electron/remote");

const {assert} = require("./utils");
const fs = remote.require("fs");
const path = require("path");
const mkdirp = remote.require("mkdirp");
const rimraf = remote.require("rimraf");
const nodeURL = require("url");

const {settings, tmpDLFolder, getDevBuildFolder} = require("./settings.js");
const {showUnpackMessages, devBuildCacheName} = require("./config");
const {Modal} = require("./modal");
const {onGameEnded} = require("./crash_reporting.js");
const errorSuggestions = require("./error_suggestions");
const {Progress} = require("./progress");
const {unpackRelease} = require("./unpack");
const {getSelectedVersion, storeVersionObject} = require("./version_select_button");
const {downloadFile, verifyDLHash} = require("./download_helper");
const versionInfo = require("./version_info");
const {findFirstSubFolder} = require("./file_utils");
const {
    getCurrentDevBuildType, getDevBuildPlatform, fetchDevBuildInfo,
    getDownloadForBuild, getCurrentDevBuildVersion,
} = require("./dev_center");
const {devBuildIdentifier, storeBuildIdentifier} = require("./version_select_button");
const {runThrive} = require("./thrive_runner");

const playBox = document.getElementById("playModalContent");
const unsafePlayAnyway = document.getElementById("unsafePlayAnyway");

let playModalQuitDLCancel = null;
let currentDLCanceled = false;

const playModal = new Modal("playModal", "playModalDialog", {
    autoClose: false,
    closeButton: "playModalClose",
    onClose: function(){

        // Cancel download //
        if(playModalQuitDLCancel){

            currentDLCanceled = true;
            playModalQuitDLCancel.abort();
            playModalQuitDLCancel = null;
        }

        // Prevent closing //
        // return true;
    },
});

const unsafeModal = new Modal("tryingToPlayUnsafeDevBuildModal",
    "tryingToPlayUnsafeDevBuildModalDialog", {
        autoClose: true,
        closeButton: ["tryingToPlayUnsafeCross", "closeTryingToPlayUnsafe"],
    });


// Runs the game after the game folder is ready
function onThriveFolderReady(version, download){
    let installFolder = null;

    if(version.devbuild){
        installFolder = path.join(getDevBuildFolder(), "build");
    } else if(version.store){
        installFolder = path.join(remote.app.getAppPath(), download.folderName);
    } else {
        installFolder = path.join(settings.installPath, download.folderName);
    }

    const status = document.getElementById("playingInternalP");

    if(!fs.existsSync(installFolder)){
        status.innerText = "Error, required folder doesn't exist: " + installFolder;
        return;
    }

    runThrive(installFolder, status, () => {
        playModal.hide();
    }, (bin, signal, closeContainer) => {
        onGameEnded(bin, signal, closeContainer, version.releaseNum);
    });
}

function dlHelperUnPack(status, localTarget, version, download, fileName,
    customUnzipMove = null, installFolder = settings.installPath){

    removeDLProgress();

    // Hash is verified before unpacking //
    status.textContent = "Verifying archive '" + fileName + "'";
    const element = document.createElement("p");
    element.id = "playHashProgress";
    status.append(element);

    // Unpack archive //
    const verify = verifyDLHash(version, download, localTarget).catch(() => {
        // Fail //
        status.textContent = "Hash for file '" + fileName + "' is invalid " +
            "(download corrupted or wrong file was downloaded) please try again";
    });

    verify.then(() => {
        // Hash is correct //
        status.innerHTML = "";
        status.textContent = "Unpacking archive '" + fileName +
            "'";

        status.append(document.createElement("br"));

        status.append(document.createTextNode("To  '" + settings.installPath + "'"));

        status.append(document.createElement("br"));

        status.append(document.createTextNode("This may take several minutes to " +
            "complete, please be patient."));

        let unpackProgress = null;

        if(showUnpackMessages){

            unpackProgress = document.createElement("div");
            unpackProgress.classList.add("UnpackProgressLog");

            status.append(unpackProgress);
        }

        console.log("beginning unpacking");

        return unpackRelease(installFolder, download.folderName, localTarget,
            unpackProgress);
    }).then(() => {
        // This is the top level unzip target
        const created = path.join(installFolder, download.folderName);
        assert(fs.existsSync(created));

        // Custom rename
        if(customUnzipMove){
            // The rename requires there to be another folder within the unpack folder

            const finalPath = path.join(installFolder, customUnzipMove);
            const source = findFirstSubFolder(created);

            if(!source){
                throw new Error("The unpacked folder doesn't contain any folders for" +
                    " move");
            }

            assert(fs.existsSync(source));

            return new Promise((resolve, reject) => {
                // First delete existing
                rimraf(finalPath, (error) => {
                    if(error){
                        console.log("rimraf on existing file to rename over failed:", error);
                    }

                    // And then rename
                    fs.rename(source, finalPath,
                        (error) => {

                            if(error){
                                reject(new Error("Failed to rename extracted file: " + error));
                                return;
                            }

                            // Delete the folder to not leave it over, don't need error
                            // checking on this
                            rimraf(created, () => {
                            });

                            resolve();
                        });
                });
            });
        }

        return true;

    }).then(() => {

        console.log("unpacking completed");
        onThriveFolderReady(version, download);
    }).catch((error) => {
        // Fail //
        status.textContent = "Unpacking failed, File '" + fileName +
            "' is invalid? " + error;

        status.append(document.createElement("br"));

        // Auto detect solutions //
        errorSuggestions.unpackError("" + error, status);

        status.append(document.createElement("br"));

        status.append(document.createTextNode("To try redownloading delete '" +
            localTarget + "'"));
    });
}

function removeDLProgress(){
    try{
        $("#dlProgress").remove();
    } catch(error){
        console.log("couldn't remove dl progress, was probably gone already");
    }
}

// Called once a file has been downloaded (or existed) and startup should continue
function onDLFileReady(version, download, fileName){
    removeDLProgress();

    const localTarget = path.join(tmpDLFolder, fileName);

    assert(fs.existsSync(localTarget));

    const status = document.getElementById("playingInternalP");

    // Destroy the download progress indicator
    status.innerHTML = "";

    const mkdir = mkdirp(settings.installPath);

    // If unpacked already launch Thrive //
    mkdir.catch((err) => {
        console.error(err);
        alert("failed to create install directory");
    });

    mkdir.then(function(){
        // Check does it exist //
        if(fs.existsSync(path.join(settings.installPath, download.folderName))){
            console.log("archive has already been extracted");
            onThriveFolderReady(version, download);
            return;
        }

        // Need to unpack //
        dlHelperUnPack(status, localTarget, version, download, fileName);
    });
}

async function downloadTheGame(url, localTarget, status){

    const mkdir = mkdirp(tmpDLFolder);

    mkdir.catch((err) => {
        console.error(err);
        alert("failed to create dl directory");
    });

    const op = mkdir.then(() => {
        const downloadProgress = Progress("download");
        downloadProgress.render(status);

        const dataObj = {
            remoteFile: url,
            localFile: localTarget,

            onProgress: function(received){
                downloadProgress.value = received;
            },
        };

        const promise = downloadFile(dataObj);

        // This object is for aborting a download
        assert(dataObj.reqObj);

        playModalQuitDLCancel = dataObj.reqObj;

        return promise;

    }).then((contentType) => {
        if(currentDLCanceled){
            // It was canceled //
            console.log("dl was canceled by user");

            if(fs.existsSync(localTarget))
                fs.unlinkSync(localTarget);

            return;
        }

        if(![
            "application/x-7z-compressed",
            "application/zip",
            "application/octet-stream",

            // This seems to happen when downloading devbuilds
            "application/x-www-form-urlencoded",
        ].includes(contentType)){
            throw "download type is wrong: " + contentType;
        }

        console.log("Successfully downloaded");

        // No longer need to cancel
        playModalQuitDLCancel = null;
    });

    op.catch((error) => {
        if(fs.existsSync(localTarget)){

            fs.unlinkSync(localTarget);
        }

        if(status){
            status.textContent = "Download Failed! " + error;
        }
    });

    return op;
}

// Once devbuild URL is figured out this determines if it needs to be downloaded and then
// goes on to the game start function
async function onDevBuildURLReceived(url, hash){
    const cacheFile = path.join(getDevBuildFolder(), devBuildCacheName);

    // This is used to determine if we have already downloaded something despite the tokens
    // changing
    const fileUrl = url.substr(0, url.indexOf("?"));

    const parsedUrl = nodeURL.parse(url);
    const fileName = path.basename(parsedUrl.pathname);

    const ext = path.extname(fileName);

    const version = getCurrentDevBuildVersion();
    const download = {
        devbuild: true,
        folderName: path.basename(fileName, ext),
        hash: hash,
    };

    fs.readFile(cacheFile, (error, data) => {
        let cache = {};

        if(!error){
            cache = JSON.parse(data);
        }

        if(cache.unpackedBuild === fileUrl &&
            fs.existsSync(path.join(getDevBuildFolder(), "build"))){

            console.log("Currently selected devbuild is already unpacked");
            removeDLProgress();
            onThriveFolderReady(version, download);
        } else {
            const localTarget = path.join(tmpDLFolder, "devbuild.7z");

            if(fs.existsSync(localTarget)){
                fs.unlinkSync(localTarget);
            }

            const status = document.getElementById("dlProgress");

            downloadTheGame(url, localTarget, status).then(() => {

                const status = document.getElementById("playingInternalP");
                status.textContent = "Successfully downloaded devbuild";

                dlHelperUnPack(status, localTarget, version, download, "devbuild.7z",
                    "build", getDevBuildFolder());

                cache.unpackedBuild = fileUrl;

                fs.writeFile(cacheFile, JSON.stringify(cache), (error) => {
                    if(error)
                        console.log("failed to write devbuild cache file");
                });
            });
        }
    });
}

function playDevBuild(){
    playBox.innerHTML = "<div>Playing Thrive DevBuild (" + getCurrentDevBuildType() +
        ") for " + getDevBuildPlatform() + "</div>" +
        "<div id='playingDevBuildInfo'>Retrieving build info... </div>" +
        "<div id='dlProgress'></div><div id='playingInternalP'></div>";

    const buildInfo = document.getElementById("playingDevBuildInfo");
    const status = document.getElementById("playingInternalP");

    const build = fetchDevBuildInfo().then((build) => {
        if(!build){
            throw "Could not find build information.";
        }

        console.log("received build info:", build);

        // Make build info visible
        let info = `Found build: ${build.id} hash: ${build.build_hash}. ` +
            `BOTD: ${build.build_of_the_day}, branch: ${build.branch}`;

        if(build.description){
            info += ", description: ";
        }

        buildInfo.innerText = info;

        if(build.description){
            const description = document.createElement("pre");
            description.innerText = build.description;
            buildInfo.append(description);
        }

        status.innerText = "Fetching download for build...";

        return new Promise((resolve, reject) => {
            if(!build.verified && build.anonymous){
                unsafeModal.show();

                const dataHolder = {
                    allowed: false,
                };

                unsafeModal.onClose = () => {
                    if(!dataHolder.allowed){
                        reject(new Error("Not playing an unsafe build"));
                    }

                    unsafeModal.onClose = null;
                    return false;
                };

                unsafePlayAnyway.addEventListener("click", () => {
                    console.log("Playing an unsafe build anyway:", build);
                    dataHolder.allowed = true;
                    unsafeModal.hide();
                    resolve(build);
                }, {once: true});
            } else {
                resolve(build);
            }
        });
    }).then((build) => {
        return getDownloadForBuild(build);
    });

    build.catch((error) => {
        status.innerText = "Error retrieving build information: " + error;
    });

    build.then((download) => {
        return mkdirp(getDevBuildFolder()).then(() => {
            return download;
        });
    });

    build.then((download) => {
        status.innerText = "Downloading: " +
            download.download_url.substr(0, download.download_url.indexOf("?"));

        return onDevBuildURLReceived(download.download_url, download.dl_hash);

    }).catch((error) => {
        status.innerText = "Error downloading build: " + error;
    });
}

function playNormalVersion(version, download){

    playBox.innerHTML = "<div>Playing Thrive " + version.releaseNum + "</div>" +
        "<div id='dlProgress'></div><div id='playingInternalP'>Downloading: " + download.url +
        "</div>";

    const fileName = download.fileName;

    assert(fileName);

    if(fs.existsSync(path.join(settings.installPath, download.folderName))){

        console.log("archive has already been extracted (assumed)");
        onThriveFolderReady(version, download);
        return;
    }

    const localTarget = path.join(tmpDLFolder, fileName);

    if(fs.existsSync(localTarget)){

        console.log("already exists: " + fileName);
        onDLFileReady(version, download, fileName);
        return;
    }

    const status = document.getElementById("dlProgress");

    downloadTheGame(download.url, localTarget, status).then(() => {

        const status = document.getElementById("playingInternalP");
        status.textContent = "Successfully downloaded " + version.releaseNum;

        onDLFileReady(version, download, fileName);
    });
}

function playStoreVersion(){

    playBox.innerHTML =
        "<div>Playing bundled Thrive version</div><div id='playingInternalP'></div>";

    onThriveFolderReady(storeVersionObject.version, storeVersionObject.download);
}

// Called when the play button is pressed
function playPressed(){
    // Cannot be downloading already //
    assert(playModalQuitDLCancel == null);
    currentDLCanceled = false;

    // Open play modal thing
    playModal.show();

    const {id, os} = getSelectedVersion();

    // Due to dataset use the ids are strings
    // noinspection EqualityComparisonWithCoercionJS
    if(id == devBuildIdentifier && os == devBuildIdentifier){
        log.log("Playing devbuild");
        playDevBuild();

        // Due to dataset use the ids are strings
        // noinspection EqualityComparisonWithCoercionJS
    } else if(id == storeBuildIdentifier && os == storeBuildIdentifier){
        log.log("Playing store build");
        playStoreVersion();
    } else {
        const version = versionInfo.getVersionByID(id);

        const download = versionInfo.getDownloadByOSID(version.id, os);

        assert(download);

        console.log("Playing thrive version: " + version.getDescriptionString() + " " +
            download.getDescriptionString());

        playNormalVersion(version, download);
    }
}

exports.playPressed = playPressed;
