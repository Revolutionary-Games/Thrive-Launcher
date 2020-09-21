//
// Functionality for reporting crashes
//
"use strict";

const remote = require("electron").remote;

const fs = remote.require("fs");
const path = require("path");
const url = require("url");

const {shell} = remote;

const moment = require("moment");
const request = require("request");

const {Modal, showGenericError} = require("./modal");

const logFilenamesToCheck = ["ThriveLog.txt", "ThriveLogCEF.txt", "ThriveLogOGRE.txt"];

const {devCenterURL} = require("./config");

const devCenterReportAPI = url.resolve(devCenterURL, "/api/v1/crash_report");

function getCrashDumpsInFolder(folder){
    return new Promise((resolve, reject) => {
        fs.readdir(folder, (err, files) => {

            if(err){
                reject(err);
                return;
            }

            const dumps = [];

            for(const file of files){

                if(file.endsWith(".dmp")){

                    const data = {name: file, path: path.join(folder, file)};

                    try{
                        const stats = fs.lstatSync(data.path);
                        data.mtimeMs = stats.mtimeMs;
                    } catch(err){
                        continue;
                    }

                    dumps.push(data);
                }
            }

            // Sort by time
            dumps.sort((a, b) => b.mtimeMs - a.mtimeMs);

            resolve(dumps);
        });
    });
}

function findLogsInFolder(folder){
    return new Promise((resolve, reject) => {
        fs.readdir(folder, (err, files) => {

            if(err){
                reject(err);
                return;
            }

            const logs = [];

            for(const file of files){
                if(logFilenamesToCheck.includes(file)){

                    logs.push({name: file, path: path.join(folder, file)});
                }
            }

            resolve(logs);
        });
    });
}

const crashReporterModal = new Modal("crashReportingModal", "crashReportingModalDialog", {
    closeButton: "crashReportingClose",
    autoClose: false,

    // TODO: confirm cancel report
    // onClose:
});

function formatTime(ms){
    return moment(ms).format("L LTS");
}

const crashReportingContent = document.getElementById("crashReportingContent");

function onAgreeChanged(value, settings){

    settings.agreed = value;

    if(settings.agreed){

        settings.submit.classList.remove("Disabled");
    } else {

        settings.submit.classList.add("Disabled");
    }
}

function onTrySubmit(settings){

    if(!settings.agreed || settings.uploading)
        return;

    settings.uploading = true;

    // Console.log("Starting reporting crash", settings);
    settings.submit.textContent = "Creating request...";

    // Get selected log file contents

    let logs = "";

    try{

        for(const log of settings.selectedLogs){

            logs += "==== START OF " + log.name + " ===\n" + fs.readFileSync(log.path) +
                "=== END OF " + log.name + " ====";
        }

    } catch(err){
        showGenericError("Failed to read log files: " + err);
        settings.uploading = false;
        return;
    }

    const formData = {
        exit_code: "" + settings.exitCode,
        crash_time: "" + Math.floor(settings.selectedDump.mtimeMs / 1000),
        public: "" + settings.public,
        log_files: logs,
        game_version: settings.gameVersion,
        dump: fs.createReadStream(settings.selectedDump.path),
    };

    if(settings.extraDescription)
        formData.extra_description = settings.extraDescription;

    if(settings.email)
        formData.email = settings.email;

    settings.submit.textContent = "Sending request...";

    request.post({url: devCenterReportAPI, formData: formData},
        function(err, httpResponse, body){
            let data = {};

            try{
                data = JSON.parse(body);
            } catch(ignore){
                err = "JSON parsing failed";
            }

            if(err || httpResponse.statusCode != 201){

                console.log("error in creating report: err:", err, "status:",
                    httpResponse.statusCode, "response:", httpResponse, "body:", body);

                if(!err)
                    err = data.error;

                settings.statusText.textContent =
                    "Error sending request, please try again later. " +
                    "status code: " + httpResponse.statusCode + " error: " +
                    err;
                settings.submit.textContent = "Retry";
                settings.uploading = false;
                return;
            }

            console.log("successfully created report:", body);
            onSuccess(url.resolve(devCenterURL, "/report/" + data.created_id),
                url.resolve(devCenterURL, "/delete_report/" + data.delete_key));
        });

    settings.submit.textContent = "Waiting for server response...";

    settings.statusText.textContent = "Please allow up to a minute for your report to " +
        "be processed";
}

