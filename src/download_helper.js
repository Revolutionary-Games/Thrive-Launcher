// Functions for downloading stuff using node js APIs
"use strict";

const fs = require("electron").remote.require("fs");
const request = require("request");

const sha3_256 = require("js-sha3").sha3_256;

const {Progress} = require("./progress");
const {formatBytes} = require("./utils");

// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-
// save-it-and-show-download-progress (note: link split on two lines)
// With some modifications
function downloadFile(configuration){
    const downloadProgress = Progress("download");
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

        // Req.pipe(out);

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

// Verifies file hash
// returns a promise that either succeeds or fails once the check is done
function verifyDLHash(version, download, localTarget){

    return new Promise((resolve, reject) => {

        fs.accessSync(localTarget, fs.constants.R_OK);

        const totalSize = fs.statSync(localTarget).size;

        const hasher = sha3_256.create();

        const readable = fs.createReadStream(localTarget, {encoding: null});

        const status = document.getElementById("playHashProgress");

        readable.on("data", (chunk) => {

            const percentage = (readable.bytesRead * 100) / totalSize;

            if(status)
                status.textContent = "Progress " + percentage.toFixed(2) + "%";

            hasher.update(chunk);
        });

        readable.on("end", () => {

            const fileHash = hasher.hex();

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
