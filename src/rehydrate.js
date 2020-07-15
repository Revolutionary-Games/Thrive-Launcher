// Handles rehydrating dehydrated builds
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");

const mkdirp = remote.require("mkdirp");
const rimraf = remote.require("rimraf");

const {getDehydrateCacheFolder, tmpDLFolder} = require("../settings");
const {downloadFile} = require("./download_helper");
const {getDownloadForDehydrated} = require("./dev_center");
const {computeFileHashSHA3} = require("./download_helper");
const {unGZip} = require("./file_utils");

// Shows rehydrate progress
// TODO: make a progress bar for this
function reportProgress(status, current, processed, total){
    status.textContent = "Rehydrating... Processing item '" + current + "' " +
        processed + " / " + total;
}

function dehydratedTarget(objectHash){
    return path.join(getDehydrateCacheFolder(), objectHash);
}

async function isDehydratedMissing(objectHash){
    const targetFile = dehydratedTarget(objectHash);

    return new Promise((resolve) => {
        fs.access(targetFile, fs.constants.R_OK, (error) => {
            if(error){
                // Not downloaded. Need to download
                resolve(true);
            } else {
                resolve(false);
            }
        });
    });
}

async function downloadDehydratedObjects(missingHashes, status){
    const total = missingHashes.length;
    let processed = 0;

    while(missingHashes.length > 0){
        const chunk = missingHashes.splice(0, 100);

        const dls = await getDownloadForDehydrated(chunk);

        // TODO: do downloads in parallel
        for(const item of dls){
            ++processed;
            status.textContent = "Rehydrating... downloading item '" + item.sha3 + "' " +
                processed + " / " + total;

            const target = dehydratedTarget(item.sha3);
            const tmpZip = path.join(tmpDLFolder, item.sha3 + ".gz");

            await downloadFile({
                remoteFile: item.download_url,
                localFile: tmpZip,

                onProgress: (/* received */) => {
                    // TODO: progress indicator
                },
            }, false);

            // Unzip it
            await unGZip(tmpZip, target);

            fs.unlink(tmpZip, (error) => {
                if(error){
                    console.error("Failed to delete temporary gz file:", error);
                }
            });

            // Check that downloaded hash is good
            const hash = await computeFileHashSHA3(target, null);

            if(hash !== item.sha3 || !chunk.includes(hash)){
                fs.unlinkSync(target);
                throw "Invalid hash of unzipped dehydrated object";
            }
        }
    }
}

// Rehydrates a thrive install according to info in dehydratedData
async function rehydrate(thriveFolder, dehydratedData, status){
    await mkdirp(getDehydrateCacheFolder());

    // First count total items
    // And also get a list of things we need to download
    let total = 0;
    const missingHashes = [];

    for(const fileName in dehydratedData.files){
        if(!Object.hasOwnProperty.call(dehydratedData.files, fileName))
            continue;

        const file = dehydratedData.files[fileName];

        if(file.type === "pck"){
            // Pcks have sub items
            for(const pckContainedFile in file.data.files){
                if(!Object.hasOwnProperty.call(file.data.files, pckContainedFile))
                    continue;

                const containedData = file.data.files[pckContainedFile];

                if(await isDehydratedMissing(containedData.sha3))
                    missingHashes.push(containedData.sha3);

                ++total;
            }
        } else {
            // Just a single file
            if(await isDehydratedMissing(file.sha3))
                missingHashes.push(file.sha3);

            ++total;
        }
    }

    // Download missing
    if(missingHashes.length > 0){
        await downloadDehydratedObjects(missingHashes, status);
    }

    let processed = 0;

    // Then process the items
    for(const fileName in dehydratedData.files){
        if(!Object.hasOwnProperty.call(dehydratedData.files, fileName))
            continue;

        const file = dehydratedData.files[fileName];

        reportProgress(status, fileName, processed + 1, total);

        if(file.type === "pck"){
            // Run pck tool to repack it

            processed += Object.keys(file.data.files).length;
        } else {
            // Just a single file. Copy it to the target folder

            ++processed;
        }
    }

    status.textContent = "Rehydration complete";
}

async function checkIsDehydrated(thriveFolder, status){
    const dehydrateInfo = path.join(thriveFolder, "dehydrated.json");

    return new Promise((resolve, reject) => {
        fs.access(dehydrateInfo, fs.constants.R_OK, (error) => {
            // Not a dehydrated build
            if(error){
                resolve();
                return;
            }

            console.log(`Thrive folder ${thriveFolder} is a dehydrated build`);

            let dehydrated = null;

            try{
                dehydrated = JSON.parse(fs.readFileSync(dehydrateInfo));
            } catch(error){
                reject(new Error("Failed to read dehydrated info: " + error));
                return;
            }

            status.textContent = "This is a dehydrated build. Rehydrating...";

            rehydrate(thriveFolder, dehydrated, status).then(() => {
                return;
                rimraf(dehydrateInfo, (error) => {
                    if(error){
                        console.error("Failed to delete dehydrate info file:", error);
                    }

                    resolve();
                });
            }).catch((error) => {
                reject(error);
            });
        });
    });
}

exports.checkIsDehydrated = checkIsDehydrated;
