// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const fs = require('fs');
const path = require('path');
const assert = require('assert');

var {ipcRenderer, remote} = require('electron');

const versionInfo = require('./version_info');


//! Parses version information from data and adds it to all the places
function onVersionDataReceived(data){

    versionInfo.parseData(data);

    assert(versionInfo.getVersionData().versions);
    
    updatePlayButton();
}


fs.readFile(path.join(remote.app.getAppPath(), 'test/data/thrive_versions.json'),
            "utf8",
            function (err,data){
                
                if (err) {
                    return console.log(err);
                }

                onVersionDataReceived(data);
            });


document.getElementById("text").textContent =
    "This would be result of some discourse API call.";

// Buttons
let playButton = document.getElementById("playButton");

let playButtonText = document.getElementById("playText");

playButtonText.textContent = "Retrieving version information...";

//! Called once version info is loaded
function updatePlayButton(){

    let version = versionInfo.getRecommendedVersion();

    let dl = versionInfo.getDownloadForPlatform(version.id);
    
    playButtonText.textContent = "Play " + version.releaseNum +
        "(Current)";

    console.log("dl: " + dl.url);
    
}

playButtonText.addEventListener("click", function(event){

    console.log("play clicked");
    
});

console.log("play clicked");


let playComboPopup = document.getElementById("playComboPopup");

playComboPopup.addEventListener("click", function(event){

    console.log("open combo popup");
    
});






