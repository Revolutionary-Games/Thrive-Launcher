"use strict";

const remote = require("@electron/remote");

const path = require("path");

// Implementation taken from https://stackoverflow.com/a/18650828/9277476.
/**
 * Convert a raw number of bytes into a human-readable notation.
 *
 * @param {string | number} bytes
 */
function formatBytes(bytes, precision = 2){
    const units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
    const kb = 1024;

    if(bytes == 0 || isNaN(bytes)){
        return "0 B";
    }

    const unit = Math.floor(Math.log(Number(bytes)) / Math.log(kb));

    return (Number(bytes) / Math.pow(kb, unit)).
        toFixed(Math.max(precision, 0)) + " " + units[unit];
}

// Fetch with a timeout
function fetchWithTimeout(url, options, ms){
    const controller = new AbortController();

    const timeoutId = setTimeout(() => controller.abort(), ms);

    return fetch(url, {...options, signal: controller.signal}).then((response) => {
        clearTimeout(timeoutId);
        return response;
    });
}

// Fixes executable path if this is a packaged release
function convertPackedExecutablePath(executablePath){
    return executablePath.replace("app.asar", "app.asar.unpacked");
}

function sleep(ms){
    return new Promise((resolve) => setTimeout(resolve, ms));
}

function hideElement(id){
    document.getElementById(id).style.display = "none";
}

//! Returns the application folder. Handling for both packed and unpacked version
//! \remarks main.js Has this same functionality and these need to be kept in sync
function getApplicationFolder(){
    const appPath = remote.app.getAppPath();

    if(appPath.includes("app.asar")){
        // Packaged version
        return path.dirname(remote.app.getPath("exe"));
    } else {
        return appPath;
    }
}

//! Actually working assert. The normal assert should be fixed in electron, but maybe
//! they broke it again?
//! https://github.com/electron/electron/issues/24577
function assert(bool){
    if(!bool)
        throw new Error("Assertion failed");
}

exports.formatBytes = formatBytes;
exports.fetchWithTimeout = fetchWithTimeout;
exports.convertPackedExecutablePath = convertPackedExecutablePath;
exports.sleep = sleep;
exports.hideElement = hideElement;
exports.getApplicationFolder = getApplicationFolder;
exports.assert = assert;
