//
// Helpers for installing and removing versions
//
// TODO: the installing functions are still in renderer.js and should be moved here
const fs = require('fs-extra');
const path = require('path');
const rimraf = require("rimraf");
const { dialog } = require('electron').remote;
const win = remote.getCurrentWindow();

const {getVersionData} = require("./version_info.js");

const {saveSettings, defaultInstallPath, getInstallPath, settings, setInstallPath } = require("./settings.js");

const isDirectory = source => fs.lstatSync(source).isDirectory();
const getDirectories = source =>
      fs.readdirSync(source).map(name => path.join(source, name)).filter(isDirectory);

// Returns the names of all installed versions. And extra files in the installed folder
function listInstalledVersions(){
    return new Promise((resolve, reject) => {

        const versions = getVersionData().versions;

        const directories = getDirectories(getInstallPath());;

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

        const finalPath = path.join(getInstallPath(), name);

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

module.exports.listInstalledVersions = listInstalledVersions;
module.exports.deleteInstalledVersion = deleteInstalledVersion;
