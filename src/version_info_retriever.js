// Retrieves Thrive version info
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");

const {loadTestVersionData} = require("./config");
const {Modal, showGenericError} = require("../modal");
const {setPlayButtonText} = require("./version_select_button");
const {locallyCachedDLFile} = require("../settings.js");
const {fetchWithTimeout} = require("./utils");

const versionDataFailedModal = new Modal("versionDataDownloadFailedModal",
    "versionDataDownloadFailedModalDialog", {autoClose: false});

// If true will only attempt reading the prepackaged version data
// Can be changed by user if no internet / download fails
let loadPrePackagedVersionData = false;

async function loadVersionData(callback){
    setPlayButtonText("Retrieving version information...");

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

                callback(data, true);
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

                callback(data);
            });
    } else {
        fetchWithTimeout("https://raw.githubusercontent.com/" +
            "Revolutionary-Games/Thrive-Launcher/master/version_data/signed_versions.json", {
            mode: "no-cors",
            credentials: "omit",
            cache: "no-cache",
        }, 15000).then((response) => {
            if(response.status !== 200){
                throw `Invalid response code from server: (${response.status})`;
            }

            return response.text();
        }).then((data) => {
            console.log("successfully downloaded version information");

            fs.writeFile(locallyCachedDLFile, data, (err) => {
                if(err){
                    console.error("Unable to locally save downloaded version info: " +
                        err);
                }
            });

            callback(data);

        }).catch((error) => {
            // Construct an error message
            let message = "Unable to download Thrive version information. ";

            // Create a good error message //
            if(error){
                message += error;
            }

            console.log(message);

            versionDataFailedModal.show();

            const existsLocalFile = fs.existsSync(locallyCachedDLFile);

            const failText = $("#versionDataDownloadFailedText");
            failText.text(message);

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
                    loadVersionData(callback);
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

                            callback(data);
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

            failText.append($(container));
        });
    }
}

module.exports.loadVersionData = loadVersionData;
