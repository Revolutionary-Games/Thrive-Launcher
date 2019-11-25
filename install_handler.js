//
// Helpers for installing and removing versions
//
// TODO: the installing functions are still in renderer.js and should be moved here
"use strict";

const fs = require("fs-extra");
const path = require("path");
const rimraf = require("rimraf");

const {getVersionData} = require("./version_info.js");

const {settings} = require("./settings.js");

const isDirectory = (source) => fs.lstatSync(source).isDirectory();
const getDirectories = (source) =>
    fs.readdirSync(source).map((name) => path.join(source, name)).filter(isDirectory);

// Returns the names of all installed versions. And extra files in the installed folder
function listInstalledVersions(){
    return new Promise((resolve) => {

        const versions = getVersionData().versions;

        let directories = null;

        try{
            directories = getDirectories(settings.installPath);
        } catch(err){
            // Ignore error regarding the install directory not existing
            if(err.code == "ENOENT"){
                resolve({});
                return;
            }
            throw err;
        }

        const result = {};

        for(const dir of directories){

            const name = path.basename(dir);
            let good = false;

            for(const ver of versions){
                for(const dl of ver.platforms){

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

        const finalPath = path.join(settings.installPath, name);

        if(!fs.existsSync(finalPath)){
            reject(new Error("path for version doesn't exist: " + finalPath));
            return;
        }

        rimraf(finalPath, (error) => {
            if(error){

                reject(new Error("failed to delete, error: " + error));
                return;
            }

            resolve();
        });
    });
}

module.exports.listInstalledVersions = listInstalledVersions;
module.exports.deleteInstalledVersion = deleteInstalledVersion;
