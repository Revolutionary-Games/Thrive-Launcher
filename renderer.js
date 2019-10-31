// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const fs = require('fs');
const path = require('path');
const assert = require('assert');
const request = require('request');
const mkdirp = require('mkdirp');
const os = require('os');
const child_process = require('child_process');
const url = require("url");
const si = require("systeminformation");

const sha3_256 = require('js-sha3').sha3_256;

var {ipcRenderer, remote} = require('electron');

const win = remote.getCurrentWindow();

const versionInfo = require('./version_info');
const retrieveNews = require('./retrieve_news');
const errorSuggestions = require('./error_suggestions');
const { Modal, ComboBox, showGenericError} = require('./modal');
const { unpackRelease, findBinInRelease } = require('./unpack');

const openpgp = require('openpgp');

// There's warnings that this could expose some server-only data to
// clients, but we don't have separate things so that shouldn't apply
var pjson = require('./package.json');

//
// Settings thing
//
const { settings, loadSettings, saveSettings, dataFolder,
        tmpDLFolder, locallyCachedDLFile } =
      require('./settings.js');

let cardsModel = [];

let showIncompatiblePopup = false;

let showHelpText = false;

// This loads settings in sync mode here
loadSettings();

const { onGameEnded } = require('./crash_reporting.js');

// Shows output from 7z. Not really usefull as it shows no actual progress
const showUnpackMessages = false;

// If true then the testing data (local, unsigned) file is loaded
const loadTestVersionData = false;

// If true will only attempt reading the prepackaged version data
// Can be changed by user if no internet / download fails
let loadPrePackagedVersionData = false;


const linksModal = new Modal("linksModal", "linksModalDialog", {
    closeButton: "linksClose"
});

const constOldVersionErrorModal = new Modal("errorOldCantStartModal",
                                            "errorOldCantStartModalDialog", {
                                                autoClose: false
                                            });

const updateModal = new Modal("newReleaseAvailableModal",
                              "newReleaseAvailableModalDialog", {
                                  autoClose: false,
                                  closeButton: "newReleaseClose",
                              });

const versionDataFailedModal = new Modal("versionDataDownloadFailedModal",
                                         "versionDataDownloadFailedModalDialog", {
                                             autoClose: false
                                         });

var playModalQuitDLCancel = null;
var currentDLCanceled = false;

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
        //return true;
    }
});

const incompatibleModal = new Modal("incompatibleModal", "incompatibleModalDialog", {
    autoClose: true,
    closeButton: "incompatibleModalClose",
});

// Use getLauncherKey instead
let launcherKey = null;

// Loads the key required for verifying version information //
function getLauncherKey(){

    return new Promise(function(resolve, reject){

        if(launcherKey){
            resolve(launcherKey);
        } else {
            fs.readFile(path.join(remote.app.getAppPath(), 'version_data/launcher_key.pgp'),
                        "utf8",
                        function (err, data){

                            if(err){
                                let msg = "Can't read launcher version info signing key";

                                reject(msg + ". " + err);
                                console.log(err);
                                return;
                            }
                            
                            let keyid;

                            openpgp.key.readArmored(data).then((key) => {
                                
                                launcherKey = key.keys;

                                try{
                                    keyid = launcherKey["0"].primaryKey.keyid.toHex();
                                } catch(err){
                                    reject("Loaded signing key but it is invalid " +
                                           "(property error): " + err);
                                    return;
                                }
                                
                                // console.log("Key: " + launcherKey);
                                console.log("Signing key loaded: " + keyid);
                                resolve(launcherKey);
                                
                            }, (err) => {
                                reject("Couldn't parse signing key: " + err);
                            });
                        });
        }
    });
}


// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-save-it-and-show-download-progress
// With some modifications
function downloadFile(configuration){
    return new Promise(function(resolve, reject){
        // Save variable to know progress
        var received_bytes = 0;
        var total_bytes = 0;

        var req = request({
            method: 'GET',
            uri: configuration.remoteFile
        });

        configuration.reqObj = req;

        var out = fs.createWriteStream(configuration.localFile, { encoding: null });
        //req.pipe(out);

        let contentType = "unknown";

        req.on('response', function ( data ) {
            // Change the total bytes value to get progress later.
            total_bytes = parseInt(data.headers['content-length' ]);

            contentType = data.headers['content-type'];
        });

        // Get progress if callback exists
        if(configuration.hasOwnProperty("onProgress")){
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;

                configuration.onProgress(received_bytes, total_bytes);

                out.write(chunk);
            });
        }else{
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;
                out.write(chunk);
            });
        }

        req.on('end', function() {
            out.end();
            resolve(contentType);
        });

        req.on('error', function(err){

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
        
        getLauncherKey().then((key) =>{

            if(!key){

                reject("get launcher key is null");
                return;
            }

            // Unpack and verify signature //
            openpgp.cleartext.readArmored(data).then((message) => {

                let options = {
                    message: message,
                    publicKeys: key
                };
                
                openpgp.verify(options).then(function(verified) {
	                let validity = verified.signatures[0].valid;
	                if (validity) {
		                console.log('Version data signed by key id ' +
                                    verified.signatures[0].keyid.toHex());
                        
                        versionInfo.parseData(verified.data);

                        resolve();
                        
	                } else {
                        let msg = "Error verifying signature validity. " +
                            "Did the download get corrupted?";
                        showGenericError(msg, () =>{

                            reject(msg);
                        });
                    }
                });

            }, (err) => {
                reject(err);
            });

            
        }, (err) =>{
            reject(err);
        });
    }).then(() => {
        return new Promise(function(resolve, reject){

            if(!versionInfo.getVersionData().versions){

                reject("No versions");
                return;
            }
            
            let dlVersion = versionInfo.getLauncherMeta();

            if(dlVersion.latestVersion == pjson.version){

                console.log("Using latest version: " + dlVersion.latestVersion);
            
            } else {

                // Show update asking dialog //
                updateModal.show();
                updateModal.onClose = () => {

                    resolve();
                };

                let message = document.createElement("p");

                message.append(
                    document.createTextNode("You are using Thrive launcher version " +
                                            pjson.version + " but the latest version is " +
                                            dlVersion.latestVersion));

                let link = document.createElement("a");
                link.textContent = "Visit releases page";

                const urlTarget = dlVersion.releaseDLURL ||
                      "https://github.com/Revolutionary-Games/Thrive-Launcher/releases";
                link.href = urlTarget;

                message.append(document.createElement("br"));
                message.append(link);

                let textParent = $("#newReleaseAvailableText");

                textParent.empty();
                textParent.append($(message));

                // Buttons //
                let container = document.createElement("div");

                container.classList.add("UpdateButtonContainer");
                
                let dlnow = document.createElement("div");
                dlnow.classList.add("BottomButton");
                dlnow.style.fontSize = "3.4em";
                // dlnow.textContent = "Download Now";
                dlnow.textContent = "Download Updated Launcher";

                container.append(dlnow);
                textParent.append($(container));


                dlnow.addEventListener('click', (event) => {

                    console.log("Clicked download now");
                    require('electron').shell.openExternal(urlTarget);
                    dlnow.textContent = "Opening link...";
                });
                
                return;
            }
            
            resolve();
        });
    }).then(() => {

        updatePlayButton();
        
    }).catch((err) => {

        // Fail //
        constOldVersionErrorModal.show();

        if(err){

            let text = document.getElementById("errorOldCantStartText");

            if(text){
                text.append(document.createElement("br"));
                text.append(document.createTextNode(" Error message: " + err));
            }
        }
    });
}

// Checks the graphics card
async function checkIfCompatible() {
    try {
        const data = await si.graphics();
        const identifier = ["nvidia", "advanced micro devices", "amd"]; // and so on...

        let cardsVendor = [];

		for(var i = 0; i < data.controllers.length; i++){
			
			let graphicControllers = data.controllers[i];
			
			cardsVendor.push(graphicControllers.vendor.toLowerCase());
			cardsModel.push(" " + graphicControllers.model);
            
            for(var n = 0; n < cardsVendor.length; n++){
                
                // Is incompatible if intel is found in a substring
		        if(cardsVendor[n].split(" ").includes("intel")){

			        showIncompatiblePopup = true;
                }
				
				for(var x = 0; x < identifier.length; x++){
					if(cardsVendor[n].split(" ").includes(identifier[x])){
						showHelpText = true;
					}
				}
            }
		}
        
    } catch (e) {
        console.log(e)
    }
}

