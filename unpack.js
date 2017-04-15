//
// Implements unpacking downloaded thrive releases
//
"use strict";

const os = require('os');
const path = require('path');
const fs = require('fs');

const { exec, spawn } = require('child_process');
var { remote } = require('electron');

function sanityEscape(str){

    return str.replace(/\'/gi, '').replace(/\"/gi, '');
}

function unpackRelease(unpackFolder, targetFolderName, archiveFile, progressElement){

    // In releases working directory and remote.app.getAppPath() aren't the same
    // process.cwd
    archiveFile = path.join(process.cwd(), archiveFile)
    let target = path.join(process.cwd(), unpackFolder, targetFolderName);

    return new Promise(function(resolve, reject){

        let unpacker;

        if(os.platform() == "win32"){
            
            // By default use system installed 7zip
            const zPaths = ["C:\\Program Files\\7-Zip\\7z.exe",
                            "C:\\Program Files (x86)\\7-Zip\\7z.exe"];
            
            for(let z of zPaths){
                
                if(fs.existsSync(z)){
                    
                    unpacker = z;
                    break;
                }
            }
            
            if(!unpacker){
                
                // Use packed in version //
                console.log("No system installed 7z found, using packed in one");
                
                unpacker = path.join(remote.app.getAppPath(), "7zip\\7za.exe");
                
                if(!fs.existsSync(unpacker)){
                    reject("You don't have 7Zip installed!. Download here: " +
                        "http://www.7-zip.org/download.html");
                        
                    return;     
                }
            }

        } else {

            unpacker = path.join(remote.app.getAppPath(), "7zip/7za");
        }

        // Verify unpacker is installed
        if(!fs.existsSync(unpacker)){

            reject("unpacker (" + unpacker + ") executable is missing");
            return;
        }

        let message = "";

        const unpackProcess = spawn(
            unpacker, 
            ['x', sanityEscape(archiveFile), "-O" + sanityEscape(target) + ""], 
            {
                cwd: path.dirname(unpacker)
            });

        if(!unpackProcess){

            reject("unpack process wasn't started for some reason");
            return;
        }

        const onProgressMessage = (data) => {

            if(progressElement){
                let div = document.createElement("div");
                div.textContent = data;
                progressElement.append(div);
                progressElement.scrollTop = progressElement.scrollHeight;
            }            
        }
        
        unpackProcess.stdout.on('data', (data) => {
            message += data;

            onProgressMessage(data);
        });

        unpackProcess.stderr.on('data', (data) => {
            message += data;

            onProgressMessage(data);
        });

        unpackProcess.on('error', (err) => {
            reject("Unpacker failed to start with error: " + err);
            return;
        });
        
        unpackProcess.on('close', (code) => {
            
            if(code !== 0){
                console.log(`Unpacker exited with code ${code}`);
                
                reject("Unpacker exited with code: " + code + ", message: " + message);
                return;
            }

            onProgressMessage("Unpacking finished successfully");
            resolve();
            return;
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


