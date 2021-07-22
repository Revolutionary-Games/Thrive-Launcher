//
// Functions for working with thrive version objects
//
"use strict";

const stripJsonComments = require("strip-json-comments");
const os = require("@electron/remote").require("os");
const path = require("path");
const url = require("url");

let versionData = null;

// If true automatically finds 32 bit windows alternative if download is missing for x64
const windowsAuto32Bits = true;


function parseData(data){

    versionData = JSON.parse(stripJsonComments(data));

    // Set recommended version data //
    for(const tags of versionData.latest){

        if(tags.type == "stable"){
            const ver = getVersionByID(tags.id);

            if(ver){

                ver.stable = true;
            } else {
                console.error("Invalid JSON version data, latest is not found");
            }
        }
    }

    // Add folder names to all downloads //
    for(const ver of versionData.versions){

        for(const dl of ver.platforms){

            const parsedUrl = url.parse(dl.url);
            const fileName = path.basename(parsedUrl.pathname);

            const ext = path.extname(fileName);
            dl.folderName = path.basename(fileName, ext);
            dl.fileName = fileName;

            dl.getDescriptionString = function(){

                return getPlatformByID(this.os).name;
            };
        }

        // Add info string method //
        ver.getDescriptionString = function(){

            return this.releaseNum + (ver.stable ? " (Current)" : "");
        };
    }
}

function getCurrentPlatform(){

    return {
        arch: os.arch(),
        os: os.platform(),

        // For testing
        // arch: "x64",
        // os: "win32",

        compare: function(other){
            return this.arch == other.arch && this.os == other.os;
        },
    };
}

// Helper for getting win 32 bits platform
function getWin32BitPlatform(){

    const obj = getCurrentPlatform();
    obj.arch = "ia32";
    obj.platform = "win32";

    return obj;
}

function useWin32Workaround(platform){
    return windowsAuto32Bits && (platform.os == "win32" && platform.arch == "x64");
}

function getVersionByID(id){

    for(const ver of versionData.versions){

        if(ver.id == id)
            return ver;
    }

    return null;
}

function getPlatformByID(osID){

    for(const platform of versionData.platforms){

        if(platform.id == osID)
            return platform;
    }

    return null;
}

function getPlatformForCurrentPlatform(currentPlatform = getCurrentPlatform()){
    for(const platform of versionData.platforms){

        if(currentPlatform.compare(platform))
            return platform;
    }

    return null;
}

function getDownloadForPlatform(id, platform = getCurrentPlatform()){

    for(const ver of versionData.versions){

        if(ver.id == id){

            for(const dl of ver.platforms){

                if(platform.compare(getPlatformByID(dl.os))){

                    return dl;
                }
            }

            // Win32 workaround check
            if(useWin32Workaround(platform)){

                const platform = getWin32BitPlatform();

                for(const dl of ver.platforms){

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

function getDownloadByOSID(id, osid){

    for(const ver of versionData.versions){

        if(ver.id == id){

            for(const dl of ver.platforms){

                if(osid == dl.os){

                    return dl;
                }
            }
        }
    }

    return null;
}

function getRecommendedVersion(type = "stable"){

    for(const ver of versionData.latest){

        if(ver.type == type)
            return getVersionByID(ver.id);
    }

    return null;
}

// Returns objects with "version" and "download" items
function getAllValidVersions(platform = getCurrentPlatform()){

    const options = [];

    for(const ver of versionData.versions){

        // Add to options if a valid download is found
        for(const dl of ver.platforms){

            if(platform.compare(getPlatformByID(dl.os))){

                options.push({
                    version: ver,
                    download: dl,
                });

            } else if(useWin32Workaround(platform)){
                // Win32 workaround

                const platform = getWin32BitPlatform();

                if(platform.compare(getPlatformByID(dl.os))){

                    options.push({
                        version: ver,
                        download: dl,
                        win32On64Bit: true,
                    });
                }
            }
        }
    }

    return options;
}


module.exports.getVersionData = function(){
    return versionData;
};
module.exports.parseData = parseData;
module.exports.getRecommendedVersion = getRecommendedVersion;
module.exports.getVersionByID = getVersionByID;
module.exports.getDownloadForPlatform = getDownloadForPlatform;
module.exports.getPlatformByID = getPlatformByID;
module.exports.getPlatformForCurrentPlatform = getPlatformForCurrentPlatform;
module.exports.getCurrentPlatform = getCurrentPlatform;
module.exports.getAllValidVersions = getAllValidVersions;
module.exports.getLauncherMeta = () => {
    return {
        latestVersion: versionData["launcher-meta"].latestVersion,
        releaseDLURL: versionData["launcher-meta"].dlURL,
    };
};
module.exports.getDownloadByOSID = getDownloadByOSID;

