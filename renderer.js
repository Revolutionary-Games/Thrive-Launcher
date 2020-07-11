// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");
const assert = require("assert");
const request = require("request");
const mkdirp = remote.require("mkdirp");
const os = remote.require("os");
const child_process = remote.require("child_process");
const si = remote.require("systeminformation");
const semver = require("semver");

const sha3_256 = require("js-sha3").sha3_256;

const win = remote.getCurrentWindow();

const {
    showUnpackMessages, loadTestVersionData,
    checkGraphicsCard,
} = require("./src/config");

const versionInfo = require("./version_info");
const retrieveNews = require("./retrieve_news");
const errorSuggestions = require("./error_suggestions");
const {Modal, ComboBox, showGenericError} = require("./modal");
const {Progress} = require("./progress");
const {unpackRelease, findBinInRelease} = require("./unpack");
const {formatBytes} = require("./utils");
const autoUpdateHandler = require("./src/auto_update_handler");
const {checkConnectionStatus} = require("./src/dev_center");

const openpgp = require("openpgp");

// There's warnings that this could expose some server-only data to
// clients, but we don't have separate things so that shouldn't apply
const pjson = require("./package.json");

const {onGameEnded} = require("./crash_reporting.js");

const titleBar = require("./title_bar");
titleBar.loadTitleBar();

autoUpdateHandler();

//
// Settings thing
//
const {
    settings, loadSettings,
    tmpDLFolder, locallyCachedDLFile,
} = require("./settings.js");

// This loads settings in sync mode here
loadSettings();

// Start checking DevCenter token
checkConnectionStatus();

// Some other variables

// If true will only attempt reading the prepackaged version data
// Can be changed by user if no internet / download fails
let loadPrePackagedVersionData = false;

const cardsModel = [];

let showIncompatiblePopup = false;

let showHelpText = false;

const linksModal = new Modal("linksModal", "linksModalDialog", {closeButton: "linksClose"});

const constOldVersionErrorModal = new Modal("errorOldCantStartModal",
    "errorOldCantStartModalDialog", {autoClose: false});

const updateModal = new Modal("newReleaseAvailableModal",
    "newReleaseAvailableModalDialog", {
        autoClose: false,
        closeButton: "newReleaseClose",
    });

const versionDataFailedModal = new Modal("versionDataDownloadFailedModal",
    "versionDataDownloadFailedModalDialog", {autoClose: false});

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

const incompatibleModal = new Modal("incompatibleModal", "incompatibleModalDialog", {
    autoClose: true,
    closeButton: "incompatibleModalClose",
    onClose: function(){
        showIncompatiblePopup = false;
    },
});

// Use getLauncherKey instead
let launcherKey = null;

// Loads the key required for verifying version information //
function getLauncherKey(){

    return new Promise(function(resolve, reject){

        if(launcherKey){
            resolve(launcherKey);
        } else {
            fs.readFile(path.join(remote.app.getAppPath(), "version_data/launcher_key.pgp"),
                "utf8",
                function(err, data){

                    if(err){
                        const msg = "Can't read launcher version info signing key";

                        reject(new Error(msg + ". " + err));
                        console.log(err);
                        return;
                    }

                    openpgp.key.readArmored(data).then((key) => {

                        launcherKey = key.keys;

                        let keyid = null;

                        try{
                            keyid = launcherKey["0"].primaryKey.keyid.toHex();
                        } catch(err){
                            reject(new Error("Loaded signing key but it is invalid " +
                                "(property error): " + err));
                            return;
                        }

                        // Console.log("Key: " + launcherKey);
                        console.log("Signing key loaded: " + keyid);
                        resolve(launcherKey);

                    }, (err) => {
                        reject(new Error("Couldn't parse signing key: " + err));
                    });
                });
        }
    });
}


// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-
// save-it-and-show-download-progress (note: link split on two lines)
// With some modifications
function downloadFile(configuration){
    const downloadProgress = Progress("download");
    downloadProgress.formatter = formatBytes;

    return new Promise(function(resolve, reject){
        // Save variable to know progress
        let received_bytes = 0;
        let total_bytes = 0;

        const req = request({
            method: "GET",
            uri: configuration.remoteFile,
        });

        configuration.reqObj = req;

        const out = fs.createWriteStream(configuration.localFile, {encoding: null});

        // Req.pipe(out);

        let contentType = "unknown";

        req.on("response", function(data){
            // Change the total bytes value to get progress later.
            total_bytes = parseInt(data.headers["content-length"]);
            downloadProgress.max = total_bytes;

            contentType = data.headers["content-type"];
        });

        // Get progress if callback exists
        if(Object.prototype.hasOwnProperty.call(configuration, "onProgress")){
            req.on("data", function(chunk){
                // Update the received bytes
                received_bytes += chunk.length;

                configuration.onProgress(received_bytes, total_bytes);

                out.write(chunk);
            });
        } else {
            req.on("data", function(chunk){
                // Update the received bytes
                received_bytes += chunk.length;
                out.write(chunk);
            });
        }

        req.on("end", function(){
            out.end();
            resolve(contentType);
        });

        req.on("error", function(err){

            out.end();
            fs.unlinkSync(configuration.localFile);
            reject(err);

        });
    });
}

