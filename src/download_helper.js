// Functions for downloading stuff using node js APIs
"use strict";

const {remote} = require("electron");

const fs = remote.require("fs");
const request = require("request");

const sha3_256 = require("js-sha3").sha3_256;

const {Progress} = require("./progress");
const {formatBytes} = require("./utils");

// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-
// save-it-and-show-download-progress (note: link split on two lines)
// With some modifications
function downloadFile(configuration, useProgress = true){
    const downloadProgress = useProgress ? Progress("download") : null;

    if(useProgress)
        downloadProgress.formatter = formatBytes;

    return new Promise(function(resolve, reject){
        // Save variable to know progress
        let received_bytes = 0;
        let total_bytes = 0;

        const req = request({
            method: "GET",
            uri: configuration.remoteFile,
        });

        configuration.reqObj = req;

        const out = fs.createWriteStream(configuration.localFile, {encoding: null});

        let contentType = "unknown";

        req.on("response", function(data){
            // If we get invalid response code fail
            if(data.statusCode !== 200){
                reject(new Error("Got response with unexpected status code: " +
                    data.statusCode));
                return;
            }

            // Change the total bytes value to get progress later.
            total_bytes = parseInt(data.headers["content-length"]);
            if(useProgress)
                downloadProgress.max = total_bytes;

            contentType = data.headers["content-type"];
        });

        // Get progress if callback exists
        if(Object.prototype.hasOwnProperty.call(configuration, "onProgress")){
            req.on("data", function(chunk){
                // Update the received bytes
                received_bytes += chunk.length;

                configuration.onProgress(received_bytes, total_bytes);

                out.write(chunk);
            });
        } else {
            req.on("data", function(chunk){
                // Update the received bytes
                received_bytes += chunk.length;
                out.write(chunk);
            });
        }

        req.on("end", function(){
            out.end();
            resolve(contentType);
        });

        req.on("error", function(err){

            out.end();
            fs.unlinkSync(configuration.localFile);
            reject(err);
        });
    });
}

function computeHashTimeoutHandler(progressTracker, reject, attemptNumber){
    if(progressTracker.aborted)
        return;

    ++progressTracker.secondsWithoutProgress;

    const allowedNoProgress = attemptNumber * attemptNumber + 1;

    if(progressTracker.secondsWithoutProgress > allowedNoProgress){
        console.error("Hashing hasn't progressed in the last " + allowedNoProgress +
            " seconds, aborting");
        reject(new Error("File hashing is stuck"));
        progressTracker.aborted = true;
        return;
    }

    setTimeout(() => {
        computeHashTimeoutHandler(progressTracker, reject, attemptNumber);
    }, 1000);
}

async function computeHashInternal(file, progressCallback, attemptNumber){
    return new Promise((resolve, reject) => {
        const progressTracker = {
            secondsWithoutProgress: 0,
            aborted: false,
        };

        const totalSize = fs.statSync(file).size;

        computeHashTimeoutHandler(progressTracker, reject, attemptNumber);

        const hasher = sha3_256.create();

        const readable = fs.createReadStream(file, {encoding: null});

        readable.on("data", (chunk) => {

            const percentage = (readable.bytesRead * 100) / totalSize;

            if(progressCallback)
                progressCallback(percentage);

            progressTracker.secondsWithoutProgress = 0;

            try{
                hasher.update(chunk);
            } catch(error){
                console.error("Error updating hasher");
                reject(error);
                readable.close();
            }
        });

        readable.on("end", () => {
            progressTracker.aborted = true;
            resolve(hasher.hex());
        });

        readable.on("close", () => {
            progressTracker.aborted = true;
            resolve(hasher.hex());
        });

        readable.on("error", () => {
            progressTracker.aborted = true;
            reject(new Error("File read stream error for computing hash"));
        });
    });
}

// Returns a file hash
async function computeFileHashSHA3(file, progressCallback){
    fs.accessSync(file, fs.constants.R_OK);

    // Attempt a few times as sometimes when checking devbuild dehydrated objects, this
    // gets stuck
    for(let attempt = 1; attempt < 11; ++attempt){
        try{
            return await computeHashInternal(file, progressCallback, attempt);
        } catch(error){
            if(attempt < 10){
                console.error("Failed to compute file hash, retrying, " + error);
            } else {
                throw error;
            }
        }
    }

    throw new Error("ran out of retries for hash compute");
}

// Verifies file hash
// returns a promise that either succeeds or fails once the check is done
function verifyDLHash(version, download, localTarget){
    return new Promise((resolve, reject) => {
        const status = document.getElementById("playHashProgress");

        computeFileHashSHA3(localTarget, (percentage) => {
            if(status)
                status.textContent = "Progress " + percentage.toFixed(2) + "%";
        }).then((fileHash) => {
            if(download.hash !== fileHash){

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

                reject(new Error());
                return;
            }

            resolve();
        });
    });
}

module.exports.downloadFile = downloadFile;
module.exports.verifyDLHash = verifyDLHash;
module.exports.computeFileHashSHA3 = computeFileHashSHA3;
