// Handles rehydrating dehydrated builds
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");

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

            status.textContent = "This is a dehydrated build. Rehydrating...";

            // TODO: rehydrate
        });
    });
}

exports.checkIsDehydrated = checkIsDehydrated;
