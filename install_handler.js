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

const {saveSettings, defaultInstallPath, getInstallPath, settings,
        showMovingFileModal, hideMovingFileModal}
        = require("./settings.js");

const isDirectory = source => fs.lstatSync(source).isDirectory();
const getDirectories = source =>
      fs.readdirSync(source).map(name => path.join(source, name)).filter(isDirectory);

let isMoving = false;

// Returns the names of all installed versions. And extra files in the installed folder
function listInstalledVersions(){
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
    isMoving = true;

    listInstalledVersions().then((data) => {
        for(let key in data){
            const obj = data[key];

            if(obj.valid){
                if(fs.existsSync(settings.installedDir + "/" + obj.name)){
                    console.log(settings.installedDir + "/" + obj.name);
                    console.log("moving file...");
                    showMovingFileModal();

                    fs.move(settings.installedDir + "/" + obj.name, getInstallPath() + "/" + obj.name, err => {
                        if (err){
                            dialog.showErrorBox("Error!", "Failed to move file: " + err.message);
                            console.log("error " + err.message)
                            hideMovingFileModal();

                        }else{
                            isMoving = false;
                            console.log("moving '" + obj.name + "' finished");
    
                            settings.installedDir = getInstallPath();
                            saveSettings();

                            hideMovingFileModal();
                        }
                    });
                }
            }
        }
        // Check if the installed directory is empty
        fs.readdir(settings.installedDir, function(err, files) {
            if (err) {
                console.log(err);
            } else {
               if (!files.length) {
                    isMoving = false;
                    console.log("file doesn't exist");
                    settings.installedDir = getInstallPath();
                    saveSettings();

                    dialog.showMessageBox(win, { type: "info", title: "No files to be moved", message: "There is no files to be moved from the installed directory, \nSetting the path to the current one instead" }, 
                    (response) => {
                        if(response === 0){

                        }
                    });
               }
            }
        });
    });
}

module.exports.listInstalledVersions = listInstalledVersions;
module.exports.deleteInstalledVersion = deleteInstalledVersion;
module.exports.moveInstalledVersion = moveInstalledVersion;
