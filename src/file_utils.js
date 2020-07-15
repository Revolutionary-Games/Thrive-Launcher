// Some random file helper utils
"use strict";

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

exports.findFirstSubFolder = findFirstSubFolder;
exports.calculateFolderSize = calculateFolderSize;
exports.getDirectoriesSync = getDirectoriesSync;
exports.isDirectorySync = isDirectorySync;
