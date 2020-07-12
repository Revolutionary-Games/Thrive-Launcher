// Handles playing Thrive
// TODO: this is now the hugest file, perhaps this could be chopped up?
"use strict";

const remote = require("electron").remote;

const win = remote.getCurrentWindow();

const assert = require("assert");
const fs = remote.require("fs");
const path = require("path");
const mkdirp = remote.require("mkdirp");
const os = remote.require("os");
const child_process = remote.require("child_process");

const {settings, tmpDLFolder} = require("../settings.js");
const {showUnpackMessages} = require("./config");
const {Modal} = require("../modal");
const {onGameEnded} = require("./crash_reporting.js");
const errorSuggestions = require("./error_suggestions");
const {Progress} = require("./progress");
const {unpackRelease, findBinInRelease} = require("./unpack");
const {getSelectedVersion} = require("./version_select_button");
const {downloadFile, verifyDLHash} = require("./download_helper");
const versionInfo = require("../version_info");


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


function onThriveFolderReady(version, download){

    const installFolder = path.join(settings.installPath, download.folderName);

    assert(fs.existsSync(installFolder));

    const status = document.getElementById("playingInternalP");

    // Destroy the download progress indicator
    status.innerHTML = "";

    status.textContent = "preparing to launch";

    // Find bin folder //
    const binFolder = findBinInRelease(installFolder);

    if(!fs.existsSync(binFolder)){

        status.textContent = "Error 'bin' folder is missing! To redownload delete " +
            installFolder;
        return;
    }

    // Check that executable is there //
    let exename = null;

    if(os.platform() === "win32"){

        exename = "Thrive.exe";

    } else {

        exename = "Thrive";
    }

    if(!fs.existsSync(path.join(binFolder, exename))){

        status.textContent = "Error: Thrive executable is missing! To redownload delete " +
            installFolder;
        return;
    }

    status.textContent = "launching...";

    const launchArgs = [];

    if(settings.launchOptionSingleProcess)
        launchArgs.push("--single-process");

    if(settings.launchOptionNoGUISandbox)
        launchArgs.push("--no-sandbox");

    if(settings.launchOptionNoGUIGPU)
        launchArgs.push("--disable-gpu");

    console.log("launching thrive from folder '" + binFolder + "' with arguments: ",
        launchArgs);

    // Cwd is where relative to things are installed
    const thrive = child_process.spawn(path.join(binFolder, exename),
        launchArgs,
        {cwd: binFolder});

    if(settings.hideLauncherOnPlay){
        win.minimize();
    }

    status.innerHTML = "";

    const titleSpan = document.createElement("span");

    const processOutput = document.createElement("div");

    processOutput.style.overflow = "auto";

    // This needs a fixed size for some reason
    processOutput.style.maxHeight = "410px";
    processOutput.style.height = "410px";
    processOutput.style.paddingTop = "5px";
    processOutput.style.width = "100%";

    status.style.marginBottom = "1px";

    titleSpan.textContent = "Thrive is running. Log output: ";

    titleSpan.append(processOutput);

    status.append(titleSpan);

    const appendMessage = (text) => {

        const message = document.createElement("div");
        message.textContent = text;
        processOutput.append(message);

        const container = $(processOutput);

        // Max number of messages
        while(container.children().length > 1000){

            // Remove elements //
            container.children().first().remove();
        }

        // For some reason the jquery thing is not working so this is at least a decent choice
        message.scrollIntoView(false);

        // Let modalContainer = $( playModal.dialog );

        // let check = $("#playModalDialog");

        // //assert(modalContainer == check);

        // check.scrollTop = check.scrollHeight;

        // // modalContainer.animate({
        // //     scrollTop: modalContainer.scrollHeight
        // // }, 500);
    };

    appendMessage("Process Started");

    thrive.stdout.on("data", (data) => {

        for(const line of data.toString().split(/\r?\n/g))
            appendMessage(line);
    });

    thrive.stderr.on("data", (data) => {

        appendMessage("ERROR: " + data);
    });

    thrive.on("exit", (code, signal) => {

        if(settings.hideLauncherOnPlay){
            win.show();
        }

        if(signal){
            console.log(`child process exited due to signal ${signal}`);
            appendMessage(`child process exited due to signal ${signal}`);

        } else {
            console.log(`child process exited with code ${code}`);
            appendMessage(`child process exited with code ${code}`);

            if(code === 0)
                appendMessage("Thrive has exited normally (exit code 0).");
        }

        const closeContainer = document.createElement("div");

        closeContainer.style.textAlign = "center";

        const close = document.createElement("div");

        close.textContent = "Close";

        close.className = "CloseButton";

        close.addEventListener("click", () => {

            console.log("extra close clicked");
            playModal.hide();
        });

        closeContainer.append(close);

        status.append(closeContainer);

        // Let crash reporter do things
        onGameEnded(binFolder, signal != null ? signal : code, closeContainer,
            version.releaseNum);
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
