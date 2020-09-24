"use strict";

// Implementation taken from https://stackoverflow.com/a/18650828/9277476.
/**
 * Convert a raw number of bytes into a human-readable notation.
 *
 * @param {string | number} bytes
 */
function formatBytes(bytes, precision = 2){
    const units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
    const kb = 1024;

    if(bytes == 0){
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

/**
 * Checks if the OS is 64-bit or 32-bit.
 *
 * @returns {string} either 64x or 86x
 */
function getOSArch(){
    // If node is 64-bit we can safely assume the OS is the same
    if(process.arch === "x64" ||
        Object.prototype.hasOwnProperty.call(process.env, "PROCESSOR_ARCHITEW6432") ||
        process.arch === "arm64" ||
        process.arch === "ppc64"){
        return "x64";
    }

     // In case the above does not catch a 64-bit OS (running the 32-bit launcher on a
     // 64-bit OS may mess up the check), check the old fashioned way.
    const signatures = [
        "x86_64",
        "x86-64",
        "Win64",
        "x64;",
        "amd64",
        "AMD64",
        "WOW64",
        "x64_64",
        "ia64",
        "sparc64",
        "ppc64",
        "IRIX64",
    ];

    // Check the signatures against the userAgent. If a match is found, OS is 64-bit.
    for(const signature of signatures){
        if(navigator.userAgent.indexOf(signature) != -1){
            return "x64";
        }
    }

    // If we get here, the OS is most likely 32-bit.
    return "x86";

}

exports.formatBytes = formatBytes;
exports.fetchWithTimeout = fetchWithTimeout;
exports.convertPackedExecutablePath = convertPackedExecutablePath;
exports.sleep = sleep;
exports.getOSArch = getOSArch;
