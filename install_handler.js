//
// Helpers for installing and removing versions
//
// TODO: the installing functions are still in renderer.js and should be moved here
const fs = require('fs-extra');
const path = require('path');
const rimraf = require("rimraf");
const { dialog } = require('electron').remote;

const {getVersionData} = require("./version_info.js");

const {saveSettings, getInstallPath, settings, showMovingFileModal, hideMovingFileModal} = require("./settings.js");

const isDirectory = source => fs.lstatSync(source).isDirectory();
const getDirectories = source =>
      fs.readdirSync(source).map(name => path.join(source, name)).filter(isDirectory);

// Returns the names of all installed versions. And extra files in the installed folder
function listInstalledVersions(isMoving){
    return new Promise((resolve, reject) => {

        const versions = getVersionData().versions;

        var directories = null;

        if(isMoving){
            directories = getDirectories(settings.installedDir);
        }else{
            directories = getDirectories(getInstallPath());
        }

        let result = {};

        for(let dir of directories){

            const name = path.basename(dir);
            let good = false;

            for(let ver of versions){
                for(let dl of ver.platforms){

                    if(dl.folderName == name){

                        good = true;
                        break;
                    }
                }

                if(good)
                    break;
            }

            // TODO: calculate folder size

            result[dir] = {path: dir, valid: good, name: name};
        }

        resolve(result);
    });
}

// Deletes an installed version by name
function deleteInstalledVersion(name){
    return new Promise((resolve, reject) => {

        const finalPath = path.join(settings.installedDir, name);

        if(!fs.existsSync(finalPath)){
            reject("path for version doesn't exist: " + finalPath);
            return;
        }

        rimraf(finalPath, (error) => {
            if(error){

                reject("failed to delete, error: " + error);
                return;
            }

            resolve();
        });
    });
}

function moveInstalledVersion(){

    showMovingFileModal();

    listInstalledVersions(true).then((data) => {
        for(let key in data){
            const obj = data[key];

            if(obj.valid){
                console.log("moving file...");

                fs.move(settings.installedDir + "/" + obj.name, getInstallPath() + "/" + obj.name, err => {
                    if (err){
                        dialog.showErrorBox("Error!", "Failed to move file: " + err.message);
                        console.log("error " + err.message)
                    }else{
                        console.log("moving '" + obj.name + "' finished");

                        settings.installedDir = getInstallPath();
                        saveSettings();

                        hideMovingFileModal();
                    }
                });
            }
        }
    });
}

module.exports.listInstalledVersions = listInstalledVersions;
module.exports.deleteInstalledVersion = deleteInstalledVersion;
module.exports.moveInstalledVersion = moveInstalledVersion;
