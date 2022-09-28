// Checks if the loaded version data contains a newer launcher version number
// Suggests updating if it does
"use strict";

const semver = require("semver");

const {Modal} = require("./modal");

// There's warnings that this could expose some server-only data to
// clients, but we don't have separate things so that shouldn't apply
const pjson = require("../package.json");
const {storeInfo} = require("./store_handler");

const updateModal = new Modal("newReleaseAvailableModal",
    "newReleaseAvailableModalDialog", {
        autoClose: false,
        closeButton: "newReleaseClose",
    });

function checkLauncherVersion(versionInfo){
    return new Promise(function(resolve, reject){

        // Store versions are not updated through the launcher
        if(storeInfo.isStoreVersion){
            resolve();
            return;
        }

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

            const dlNow = document.createElement("div");
            dlNow.classList.add("BottomButton");
            dlNow.style.fontSize = "3.4em";

            dlNow.textContent = "Download Updated Launcher";

            container.append(dlNow);
            textParent.append($(container));


            dlNow.addEventListener("click", () => {

                console.log("Clicked download now, trying to press the releases link");
                dlNow.textContent = "Opening link...";
                link.click();
            });

            return;
        }

        console.log("Version is latest or pre-release: " + pjson.version);
        resolve();
    });
}

module.exports.checkLauncherVersion = checkLauncherVersion;
