//
// Implements unpacking downloaded thrive releases
//
"use strict";

const os = require('os');
const path = require('path');
const fs = require('fs');

const exec = require('child_process').exec;

function sanityEscape(str){

    return str.replace(/\'/gi, '');
}

function unpackRelease(unpackFolder, targetFolderName, archiveFile){

    return new Promise(function(resolve, reject){

        let unpacker;

        if(os.platform == "win32"){

            unpacker = "7zip/7z.exe";

        } else {

            unpacker = "7zip/7za";
        }

        // Verify unpacker is installed
        if(!fs.existsSync(unpacker)){

            reject("unpacker (" + unpacker + ") executable is missing");
            return;
        }

        let target = path.join(unpackFolder, targetFolderName);

        exec(unpacker + " x '" + sanityEscape(archiveFile) + "' -O'" + sanityEscape(target) +
             "'" ,
             {
                 timeout: 300000
             }, (error, stdout, stderr) => {
                 
                 if(error){
                     
                     console.error(`exec error: ${error}`);
                     
                     reject("Exec error: " + error);
                     return;
                 }
                 
                 //console.log(`stdout: ${stdout}`);
                 //console.log(`stderr: ${stderr}`);

                 resolve();
             });
    });
}

function findBinInRelease(releaseFolder){

    // We might already be in the right folder
    if(fs.existsSync(path.join(releaseFolder, 'bin')))
        return path.join(releaseFolder, 'bin');

    let files = fs.readdirSync(releaseFolder);

    for(let dirEntry of files){

        let file = path.join(releaseFolder, dirEntry);

        if(fs.statSync(file).isDirectory()){

            let bin = findBinInRelease(file);

            if(bin)
                return bin;
        }
    }

    // Not found //
    return null;
}

module.exports.unpackRelease = unpackRelease;
module.exports.findBinInRelease = findBinInRelease;


