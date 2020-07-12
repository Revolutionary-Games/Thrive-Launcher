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

// https://github.com/github/fetch/issues/175#issuecomment-216791333
// Adds timeout to a promise
function timeoutPromise(ms, promise){
    return new Promise((resolve, reject) => {
        const timeoutId = setTimeout(() => {
            reject(new Error("timeout"));
        }, ms);
        promise.then((res) => {
            clearTimeout(timeoutId);
            resolve(res);
        },
        (err) => {
            clearTimeout(timeoutId);
            reject(err);
        });
    });
}

exports.formatBytes = formatBytes;
exports.timeoutPromise = timeoutPromise;
