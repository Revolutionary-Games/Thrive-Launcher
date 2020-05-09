//
// Implements unpacking downloaded thrive releases
//
"use strict";

const remote = require("electron").remote;

const os = remote.require("os");
const path = require("path");
const fs = remote.require("fs");

const {spawn} = remote.require("child_process");

function sanityEscape(str){

    return str.replace(/'/gi, "").replace(/"/gi, "");
}

function unpackRelease(unpackFolder, targetFolderName, archiveFile, progressElement){

    const target = path.join(unpackFolder, targetFolderName);

    return new Promise(function(resolve, reject){

        let unpacker = null;

        if(os.platform() == "win32"){

            // By default use system installed 7zip
            const zPaths = [
                "C:\\Program Files\\7-Zip\\7z.exe",
                "C:\\Program Files (x86)\\7-Zip\\7z.exe"
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

                unpacker = path.join(remote.app.getAppPath(), "7zip\\7za.exe");

                if(!fs.existsSync(unpacker)){
                    reject(new Error("You don't have 7Zip installed!. Download here: " +
                                     "http://www.7-zip.org/download.html"));

                    return;
                }
            }

        } else {
            // TODO: allow using system 7za if present to not need to have 32 bit
            // libs installed
            unpacker = path.join(remote.app.getAppPath(), "7zip/7za");
        }

        // In packaged builds this is needed for this to work
        unpacker = unpacker.replace("app.asar", "app.asar.unpacked");

        // Verify unpacker is installed
        if(!fs.existsSync(unpacker)){

            reject(new Error("unpacker (" + unpacker + ") executable is missing"));
            return;
        }

        let message = "";

        const unpackProcess = spawn(unpacker,
            ["x", sanityEscape(archiveFile), "-O" + sanityEscape(target) + ""]);

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
    if(fallBack)
        return lastFolder;

    return null;
}

module.exports.unpackRelease = unpackRelease;
module.exports.findBinInRelease = findBinInRelease;

