// 
// Functions for working with thrive version objects
//
"use strict";

const stripJsonComments = require('strip-json-comments');
const os = require('os');
const path = require('path');
const url = require('url');

var versionData = null;

// If true automatically finds 32 bit windows alternative if download is missing for x64
const windowsAuto32Bits = true;


function parseData(data){
    
    versionData = JSON.parse(stripJsonComments(data));

    // Set recommended version data //
    for(let tags of versionData.latest){

        if(tags.type == "stable"){
            let ver = getVersionByID(tags.id);

            ver.stable = true;
        }
    }

    // Add folder names to all downloads //
    for(let ver of versionData.versions){

        for(let dl of ver.platforms){

            let parsedUrl = url.parse(dl.url);
            let fileName = path.basename(parsedUrl.pathname);
            
            let ext = path.extname(fileName);
            dl.folderName = path.basename(fileName, ext);
            dl.fileName = fileName;
        }
    }
}

function getCurrentPlatform(){

    return {
        arch: os.arch(),
        os: os.platform(),

        compare: function(other){
            return this.arch == other.arch && this.os == other.os
        }
    };
}

// Helper for getting win 32 bits platform
function getWin32BitPlatform(){

    let obj = getCurrentPlatform();
    obj.arch = "x86";
    obj.platform = "win32";
    
    return obj;
}

function getVersionByID(id){

    for(let ver of versionData.versions){

        if(ver.id == id)
            return ver;
    }

    return null;
}

function getPlatformByID(osID){

    for(let platform of versionData.platforms){

        if(platform.id == osID)
            return platform;
    }

    return null;
}

function getDownloadForPlatform(id, platform = getCurrentPlatform()){

    for(let ver of versionData.versions){

        if(ver.id == id){

            for(let dl of ver.platforms){

                if(platform.compare(getPlatformByID(dl.os))){

                    return dl;
                }
            }

            // Win32 workaround check
            if(windowsAuto32Bits && (os.platform() == "win32" && os.arch() == "x64")){

                let platform = getWin32BitPlatform();
                
                for(let dl of ver.platforms){

                    if(platform.compare(getPlatformByID(dl.os))){

                        return dl;
                    }
                }
            }
            
            return null;
        }
    }

    console.err("version with id not found when looking for platform dl");
    return null;
}

function getRecommendedVersion(type = "stable"){
    
    for(let ver of versionData.latest){

        if(ver.type == type)
            return getVersionByID(ver.id);
    }

    return null;
}

// Returns objects with "version" and "download" items
function getAllValidVersions(platform = getCurrentPlatform()){

    let options = [];

    for(let ver of versionData.versions){

        // Add to options if a valid download is found
        for(let dl of ver.platforms){

            if(platform.compare(getPlatformByID(dl.os))){

                options.push(
                    {
                        version: ver,
                        download: dl
                    });
                
            } else {

                // Win32 workaround check
                if(windowsAuto32Bits && (os.platform() == "win32" && os.arch() == "x64")){

                    let platform = getWin32BitPlatform();
                    
                    if(platform.compare(getPlatformByID(dl.os))){

                        options.push(
                            {
                                version: ver,
                                download: dl,
                                win32On64Bit: true
                            });
                    }
                }
            }
        }
    }
    
    return options;
}


module.exports.getVersionData = function(){ return versionData };
module.exports.parseData = parseData;
module.exports.getRecommendedVersion = getRecommendedVersion;
module.exports.getVersionByID = getVersionByID;
module.exports.getDownloadForPlatform = getDownloadForPlatform;
module.exports.getPlatformByID = getPlatformByID;
module.exports.getCurrentPlatform = getCurrentPlatform;
module.exports.getAllValidVersions = getAllValidVersions;