//! Parses version information from data and adds it to all the places
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
            openpgp.cleartext.readArmored(data).then((message) => {

                const options = {
                    message: message,
                    publicKeys: key,
                };

                openpgp.verify(options).then(function(verified){
                    const validity = verified.signatures[0].valid;

                    if(validity){
                        console.log("Version data signed by key id " +
                            verified.signatures[0].keyid.toHex());

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

            }, (err) => {
                reject(err);
            });


        }, (err) => {
            reject(err);
        });
    }).then(() => {
        return new Promise(function(resolve, reject){

            if(!versionInfo.getVersionData().versions){

                reject(new Error("No versions"));
                return;
            }

            const dlVersion = versionInfo.getLauncherMeta();

            if(semver.gt(dlVersion.latestVersion, pjson.version)){
                // Show update asking dialog //
                updateModal.show();
                updateModal.onClose = () => {

                    resolve();
                };

                const message = document.createElement("p");

                message.
                    append(document.createTextNode("You are using Thrive launcher version " +
                        pjson.version + " but the latest version is " +
                        dlVersion.latestVersion));

                const link = document.createElement("a");
                link.textContent = "Visit releases page";

                const urlTarget = dlVersion.releaseDLURL ||
                    "https://github.com/Revolutionary-Games/Thrive-Launcher/releases";
                link.href = urlTarget;

                message.append(document.createElement("br"));
                message.append(link);

                const textParent = $("#newReleaseAvailableText");

                textParent.empty();
                textParent.append($(message));

                // Buttons //
                const container = document.createElement("div");

                container.classList.add("UpdateButtonContainer");

                const dlnow = document.createElement("div");
                dlnow.classList.add("BottomButton");
                dlnow.style.fontSize = "3.4em";

                // Dlnow.textContent = "Download Now";
                dlnow.textContent = "Download Updated Launcher";

                container.append(dlnow);
                textParent.append($(container));


                dlnow.addEventListener("click", () => {

                    console.log("Clicked download now");
                    require("electron").shell.openExternal(urlTarget);
                    dlnow.textContent = "Opening link...";
                });

                return;
            }

            console.log("Version is latest or pre-release: " + pjson.version);
            resolve();
        });
    }).then(() => {

        updatePlayButton();

    }).catch((err) => {

        // Fail //
        constOldVersionErrorModal.show();

        if(err){

            const text = document.getElementById("errorOldCantStartText");

            if(text){
                text.append(document.createElement("br"));
                text.append(document.createTextNode(" Error message: " + err));
            }
        }
    });
}

// Buttons
const playButton = document.getElementById("playButton");

const playButtonText = document.getElementById("playText");

// Checks the graphics card
async function checkIfCompatible(){
    if(!checkGraphicsCard)
        return;

    try{
        playButtonText.textContent = "Checking graphics hardware...";

        const data = await si.graphics();
        const identifier = ["nvidia", "advanced micro devices", "amd"]; // And so on...

        for(let i = 0; i < data.controllers.length; i++){

            const vendor = data.controllers[i].vendor.toLowerCase();

            cardsModel.push(" " + data.controllers[i].model);

            // Is incompatible if Intel is found in a substring
            if(vendor.includes("intel")){

                console.log("hardware is not compatible");
                showIncompatiblePopup = true;
            }

            for(let n = 0; n < identifier.length; n++){
                if(vendor.includes(identifier[n])){
                    showHelpText = true;
                }
            }
        }

        console.log("finished checking the graphics hardware");

    } catch(err){
        console.log(err);
        showGenericError("Failed to check the graphics hardware: " + err);
    }
}

