// 
// Functions for working with thrive version objects
//
const stripJsonComments = require('strip-json-comments');
const os = require('os');

var versionData = null;

function parseData(data){
    
    versionData = JSON.parse(stripJsonComments(data));

};

function getCurrentPlatform(){

    return {
        arch: os.arch(),
        os: os.platform(),

        compare: function(other){
            return this.arch == other.arch && this.os == other.os
        }
    };
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


module.exports.getVersionData = function(){ return versionData };
module.exports.parseData = parseData;
module.exports.getRecommendedVersion = getRecommendedVersion;
module.exports.getVersionByID = getVersionByID;
module.exports.getDownloadForPlatform = getDownloadForPlatform;