function onSuccess(reportURL, privateURL){

    crashReportingContent.innerHTML = "";

    crashReportingContent.
        append(document.createTextNode("Your report has been successfully " +
            "submitted. Thank you for your report."));

    crashReportingContent.append(document.createElement("hr"));

    crashReportingContent.append(document.createTextNode("You can view your report, " +
        "if it is public, here: "));

    const reportLink = document.createElement("a");
    reportLink.textContent = reportURL;
    reportLink.href = reportURL;

    crashReportingContent.append(reportLink);

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.append(document.createTextNode("If you want to delete your report " +
        "you can do so here: "));

    const privateLink = document.createElement("a");
    privateLink.textContent = privateURL;
    privateLink.href = privateURL;

    crashReportingContent.append(privateLink);

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.
        append(document.createTextNode("IMPORTANT if you lose your delete " +
            "link you won't be able to delete your " +
            "report. So save it!"));


    crashReportingContent.append(document.createElement("br"));
    crashReportingContent.append(document.createElement("br"));
    crashReportingContent.append(document.
        createTextNode("You can now safely close this reporter."));
}

function updateShownLogFiles(settings){
    settings.logsWidget.innerHTML = "";

    for(const log of settings.selectedLogs){
        const li = document.createElement("li");
        const span = document.createElement("span");
        span.append(document.createTextNode(log.name));

        const folderLink = document.createElement("a");
        folderLink.textContent = "View in folder";
        folderLink.href = "#";
        folderLink.style.paddingLeft = "15px";
        folderLink.style.paddingRight = "15px";
        folderLink.addEventListener("click", function(){
            shell.showItemInFolder(log.path);
        });

        span.append(folderLink);

        const fileLink = document.createElement("a");
        fileLink.textContent = "View file";
        fileLink.href = "#";
        fileLink.style.paddingRight = "15px";
        fileLink.addEventListener("click", function(){
            shell.openPath(log.path);
        });

        span.append(fileLink);

        const removeLink = document.createElement("a");
        removeLink.textContent = "Remove";
        removeLink.href = "#";
        removeLink.addEventListener("click", function(){

            settings.selectedLogs = settings.selectedLogs.filter((value) => value != log);
            updateShownLogFiles(settings);
        });

        span.append(removeLink);

        li.append(span);
        settings.logsWidget.append(li);
    }
}

function setLogFileStatus(settings, status){
    const li = document.createElement("li");
    li.textContent = status;
    settings.logsWidget.innerHTML = "";
    settings.logsWidget.append(li);
}

function findALlLogFiles(settings){
    setLogFileStatus(settings, "Searching for log files");

    findLogsInFolder(settings.dumpFolder).then((logs) => {

        settings.selectedLogs = logs;
        updateShownLogFiles(settings);

    }).catch((err) => {

        setLogFileStatus(settings, "Error finding log files: " + err);
    });
}

function onBeginReportingCrash(dump, settings){

    settings.selectedDump = dump;

    crashReportingContent.innerHTML = "";
    crashReportingContent.append(document.createTextNode("reporting crash: " + dump.name +
        " " + formatTime(dump.mtimeMs)));

    crashReportingContent.append(document.createElement("hr"));

    //
    // Logs part
    //

    crashReportingContent.
        append(document.createTextNode("Please keep the logs included if they are from the " +
            "same run as the crash. " +
            "You can click the name of the file to view the folder it is in and edit it to " +
            "remove any personal details you want."));


    settings.logsWidget = document.createElement("ul");

    findALlLogFiles(settings);

    crashReportingContent.append(settings.logsWidget);

    const rescanButton = document.createElement("span");
    rescanButton.textContent = "Scan again for logs";
    rescanButton.classList.add("BottomButton");

    rescanButton.addEventListener("click", function(){

        findALlLogFiles(settings);
    });

    crashReportingContent.append(rescanButton);

    crashReportingContent.append(document.createElement("hr"));

    //
    // Extra info part
    //

    crashReportingContent.append(document.createTextNode("Describe what you were doing when " +
        "the crash happened (optional)"));
    crashReportingContent.append(document.createElement("br"));

    const extraDescription = document.createElement("textarea");
    extraDescription.classList.add("Report");

    extraDescription.addEventListener("change", function(event){

        settings.extraDescription = event.target.value;
    });

    crashReportingContent.append(extraDescription);

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.
        append(document.createTextNode("Your email, if you want to " +
            "receive updates about this report (optional):"));

    const optionalEmail = document.createElement("input");
    optionalEmail.type = "text";
    optionalEmail.classList.add("Report");

    optionalEmail.addEventListener("change", function(event){

        settings.email = event.target.value;
    });

    crashReportingContent.append(optionalEmail);

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.
        append(document.createTextNode("Your email will only be visible " +
            "to ThriveDevCenter administrators."));

    //
    // Bottom part
    //
    crashReportingContent.append(document.createElement("hr"));


    const publicBox = document.createElement("input");
    publicBox.type = "checkbox";
    publicBox.checked = true;
    settings.public = true;
    publicBox.classList.add("Report");

    publicBox.addEventListener("change", function(event){

        settings.public = event.target.checked;
    });

    crashReportingContent.append(publicBox);
    crashReportingContent.
        append(document.createTextNode("Public. Public reports have " +
            "their crash callstack and description publicly " +
            "visible. Private reports are only visible to developers. Log files are always " +
            "only visible to developers."));

    crashReportingContent.append(document.createElement("br"));

    const agreeBox = document.createElement("input");
    agreeBox.type = "checkbox";
    agreeBox.classList.add("Report");

    agreeBox.addEventListener("change", function(event){

        onAgreeChanged(event.target.checked, settings);
    });

    crashReportingContent.append(agreeBox);

    crashReportingContent.
        append(document.createTextNode("I agree that the information " +
            "listed on this form, including any included files and " +
            "the crash dump, along with my IP address  will be stored in the Thrive " +
            "crash database. And if this report is public anyone can read the crash " +
            "callstack and description."));


    const submitContainer = document.createElement("div");
    submitContainer.style.textAlign = "center";

    settings.submit = document.createElement("span");
    settings.submit.classList.add("Submit");
    settings.submit.classList.add("Disabled");
    settings.submit.textContent = "Submit";

    settings.submit.addEventListener("click", function(){
        onTrySubmit(settings);
    });

    submitContainer.append(settings.submit);

    crashReportingContent.append(submitContainer);

    settings.statusText = document.createElement("span");
    crashReportingContent.append(settings.statusText);
}