async function loadVersionData(){

    await checkIfCompatible();

    playButtonText.textContent = "Retrieving version information...";

    if(loadTestVersionData){

        fs.readFile(path.join(remote.app.getAppPath(), "version_data/thrive_versions.json"),
            "utf8",
            function(err, data){

                if(err){
                    const msg = "Failed to read test version data: " +
                        err;
                    showGenericError(msg);
                    console.log(msg);
                    return;
                }

                onVersionDataReceived(data, true);
            });
        return;
    }

    if(loadPrePackagedVersionData){

        // Load potentially very old data //
        fs.readFile(path.join(remote.app.getAppPath(), "version_data/signed_versions.json"),
            "utf8",
            function(err, data){

                if(err){
                    const msg = "Failed to read pre-packaged version data: " +
                        err;
                    showGenericError(msg);
                    console.log(msg);
                    return;
                }

                onVersionDataReceived(data);
            });
    } else {

        request({
            timeout: 10000,
            pool: null,
            uri: "https://raw.githubusercontent.com/Revolutionary-Games/Thrive-Launcher/" +
                "master/version_data/signed_versions.json",
            headers: {"User-Agent": "Thrive-Launcher " + pjson.version},
        }, function(error, response, body){

            if(error || !response || !body || response.statusCode != 200){

                // Unable to connect //
                // Construct an error message
                let message = "Unable to download Thrive version information. ";

                // Create a good error message //
                if(error){

                    message += error;

                } else if(response.statusCode != 200){

                    message += "File not found on server, status code: " +
                        response.statusCode;

                } else {

                    message += "Some other error occurred.";
                }

                console.log(message);

                versionDataFailedModal.show();

                const existsLocalFile = fs.existsSync(locallyCachedDLFile);

                $("#versionDataDownloadFailedText").text(message);

                const container = document.createElement("div");

                container.classList.add("UpdateButtonContainer");

                const dlnow = document.createElement("div");
                dlnow.classList.add("BottomButton");
                dlnow.style.fontSize = "1.7em";
                dlnow.style.marginRight = "5px";
                dlnow.textContent = "Retry";

                container.append(dlnow);

                dlnow.addEventListener("click", () => {

                    console.log("Clicked retry");
                    versionDataFailedModal.hide();

                    // Wait for animation to end //
                    setTimeout(() => {
                        loadVersionData();
                    }, 700);
                });

                if(existsLocalFile){

                    const useLocal = document.createElement("div");
                    useLocal.classList.add("BottomButton");
                    useLocal.style.fontSize = "1.7em";
                    useLocal.style.marginLeft = "5px";
                    useLocal.textContent = "Use Previous Version";

                    container.append(useLocal);

                    useLocal.addEventListener("click", () => {

                        console.log("Clicked use local file");

                        fs.readFile(locallyCachedDLFile,
                            "utf8",
                            function(err, data){

                                if(err){

                                    console.log(err);
                                    alert("locally cached file is missing, when " +
                                        "it shouldn't be? " + err);
                                    return;
                                }

                                onVersionDataReceived(data);
                            });

                        versionDataFailedModal.hide();
                    });
                } else {

                    const usePrepackaged = document.createElement("div");
                    usePrepackaged.classList.add("BottomButton");
                    usePrepackaged.style.fontSize = "3.4em";
                    usePrepackaged.style.marginLeft = "5px";
                    usePrepackaged.textContent = "Use Pre-packaged (old)";

                    container.append(usePrepackaged);

                    usePrepackaged.addEventListener("click", () => {

                        console.log("Clicked use prepackaged");
                        loadPrePackagedVersionData = true;
                        versionDataFailedModal.hide();

                        loadVersionData();
                    });
                }

                $("#versionDataDownloadFailedText").append($(container));

                return;
            }

            console.log("successfully downloaded version information");

            fs.writeFile(locallyCachedDLFile, body, (err) => {
                if(err){

                    console.error("Unable to locally save downloaded version info: " +
                        err);
                }
            });

            onVersionDataReceived(body);
        });
    }
}

// Maybe this does something to the stuck downloading version info bug
loadVersionData();

