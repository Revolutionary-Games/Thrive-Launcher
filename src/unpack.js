//
// Implements unpacking downloaded thrive releases
//
"use strict";

const remote = require("@electron/remote");

const os = remote.require("os");
const path = require("path");
const fs = remote.require("fs");
const which = remote.require("which");

// This can't be required through remote as otherwise we won't get the error messages to us
// and instead the main process reports the error with a popup
const {spawn} = require("child_process");

const {convertPackedExecutablePath} = require("./utils");

function sanityEscape(str){
    return str.replace(/'/gi, "").replace(/"/gi, "");
}

function unpackRelease(unpackFolder, targetFolderName, archiveFile, progressElement){
    const target = path.join(unpackFolder, targetFolderName);

    return new Promise(function(resolve, reject){
        let unpacker = null;

        if(os.platform() === "win32"){
            // By default use system installed 7zip
            const zPaths = [
                "C:\\Program Files\\7-Zip\\7z.exe",
                "C:\\Program Files (x86)\\7-Zip\\7z.exe",
            ];

            for(const z of zPaths){
                if(fs.existsSync(z)){
                    unpacker = z;
                    break;
                }
            }

            if(!unpacker){
                // Use packed in version //
                console.log("No system installed 7z found, using packed in one");

                unpacker = path.join(remote.app.getAppPath(), "tools\\7zip\\7za.exe");

                if(!fs.existsSync(unpacker)){
                    reject(new Error("You don't have 7Zip installed!. Download here: " +
                        "http://www.7-zip.org/download.html"));

                    return;
                }
            }

        } else if(os.platform() === "linux"){
            // Find from PATH
            unpacker = which.sync("7za", {nothrow: true});

            if(!unpacker){
                // Use packed in version //
                console.log("No 7za found in PATH, using packed in one");

                unpacker = path.join(remote.app.getAppPath(), "tools/7zip/7za");
            }
        } else if(os.platform() === "darwin"){
            // Find from PATH
            unpacker = which.sync("7za", {nothrow: true});

            if(!unpacker){
                // Use packed in version for mac //
                console.log("No 7za found in PATH, using packed in one");

                unpacker = path.join(remote.app.getAppPath(),
                    "tools/7zip/7za_mac");

                if(!fs.existsSync(unpacker)){
                    reject(new Error("You don't have 7Zip installed!. Download here: " +
                        "https://formulae.brew.sh/formula/p7zip"));

                    return;
                }
            }
        } else {
            reject(new Error("Unknown platform for 7zip tool"));
            return;
        }

        // In packaged builds this is needed for this to work
        unpacker = convertPackedExecutablePath(unpacker);

        // Verify unpacker is installed
        if(!fs.existsSync(unpacker)){

            reject(new Error("unpacker (" + unpacker + ") executable is missing"));
            return;
        }

        let message = "";

        // -aoa is overwrite all
        const unpackProcess = spawn(unpacker,
            ["x", sanityEscape(archiveFile), "-aoa", "-O" + sanityEscape(target) + ""]);

        if(!unpackProcess){
            reject(new Error("unpack process wasn't started for some reason"));
            return;
        }

        const onProgressMessage = (data) => {
            if(progressElement){
                const div = document.createElement("div");
                div.textContent = data;
                progressElement.append(div);
                progressElement.scrollTop = progressElement.scrollHeight;
            }
        };

        unpackProcess.stdout.on("data", (data) => {
            message += data;

            onProgressMessage(data);
        });

        unpackProcess.stderr.on("data", (data) => {
            message += data;

            onProgressMessage(data);
        });

        unpackProcess.on("error", (err) => {
            reject(new Error("Unpacker failed to start with error: " + err));
        });

        unpackProcess.on("close", (code) => {

            if(code !== 0){
                console.log(`Unpacker exited with code ${code}`);

                reject(new Error("Unpacker exited with code: " + code + ", message: " +
                    message));
                return;
            }

            onProgressMessage("Unpacking finished successfully");
            resolve();

        });
    });
}

// Returns the name of Thrive executable on this platform
function getThriveExecutableName(){
    if(os.platform() === "win32"){

        return "Thrive.exe";

    } else if(os.platform() === "linux" || os.platform() === "darwin"){

        return "Thrive";
    } else {

        throw "Unknown Thrive exe name for this platform";
    }
}

// Returns true if Thrive executable exists in the current folder
function thriveExecutableExistsInFolder(folderToCheck){
    try{
        const info = fs.lstatSync(path.join(folderToCheck, getThriveExecutableName()));

        return info && !info.isDirectory();

    } catch(error){
        return false;
    }
}

function findBinInRelease(releaseFolder, fallBack = true){
    // We might already be in the right folder
    if(fs.existsSync(path.join(releaseFolder, "bin")))
        return path.join(releaseFolder, "bin");

    const files = fs.readdirSync(releaseFolder);

    let lastFolder = null;

    for(const dirEntry of files){

        const file = path.join(releaseFolder, dirEntry);

        if(fs.statSync(file).isDirectory()){

            // Skip mono folder
            if(dirEntry.match(/Mono/i))
                continue;

            const bin = findBinInRelease(file, false);

            if(bin)
                return bin;

            lastFolder = file;
        }
    }

    // Newer releases have the executable in the root
    // So we return the top level folder we found
    if(fallBack){
        // And devbuilds directly have a thrive exe in the folder to check
        if(thriveExecutableExistsInFolder(releaseFolder)){
            return releaseFolder;
        }

        if(os.platform() === "darwin"){
            const macApp = "Thrive.app/Contents/MacOS";

            return path.join(releaseFolder, macApp);
        }

        return lastFolder;
    }

    return null;
}

module.exports.unpackRelease = unpackRelease;
module.exports.findBinInRelease = findBinInRelease;
module.exports.thriveExecutableExistsInFolder = thriveExecutableExistsInFolder;
module.exports.getThriveExecutableName = getThriveExecutableName;