function onReporterOpened(settings){

    crashReportingContent.innerHTML = "";

    crashReportingContent.
        append(document.
            createTextNode("If the game crashed on startup please, try the " +
                "thrive launch options first before " +
                "reporting a crash. They can be found in the launcher " +
                "options menu (button left of 'play')."));

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.append(document.createTextNode("Select a crash to report"));

    // List dumps
    const ul = document.createElement("ul");

    for(const dump of settings.dumps){

        const li = document.createElement("li");
        const span = document.createElement("span");
        span.append(document.createTextNode(moment(dump.mtimeMs).fromNow() + " (" +
            formatTime(dump.mtimeMs) + ") "));

        const a = document.createElement("a");
        a.textContent = dump.name;
        a.href = "#";
        a.style.paddingLeft = "4px";
        a.addEventListener("click", function(){

            onBeginReportingCrash(dump, settings);
        });

        span.append(a);

        li.append(span);
        ul.append(li);
    }

    crashReportingContent.append(ul);

    if(settings.dumps.length < 1)
        return;

    const button = document.createElement("span");
    button.classList.add("VersionDeleteButton");
    button.append(document.createTextNode("Delete All Crashdumps"));

    button.addEventListener("click", function(){

        new Promise((resolve) => {

            console.log("deleting stuff");

            for(const dump of settings.dumps){
                fs.unlinkSync(dump.path);
            }

            resolve();

        }).then(() => {

            crashReporterModal.hide();

            // This is not rechecked before running the game again
            settings.dumps = [];

        }).catch((err) => {

            showGenericError("Failed to delete some files. " + err);
        });
    });

    crashReportingContent.append(button);
}

function showDumpsDialog(dumpFolder, exitCode, gameVersion){

    crashReporterModal.show();
    crashReportingContent.innerHTML = "Finding dump files";

    const settings = {
        dumpFolder: dumpFolder,
        dumps: [],
        exitCode: exitCode,
        gameVersion: gameVersion,
    };

    // The dumps are searched for again here as otherwise the delete
    // button leaves a bunch of things showing
    getCrashDumpsInFolder(dumpFolder).then((dumps) => {

        settings.dumps = dumps;
        onReporterOpened(settings);

    }).catch(() => {
        onReporterOpened(settings);
    });
}

// Called when Thrive exits
function onGameEnded(binFolder, exitCode, buttonContainer, gameVersion){
    // Look for .dmp files
    getCrashDumpsInFolder(binFolder).then((dumps) => {

        if(dumps.length > 0){

            console.log("thrive has generated crash dump(s)");

            const button = document.createElement("span");
            button.classList.add("AfterPlayReport");
            button.append(document.createTextNode("Report Crash"));

            button.addEventListener("click", function(){

                showDumpsDialog(binFolder, exitCode, gameVersion);
            });

            buttonContainer.append(button);
        }

    }).catch((err) => {

        console.error("failed to read files for crash dump detection", err);
    });
}

module.exports.onGameEnded = onGameEnded;