//! Verifies file hash
//! returns a promise that either cusseeds or fails once the check is done
function verifyDLHash(version, download, localTarget){

    return new Promise((resolve, reject) => {

        const totalSize = fs.statSync(localTarget).size;

        const hasher = sha3_256.create();

        const readable = fs.createReadStream(localTarget, {encoding: null});

        const status = document.getElementById("playHashProgress");

        readable.on("data", (chunk) => {

            const percentage = (readable.bytesRead * 100) / totalSize;

            if(status)
                status.textContent = "Progress " + percentage.toFixed(2) + "%";

            hasher.update(chunk);
        });

        readable.on("end", () => {

            const fileHash = hasher.hex();

            if(download.hash != fileHash){

                console.error("Hashes don't match! " + download.hash + " != " + fileHash);
                console.log("Deleting invalid file");

                fs.unlink(localTarget, (err) => {
                    if(err){
                        console.log("Unable to delete file at " + localTarget);
                        console.error(err);
                        return;
                    }

                    console.log("File at " + localTarget + " deleted.");
                });

                reject(new Error());
                return;
            }

            resolve();
        });

    });
}

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

    if(os.platform() == "win32"){

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

            if(code == 0)
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

//! Called once a file has been downloaded (or existed) and startup should continue
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

//! Called when the play button is pressed
function playPressed(){

    // Cannot be downloading already //
    assert(playModalQuitDLCancel == null);
    currentDLCanceled = false;

    // Open play modal thing
    playModal.show();

    assert(playButtonText.dataset.selectedID);

    const version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

    assert(version);

    const download = versionInfo.getDownloadByOSID(version.id,
        playButtonText.dataset.selectedDLOS);

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
            throw"download type is wrong: " + contentType;
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

playButtonText.addEventListener("click", function(){
    console.log("play clicked");

    if(showIncompatiblePopup){
        incompatibleModal.show();

        const incompatibleBox = document.getElementById("incompatibleModalContent");
        incompatibleBox.innerHTML = "<p id='text'></p>";

        const box = document.getElementById("text");

        box.innerHTML = "WARNING: Intel Integrated Graphics card may cause Thrive to " +
            "have low performance or even crash.</a>";

        box.append(document.createElement("br"));
        box.append(document.createElement("br"));

        box.append(document.createTextNode("Detected graphics card(s):" + cardsModel));

        // Shows the help text if a non-Intel card is detected
        if(showHelpText){
            box.append(document.createElement("br"));
            box.append(document.createElement("br"));
            box.append(document.createTextNode("Another graphics card detected, " +
                "you should configure " +
                "Thrive to run with that instead!"));
        }

        box.append(document.createElement("br"));

        const closeContainer = document.createElement("div");
        closeContainer.style.marginTop = "20px";
        closeContainer.style.textAlign = "center";
        const close = document.createElement("div");
        close.textContent = "Continue";
        close.className = "CloseButton";

        close.addEventListener("click", () => {
            incompatibleModal.hide();
            showIncompatiblePopup = false;
            playPressed();
        });

        closeContainer.append(close);
        box.append(closeContainer);
        settings.showIncompatiblePopup = false;
    } else {
        playPressed();
    }
});

const playComboPopup = document.getElementById("playComboPopup");


const versionSelectPopupBackground = document.getElementById("playComboBackground");
const versionSelectPopup = document.getElementById("versionSelectPopup");


let playComboAllChoices = null;

const versionSelectCombo = new ComboBox(versionSelectPopupBackground, versionSelectPopup, {


    closeButton: playComboPopup,
    onClose: function(){


    },
    onOpen: function(){

        console.log("open combo popup");
        this.position(playButton);

        versionSelectPopup.innerHTML = "";

        // Add versions //
        for(const version of playComboAllChoices){

            const div = document.createElement("div");
            div.classList.add("ComboVersionSelect");
            div.classList.add("Clickable");

            let prefix = "";

            if(version.version.id == playButtonText.dataset.selectedID &&
                version.download.os == playButtonText.dataset.selectedDLOS){
                prefix = "[SELECTED] ";
            }

            div.textContent = prefix + version.version.getDescriptionString() + " " +
                version.download.getDescriptionString();

            versionSelectPopup.append(div);


            div.addEventListener("click", function(){

                console.log("selected new version");

                playButtonText.dataset.selectedID = version.version.id;
                playButtonText.dataset.selectedDLOS = version.download.os;

                updatePlayButtonText();
                versionSelectCombo.hide();
            });
        }
    },
});

//! Updates play button text
function updatePlayButtonText(){

    const version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

    assert(version);

    const download = versionInfo.getDownloadByOSID(version.id,
        playButtonText.dataset.selectedDLOS);

    assert(download);

    playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
        download.getDescriptionString();
}

//! Called once version info is loaded
function updatePlayButton(){

    playButtonText.textContent = "Processing Version Data...";

    const version = versionInfo.getRecommendedVersion();

    if(!version){
        playButtonText.textContent = "Latest version has invalid number";
        return;
    }

    const dl = versionInfo.getDownloadForPlatform(version.id);

    // Dump the other versions to be selected in the combo box thing //
    const options = versionInfo.getAllValidVersions();

    playComboAllChoices = options;

    // Sort the versions //
    playComboAllChoices.sort(function(a, b){

        if(a.version.releaseNum < b.version.releaseNum)
            return 1;
        if(a.version.releaseNum > b.version.releaseNum)
            return -1;

        return a.download.os < b.download.os;
    });

    console.log("All valid versions: " + options.length);

    // If this is null then we should let the user know that there was no
    // preferred version
    if(!dl){
        playButtonText.textContent = "Couldn't find recommended version for current platform";
        return;
    }

    // Verify retrieve logic
    assert(versionInfo.getCurrentPlatform().os == versionInfo.getPlatformByID(dl.os).os);

    // I don't think this is needed
    // playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
    //     dl.getDescriptionString();

    playButtonText.dataset.selectedID = version.id;
    playButtonText.dataset.selectedDLOS = dl.os;

    updatePlayButtonText();
}

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
