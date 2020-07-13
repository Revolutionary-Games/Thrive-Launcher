// Handles playing Thrive
// TODO: this is now the hugest file, perhaps this could be chopped up?
"use strict";

const remote = require("electron").remote;

const assert = require("assert");
const fs = remote.require("fs");
const path = require("path");
const mkdirp = remote.require("mkdirp");


const {settings, tmpDLFolder} = require("../settings.js");
const {showUnpackMessages} = require("./config");
const {Modal} = require("../modal");
const {onGameEnded} = require("./crash_reporting.js");
const errorSuggestions = require("./error_suggestions");
const {Progress} = require("./progress");
const {unpackRelease} = require("./unpack");
const {getSelectedVersion} = require("./version_select_button");
const {downloadFile, verifyDLHash} = require("./download_helper");
const versionInfo = require("../version_info");
const {runThrive} = require("./thrive_runner");


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

// Runs the game after the game folder is ready
function onThriveFolderReady(version, download){
    const installFolder = path.join(settings.installPath, download.folderName);

    assert(fs.existsSync(installFolder));

    const status = document.getElementById("playingInternalP");

    runThrive(installFolder, status, () => {
        playModal.hide();
    }, (bin, signal, closeContainer) => {
        onGameEnded(bin, signal, closeContainer, version.releaseNum);
    });
}

function dlHelperUnPack(status, localTarget, version, download, fileName){
    // Hash is verified before unpacking //
    status.textContent = "Verifying archive '" + fileName + "'";
    const element = document.createElement("p");
    element.id = "playHashProgress";
    status.append(element);

    // Unpack archive //
    verifyDLHash(version, download, localTarget).catch(() => {
        // Fail //
        status.textContent = "Hash for file '" + fileName + "' is invalid " +
            "(download corrupted or wrong file was downloaded) please try again";
    }).then(() => {
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

        return unpackRelease(settings.installPath, download.folderName, localTarget,
            unpackProgress);
    }).then(() => {
        assert(fs.existsSync(path.join(settings.installPath, download.folderName)));
        console.log("unpacking completed");

        onThriveFolderReady(version, download);

    }).catch((error) => {
        // Fail //
        status.textContent = "Unpacking failed, File '" + fileName +
            "' is invalid? " + error;

        status.append(document.createElement("br"));

        // Auto detect solutions //
        errorSuggestions.unpackError(error, status);

        status.append(document.createElement("br"));

        status.append(document.createTextNode("To try redownloading delete '" +
            localTarget + "'"));
    });
}

// Called once a file has been downloaded (or existed) and startup should continue
function onDLFileReady(version, download, fileName){
    // Delete the download progress //
    $("#dlProgress").remove();

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

// Called when the play button is pressed
function playPressed(){
    // Cannot be downloading already //
    assert(playModalQuitDLCancel == null);
    currentDLCanceled = false;

    // Open play modal thing
    playModal.show();

    const {id, os} = getSelectedVersion();

    const version = versionInfo.getVersionByID(id);

    const download = versionInfo.getDownloadByOSID(version.id, os);

    assert(download);

    console.log("Playing thrive version: " + version.getDescriptionString() + " " +
        download.getDescriptionString());

    const playBox = document.getElementById("playModalContent");

    playBox.innerHTML = "Playing Thrive " + version.releaseNum +
        "<p id='playingInternalP'>Downloading: " + download.url +
        "</p><div id='dlProgress'></div>";

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
    const mkdir = mkdirp(tmpDLFolder);

    mkdir.catch((err) => {
        console.error(err);
        alert("failed to create dl directory");
    });

    mkdir.then(() => {
        const downloadProgress = Progress("download");
        downloadProgress.render(status);

        const dataObj = {
            remoteFile: download.url,
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
        ].includes(contentType)){
            throw "download type is wrong: " + contentType;
        }

        console.log("Successfully downloaded");

        // No longer need to cancel
        playModalQuitDLCancel = null;

        const status = document.getElementById("playingInternalP");
        status.textContent = "Successfully downloaded " + version.releaseNum;

        onDLFileReady(version, download, fileName);

    }).catch((error) => {
        if(fs.existsSync(localTarget)){

            fs.unlinkSync(localTarget);
        }

        if(status){
            status.textContent = "Download Failed! " + error;
        }
    });
}

exports.playPressed = playPressed;
