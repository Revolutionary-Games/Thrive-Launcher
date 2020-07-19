// Some random file helper utils
"use strict";

const {ipcRenderer} = require("electron");
const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");
const du = require("du");

function isDirectorySync(folder){
    return fs.lstatSync(folder).isDirectory();
}

// Returns sub directories
function getDirectoriesSync(folder){
    return fs.readdirSync(folder).map((name) => path.join(folder, name)).
        filter(isDirectorySync);
}

// Finds the first subfolder in a folder, or returns null
function findFirstSubFolder(folder){
    for(const directory of getDirectoriesSync(folder))
        return directory;

    return null;
}

// Counts the total size in bytes of a folder and it's sub folders
async function calculateFolderSize(folder){
    try{
        return await du(folder);
    } catch(error){
        console.log("Couldn't get size of directory: ", folder);
        return 0;
    }
}

// Ungzips a file (request goes through the main process)
async function unGZip(file, target){
    return new Promise(function(resolve, reject){
        const responseEvent = "gunzipResult-" + Math.random();

        ipcRenderer.once(responseEvent, (event, arg) => {
            if(arg.error){
                reject(arg.error);
                return;
            }

            resolve();
        });

        ipcRenderer.send("requestGunzip", {
            file: file,
            target: target,
            responseEvent: responseEvent,
        });
    });
}

exports.findFirstSubFolder = findFirstSubFolder;
exports.calculateFolderSize = calculateFolderSize;
exports.getDirectoriesSync = getDirectoriesSync;
exports.isDirectorySync = isDirectorySync;
exports.unGZip = unGZip;