async function loadVersionData(){

    await checkIfCompatible();

    if(loadTestVersionData){
        
        fs.readFile(path.join(remote.app.getAppPath(), 'version_data/thrive_versions.json'),
                    "utf8",
                    function (err,data){
                        
                        if (err) {
                            let msg = "Failed to read test version data: " +
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
        fs.readFile(path.join(remote.app.getAppPath(), 'version_data/signed_versions.json'),
                    "utf8",
                    function (err,data){
                        
                        if (err) {
                            let msg = "Failed to read pre-packaged version data: " +
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
            headers: {
                'User-Agent': "Thrive-Launcher " + pjson.version
            }
        }, function (error, response, body){

            if(error || !response || !body || response.statusCode != 200){

                // Unable to connect //
                // Construct an error message
                let message = "Unable to download Thrive version information. ";

                // Create a good error message //
                if(error){

                    message += error;

                } else {

                    if(response.statusCode != 200){

                        message += "File not found on server, status code: " +
                            response.statusCode;

                    } else {

                        message += "Some other error occurred.";
                    }
                }

                console.log(message);

                versionDataFailedModal.show();

                const existsLocalFile = fs.existsSync(locallyCachedDLFile);

                $("#versionDataDownloadFailedText").text(message);

                let container = document.createElement("div");

                container.classList.add("UpdateButtonContainer");
                
                let dlnow = document.createElement("div");
                dlnow.classList.add("BottomButton");
                dlnow.style.fontSize = "1.7em";
                dlnow.style.marginRight = "5px";
                dlnow.textContent = "Retry";
                
                container.append(dlnow);
                
                dlnow.addEventListener('click', (event) => {
                    
                    console.log("Clicked retry");
                    versionDataFailedModal.hide();

                    // Wait for animation to end //
                    setTimeout(() =>{
                        loadVersionData();
                    }, 700);
                });

                if(existsLocalFile){

                    let useLocal = document.createElement("div");
                    useLocal.classList.add("BottomButton");
                    useLocal.style.fontSize = "1.7em";
                    useLocal.style.marginLeft = "5px";
                    useLocal.textContent = "Use Previous Version";
                    
                    container.append(useLocal);
                    
                    useLocal.addEventListener('click', (event) => {
                        
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

                    let usePrepackaged = document.createElement("div");
                    usePrepackaged.classList.add("BottomButton");
                    usePrepackaged.style.fontSize = "3.4em";
                    usePrepackaged.style.marginLeft = "5px";
                    usePrepackaged.textContent = "Use Pre-packaged (old)";
                    
                    container.append(usePrepackaged);
                    
                    usePrepackaged.addEventListener('click', (event) => {
                        
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
};

// Buttons
let playButton = document.getElementById("playButton");

let playButtonText = document.getElementById("playText");

playButtonText.textContent = "Retrieving version information...";

// Maybe this does something to the stuck downloading version info bug
loadVersionData();

//! Verifies file hash
//! returns a promise that either cusseeds or fails once the check is done
function verifyDLHash(version, download, localTarget){

    return new Promise((resolve, reject) => {

        const totalSize = fs.statSync(localTarget).size;

        let hasher = sha3_256.create();

        let readable = fs.createReadStream(localTarget, { encoding: null });

        let status = document.getElementById("playHashProgress");
        
        readable.on('data', (chunk) => {

            let percentage = (readable.bytesRead * 100) / totalSize;

            if(status)
                status.textContent = "Progress " + percentage.toFixed(2) + "%";
            
            hasher.update(chunk);
        });

        readable.on('end', () => {

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

                reject();
                return;
            }
            
            resolve();
        });
        
    });
}

function onThriveFolderReady(version, download){

    const installFolder = path.join(settings.installPath, download.folderName);
    
    assert(fs.existsSync(installFolder));

    let status = document.getElementById("playingInternalP");

    // Destroy the download progress indicator
    status.innerHTML = "";

    status.textContent = "preparing to launch";

    // Find bin folder //
    let binFolder = findBinInRelease(installFolder);

    if(!fs.existsSync(binFolder)){

        status.textContent = "Error 'bin' folder is missing! To redownload delete " +
            installFolder;
        return;
    }

    // Check that executable is there //
    let exename;
    
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

    // cwd is where relative to things are installed
    let thrive = child_process.spawn(path.join(binFolder, exename),
                                     [],
                                     {
                                         cwd: binFolder
                                     });

    if(settings.hideLauncherOnPlay){
        win.minimize();
    }
    
    status.innerHTML = "";

    let titleSpan = document.createElement("span");

    let processOutput = document.createElement("div");

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

    let appendMessage = (text) => {

        let message = document.createElement("div");
        message.textContent = text;
        processOutput.append(message);

        let container = $( processOutput );

        // Max number of messages
        while(container.children().length > 1000){

            // Remove elements //
            container.children().first().remove();
        }

        // For some reason the jquery thing is not working so this is at least a decent choice
        message.scrollIntoView(false);

        // let modalContainer = $( playModal.dialog );

        // let check = $("#playModalDialog");

        // //assert(modalContainer == check);

        // check.scrollTop = check.scrollHeight;

        // // modalContainer.animate({
        // //     scrollTop: modalContainer.scrollHeight
        // // }, 500);
    };

    appendMessage("Process Started");
    
    thrive.stdout.on('data', (data) => {

        for(let line of data.toString().split(/\r?\n/g))
            appendMessage(line);
    });

    thrive.stderr.on('data', (data) => {

        appendMessage("ERROR: " + data);
    });

    thrive.on('exit', (code, signal) => {

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

        let closeContainer = document.createElement("div");

        closeContainer.style.textAlign = "center";
        
        let close = document.createElement("div");

        close.textContent = "Close";

        close.className = "CloseButton";

        close.addEventListener('click', (event) => {

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

//! Called once a file has been downloaded (or existed) and startup should continue
function onDLFileReady(version, download, fileName){

    // Delete the download progress //
    $( "#dlProgress" ).remove();
        

    const localTarget = path.join(tmpDLFolder, fileName);
    
    assert(fs.existsSync(localTarget));

    let status = document.getElementById("playingInternalP");

    // Destroy the download progress indicator
    status.innerHTML = "";

    // If unpacked already launch Thrive //
    mkdirp(settings.installPath, function (err){
        
        if(err){
            
            console.error(err);
            alert("failed to create install directory");
            return;
        }

        // Check does it exist //
        if(fs.existsSync(path.join(settings.installPath, download.folderName))){

            console.log("archive has already been extracted");
            onThriveFolderReady(version, download);
            return;
        }

        // Need to unpack //


        // Hash is verified before unpacking //
        status.textContent = "Verifying archive '" + fileName + "'";
        let element = document.createElement("p");
        element.id = "playHashProgress";
        status.append(element);

        // Unpack archive //
        verifyDLHash(version, download, localTarget).then(() => {

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

            unpackRelease(settings.installPath, download.folderName, localTarget, unpackProgress).then(
                () => {

                    assert(fs.existsSync(path.join(settings.installPath, download.folderName)));
                    console.log("unpacking completed");

                    onThriveFolderReady(version, download);

                }, (error) => {

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

        }, () => {

            // Fail //
            status.textContent = "Hash for file '" + fileName + "' is invalid "+
                "(download corrupted or wrong file was downloaded) please try again";
        });
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

    let version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

    assert(version);

    let download = versionInfo.getDownloadByOSID(version.id,
                                                 playButtonText.dataset.selectedDLOS);

    assert(download);

    console.log("Playing thrive version: " + version.getDescriptionString() + " " +
                download.getDescriptionString());

    let playBox = document.getElementById("playModalContent");

    playBox.innerHTML = "Playing Thrive " + version.releaseNum +
        "<p id='playingInternalP'>Downloading: " + download.url +
        "</p><div id='dlProgress'></div>";

    let fileName = download.fileName;

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

    mkdirp(tmpDLFolder, function (err){
        
        if(err){
            
            console.error(err);
            alert("failed to create dl directory");
            return;
            
        }

        let dlFailCallback = (error) => {

            if(fs.existsSync(localTarget)){
                
                fs.unlinkSync(localTarget);
            }

            let status = document.getElementById("dlProgress");

            if(status){

                status.textContent = "Download Failed! " + error;
            }
        };

        let dataObj = {
            remoteFile: download.url,
            localFile: localTarget,
            
            onProgress: function (received, total){
                
                let percentage = (received * 100) / total;
                let status = document.getElementById("dlProgress");

                if(status){

                    status.textContent = percentage.toFixed(2) + "% | " + received +
                        " bytes out of " + total + " bytes.";
                }
            }
        };

        downloadFile(dataObj).then(function(contentType){

            if(currentDLCanceled){

                // It was canceled //
                console.log("dl was canceled by user");

                if(fs.existsSync(localTarget))
                    fs.unlinkSync(localTarget);
                return;
            }
            
            if(![ "application/x-7z-compressed",
                  "application/zip",
                  "application/octet-stream"].includes(contentType))
            {
                dlFailCallback("download type is wrong: " + contentType);
                return;
            }

            console.log("Successfully downloaded");
            // No longer need to cancel
            playModalQuitDLCancel = null;

            let status = document.getElementById("playingInternalP");
            status.textContent = "Successfully downloaded " + version.releaseNum;
            
            onDLFileReady(version, download, fileName);
            
            
        }, function(error){

            dlFailCallback("Download failed: " + error);
        });

        // This object is for aborting a download
        assert(dataObj.reqObj);

        playModalQuitDLCancel = dataObj.reqObj;
        
        
    });

}

playButtonText.addEventListener("click", function(event){
    console.log("play clicked");

    if(showIncompatiblePopup){
        incompatibleModal.show();

        let incompatibleBox = document.getElementById("incompatibleModalContent");
        incompatibleBox.innerHTML = "<p id='text'></p>"
    
        let box = document.getElementById("text");

        box.textContent = "Detected graphics card(s):" + cardsModel;

        if(showHelpText){
            box.append(document.createElement("br"));
            box.append(document.createElement("br"));
            box.append(document.createTextNode("Another graphics card detected, you could configure Thrive to run with that instead!"));
        }
        box.append(document.createElement("br"));
        box.append(document.createElement("br"));
        box.append(document.createTextNode("WARNING: Intel Integrated Graphics card may causes Thrive to crash due to some issues with the graphics engine running on it: https://github.com/Revolutionary-Games/Thrive/issues/804."))
        box.append(document.createElement("br"));
        box.append(document.createElement("br"));
        box.append(document.createTextNode("This is a known problem, any help fixing this would be very much appreciated!"));
        
        let closeContainer = document.createElement("div");
        closeContainer.style.marginTop = "20px";
        closeContainer.style.textAlign = "center";
        let close = document.createElement("div");
        close.textContent = "Continue";
        close.className = "CloseButton";
    
        close.addEventListener('click', (event) => {
            incompatibleModal.hide();
            showIncompatiblePopup = false;
            if(cardsModel != null){
                playPressed();
            }
        });

        closeContainer.append(close);
        box.append(closeContainer);
        showIncompatiblePopup = false;
    }
    else{
        playPressed();
    }
});

let playComboPopup = document.getElementById("playComboPopup");


const versionSelectPopupBackground = document.getElementById("playComboBackground");
const versionSelectPopup = document.getElementById("versionSelectPopup");


var playComboAllChoices = null;

const versionSelectCombo = new ComboBox(versionSelectPopupBackground, versionSelectPopup, {

    
    closeButton: playComboPopup,
    onClose: function(){

        
    },
    onOpen: function(){

        console.log("open combo popup");
        this.position(playButton);

        versionSelectPopup.innerHTML = "";

        // Add versions //
        for(let version of playComboAllChoices){

            let div = document.createElement("div");
            div.classList.add("ComboVersionSelect");
            div.classList.add("Clickable");

            let prefix = "";

            if(version.version.id == playButtonText.dataset.selectedID &&
               version.download.os == playButtonText.dataset.selectedDLOS)
            {
                prefix = "[SELECTED] ";
            }
            
            div.textContent = prefix + version.version.getDescriptionString() + " " +
                version.download.getDescriptionString();

            versionSelectPopup.append(div);


            div.addEventListener("click", function(event){

                console.log("selected new version");

                playButtonText.dataset.selectedID = version.version.id;
                playButtonText.dataset.selectedDLOS = version.download.os;
                
                updatePlayButtonText();
                versionSelectCombo.hide();
            });
        }
    }
});

//! Updates play button text
function updatePlayButtonText(){

    let version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

    assert(version);

    let download = versionInfo.getDownloadByOSID(version.id,
                                                 playButtonText.dataset.selectedDLOS);

    assert(download);
    
    playButtonText.textContent = "Play " + version.getDescriptionString() + " " +
        download.getDescriptionString();
}

//! Called once version info is loaded
function updatePlayButton(){

    playButtonText.textContent = "Processing Version Data...";

    let version = versionInfo.getRecommendedVersion();

    if(!version){
        playButtonText.textContent = "Latest version has invalid number";
        return;
    }

    let dl = versionInfo.getDownloadForPlatform(version.id);

    // Dump the other versions to be selected in the combo box thing //
    let options = versionInfo.getAllValidVersions();

    playComboAllChoices = options;

    // Sort the versions //
    playComboAllChoices.sort(function(a, b) {

        if(a.version.releaseNum < b.version.releaseNum)
            return 1;
        if(a.version.releaseNum > b.version.releaseNum)
            return -1;

        return a.version.download.os < b.version.download.os;
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

let newsContent = document.getElementById("newsContent");

let devForumPosts = document.getElementById("devForumPosts");

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
let linksButton = document.getElementById("linksButton");

linksButton.addEventListener("click", function(event){

    linksModal.show();
});

// Settings dialog
require('./settings_dialog.js');
