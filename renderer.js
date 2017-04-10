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

const  url = require("url");

const $ = require('jquery');

// http://ourcodeworld.com/articles/read/228/how-to-download-a-webfile-with-electron-save-it-and-show-download-progress
// With some modifications
function downloadFile(configuration){
    return new Promise(function(resolve, reject){
        // Save variable to know progress
        var received_bytes = 0;
        var total_bytes = 0;

        var req = request({
            method: 'GET',
            uri: configuration.remoteFile
        });

        var out = fs.createWriteStream(configuration.localFile, { encoding: null });
        //req.pipe(out);

        let contentType = "unknown";

        req.on('response', function ( data ) {
            // Change the total bytes value to get progress later.
            total_bytes = parseInt(data.headers['content-length' ]);

            contentType = data.headers['content-type'];
        });

        // Get progress if callback exists
        if(configuration.hasOwnProperty("onProgress")){
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;

                configuration.onProgress(received_bytes, total_bytes);

                out.write(chunk)
            });
        }else{
            req.on('data', function(chunk) {
                // Update the received bytes
                received_bytes += chunk.length;
                out.write(chunk)
            });
        }

        req.on('end', function() {
            out.end();
            resolve(contentType);
        });

        req.on('error', function(err){

            out.end();
            fs.unlinkSync(configuration.localFile);
            reject(err);
            
        });
    });
}





//! Parses version information from data and adds it to all the places
function onVersionDataReceived(data){

    versionInfo.parseData(data);

    assert(versionInfo.getVersionData().versions);
    
    updatePlayButton();
}

// Load dummy version data //
fs.readFile(path.join(remote.app.getAppPath(), 'test/data/thrive_versions.json'),
            "utf8",
            function (err,data){
                
                if (err) {
                    return console.log(err);
                }

                onVersionDataReceived(data);
            });



// Get the modal
let modal = document.getElementById('myModal');

let modalDialog = document.getElementById('myModalDialog');

// Get the <span> element that closes the modal
let span = document.getElementsByClassName("close")[0];

// When the user clicks on <span> (x), close the modal
span.onclick = function() {

    $( modalDialog ).slideUp( "fast", function() {
        modal.style.display = "none";
    });
}

// When the user clicks anywhere outside of the modal, close it
window.onclick = function(event) {
    if (event.target == modal) {

        $( modalDialog ).slideUp( "fast", function() {
            modal.style.display = "none";
        });
       
    }
}





// Buttons
let playButton = document.getElementById("playButton");

let playButtonText = document.getElementById("playText");

playButtonText.textContent = "Retrieving version information...";


playButtonText.addEventListener("click", function(event){

    // Open play modal thing
    modal.style.display = "block";
    
    $( modalDialog ).slideDown( "fast", function() {
        // Animation complete.
    });
    
    

    console.log("play clicked");

    assert(playButtonText.dataset.selectedID);

    let version = versionInfo.getVersionByID(playButtonText.dataset.selectedID);

    assert(version);

    let download = versionInfo.getDownloadForPlatform(version.id);

    assert(download);

    console.log("Playing thrive version: " + version.releaseNum);

    let parsedUrl = url.parse(download.url);
    let fileName = path.basename(parsedUrl.pathname);
    
    const dlPath = "staging/download/";


    mkdirp(dlPath, function (err){

        const localTarget = dlPath + fileName;
        
        if(err){
            
            console.error(err)
            
        } else {

            console.log("skipping dl because testing");
            return;
            downloadFile({
                remoteFile: download.url,
                localFile: localTarget,
                
                onProgress: function (received, total){
                    
                    var percentage = (received * 100) / total;
                    //console.log(percentage + "% | " + received + " bytes out of " + total
                    //            + " bytes.");
                }
                
            }).then(function(contentType){
                
                if(![ "application/x-7z-compressed",
                      "application/zip",
                      "application/octet-stream"].includes(contentType))
                {

                    console.error("download type is wrong: " + contentType);
                    fs.unlinkSync(localTarget);
                    alert("type not supported: " + contentType);
                    return;
                }
                
                alert("File succesfully downloaded");
                
            }, function(error){
                
                console.log("DL failed: " + error);
                alert("DL failed: " + error);
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

    playButtonText.dataset.selectedID = version.id;

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









