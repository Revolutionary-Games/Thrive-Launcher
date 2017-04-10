// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const fs = require('fs');
const path = require('path');
const assert = require('assert');
const request = require('request');
const mkdirp = require('mkdirp');

var {ipcRenderer, remote} = require('electron');

const versionInfo = require('./version_info');

const retrieveNews = require('./retrieve_news');



// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-save-it-and-show-download-progress

function downloadFile(configuration){
    return new Promise(function(resolve, reject){
        // Save variable to know progress
        var received_bytes = 0;
        var total_bytes = 0;

        var req = request({
            method: 'GET',
            uri: configuration.remoteFile
        });

        var out = fs.createWriteStream(configuration.localFile);
        req.pipe(out);

        req.on('response', function ( data ) {
            // Change the total bytes value to get progress later.
            total_bytes = parseInt(data.headers['content-length' ]);
        });

        // Get progress if callback exists
        if(configuration.hasOwnProperty("onProgress")){
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;

                configuration.onProgress(received_bytes, total_bytes);
            });
        }else{
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;
            });
        }

        req.on('end', function() {
            resolve();
        });
    });
}





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


// Buttons
let playButton = document.getElementById("playButton");

let playButtonText = document.getElementById("playText");

playButtonText.textContent = "Retrieving version information...";


playButtonText.addEventListener("click", function(event){

    console.log("play clicked");

    mkdirp("staging/download/", function (err){
        
        if(err){
            
            console.error(err)
            
        } else {

            downloadFile({
                remoteFile: "https://fp.boostslair.com/images/cards/Garry.png",
                localFile: "staging/download/garry.png",
                
                onProgress: function (received, total){
                    
                    var percentage = (received * 100) / total;
                    console.log(percentage + "% | " + received + " bytes out of " + total
                                + " bytes.");
                }
                
            }).then(function(){
                
                alert("File succesfully downloaded");
            });            
        }
    });
    

});

let playComboPopup = document.getElementById("playComboPopup");

playComboPopup.addEventListener("click", function(event){

    console.log("open combo popup");
    
});


//! Called once version info is loaded
function updatePlayButton(){

    playButtonText.textContent = "Processing Version Data...";

    let version = versionInfo.getRecommendedVersion();

    assert(version.stable);

    let dl = versionInfo.getDownloadForPlatform(version.id);

    // Verify retrieve logic
    assert(versionInfo.getCurrentPlatform().compare(versionInfo.getPlatformByID(dl.os)));
    
    playButtonText.textContent = "Play " + version.releaseNum +
        (version.stable ? "(Stable)" : "");

    playButtonText.dataset.versionObj = version;
    playButtonText.dataset.selectedID = version.id;
    playButtonText.dataset.download = dl;

    //console.log("dl: " + dl.url);


    // Dump the other versions to be selected in the combo box thing //
    let options = versionInfo.getAllValidVersions();

    playComboPopup.dataset.options = options;

    console.log("All valid versions: " + options.length);
    
}

let newsContent = document.getElementById("newsContent");

let devForumPosts = document.getElementById("devForumPosts");

//
// Starts loading the news and shows them once loaded
//
function loadNews(){

    retrieveNews.retrieveNews(function(news, devposts){

        assert(news);
        assert(devposts);

        if(news.error){

            newsContent.textContent = news.error;
            
        } else {
            
            assert(news.htmlNodes);
            
            newsContent.innerHTML = "";
            newsContent.append(news.htmlNodes);
        }

        if(devposts.error){

            devForumPosts.textContent = devposts.error;
            
        } else {

            assert(devposts.htmlNodes);

            devForumPosts.innerHTML = "";
            devForumPosts.append(devposts.htmlNodes);
        }

    });
}

// Clear news and start loading them

newsContent.textContent = "Loading...";
devForumPosts.textContent = "Loading...";

loadNews();






