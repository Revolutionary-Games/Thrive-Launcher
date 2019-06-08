//
// Helpers for installing and removing versions
//
// TODO: the installing functions are still in renderer.js and should be moved here
const fs = require('fs');
const path = require('path');

const {getVersionData} = require("./version_info.js");

const {installPath} = require("./settings.js");

const isDirectory = source => fs.lstatSync(source).isDirectory();
const getDirectories = source =>
      fs.readdirSync(source).map(name => path.join(source, name)).filter(isDirectory);


// Returns the names of all installed versions. And extra files in the installed folder
function listInstalledVersions(success, error){
    return new Promise((resolve, reject) => {

        const versions = getVersionData();

        const directories = getDirectories(installPath);

        console.log("dirs", directories);

        resolve(directories);
    });
}


module.exports.listInstalledVersions = listInstalledVersions;
