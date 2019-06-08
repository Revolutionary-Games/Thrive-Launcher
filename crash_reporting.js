//
// Functionality for reporting crashes
//
const fs = require('fs');
const { Modal, showGenericError} = require('./modal');

function getCrashDumpsInFolder(folder){
    return new Promise((resolve, reject) => {
        fs.readdir(folder, (err, files) => {

            if(err){
                reject(err);
                return;
            }

            let dumps = [];

            for(let file of files){

                if(file.endsWith(".dmp")){

                    dumps.push(file);
                }
            }

            resolve(dumps);
        });
    });
}

let currentReportSettings = {};

const crashReporterModal = new Modal("crashReportingModal", "crashReportingModalDialog", {
    closeButton: "crashReportingClose",
    autoClose: false,
    // TODO: confirm cancel report
    // onClose: 
});

let crashReportingContent = document.getElementById("crashReportingContent");

function onReporterOpened(settings){

    crashReportingContent.innerHTML = "";

    // List dumps
    let ul = document.createElement("ul");

    for(let dump of settings.dumps){

        let li = document.createElement("li");
        let span = document.createElement("span");
        span.append(document.createTextNode(dump));

        li.append(span);
        ul.append(li);
    }

    crashReportingContent.append(ul);
}

function showDumpsDialog(dumpFolder, dumps, exitCode){
    
    crashReporterModal.show();

    let settings = {
        dumpFolder: dumpFolder,
        dumps: dumps,
        exitCode: exitCode,
    };

    onReporterOpened(settings);    
}

// Called when Thrive exits
function onGameEnded(binFolder, exitCode, buttonContainer){
    // Look for .dmp files
    getCrashDumpsInFolder(binFolder).then(dumps => {

        if(dumps.length > 0){

            console.log("thrive has generated crash dump(s)");

            let button = document.createElement("span");
            button.classList.add("AfterPlayReport");
            button.append(document.createTextNode("Report Crash"));

            button.addEventListener("click", function(event){

                showDumpsDialog(binFolder, dumps, exitCode);
            });
            
            buttonContainer.append(button);
        }
        
    }).catch(err => {

        console.error("failed to read files for crash dump detection", err);
    });    
}

module.exports.onGameEnded = onGameEnded;
