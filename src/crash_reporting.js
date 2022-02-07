//
// Functionality for reporting crashes
//
"use strict";

const log = require("electron-log");
const remote = require("@electron/remote");

const win = remote.getCurrentWindow();

const fs = remote.require("fs");
const path = require("path");
const url = require("url");
const os = remote.require("os");

const {shell} = remote;

const moment = require("moment");
const request = remote.require("request");

const {Modal, showGenericError} = require("./modal");
const {startError} = require("./error_suggestions");

const logFilenamesToCheck = ["ThriveLog.txt", "log.txt"];

const {
    devCenterURL, autoCloseMinimumGameDuration, crashDumpRegex, maxCrashLogFileSize,
} = require("./config");
const globalSettings = require("./settings").settings;
const {getCurrentPlatform} = require("./version_info");

const devCenterReportAPI = url.resolve(devCenterURL, "/api/v1/crashReport");

function getPlatformThriveDataFolder(){
    const platform = getCurrentPlatform();

    if(platform.os === "win32"){
        return path.join(remote.app.getPath("appData"), "Thrive");
    } else if(platform.os === "linux"){
        // We kind of have to assume the right path here... as "home" probably doesn't take
        // XDG_HOME into account
        return path.join(remote.app.getPath("home"), ".local", "share", "Thrive");
    } else {
        log.warn("Can't detect default Thrive data folder for platform:", platform.os);
        return path.join(remote.app.getPath("appData"), "Thrive");
    }
}

function getDefaultLogsFolder(){
    return path.join(getPlatformThriveDataFolder(), "logs");
}

function getPlatformDefaultThriveCrashesFolder(){
    return path.join(getPlatformThriveDataFolder(), "crashes");
}

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

    settings.submit.textContent = "Creating request...";

    // Get selected log file contents

    let logs = "";

    try{

        for(const log of settings.selectedLogs){

            const content = fs.readFileSync(log.path, {encoding: "utf-8"});

            logs += "==== START OF " + log.name + " ===\n" +
                content.substr(0, maxCrashLogFileSize) +
                "=== END OF " + log.name + " ====";

            if(content.length > maxCrashLogFileSize){
                logs += " Previous file was truncated due to original length being:" +
                    ` ${content.length} `;
            }
        }

    } catch(err){
        showGenericError("Failed to read log files: " + err);
        settings.uploading = false;
        return;
    }

    const formData = {
        exitCode: "" + settings.exitCode,
        crashTime: "" + Math.floor(settings.selectedDump.mtimeMs / 1000),
        public: settings.public ? "true" : "false",
        logFiles: logs,
        platform: os.platform(),
        dump: fs.createReadStream(settings.selectedDump.path),
    };

    if(settings.store){
        formData.store = settings.store;

    } else {
        formData.gameVersion = settings.gameVersion;
    }

    if(settings.extraDescription)
        formData.extraDescription = settings.extraDescription;

    if(settings.email)
        formData.email = settings.email;

    settings.submit.textContent = "Sending request...";

    log.debug("sending crash report to:", devCenterReportAPI);

    request.post({url: devCenterReportAPI, formData: formData},
        function(err, httpResponse, body){
            let data = {};

            try{
                data = JSON.parse(body);
            } catch(ignore){
                if(!err)
                    err = "JSON parsing failed";
            }

            if(err || !httpResponse || httpResponse.statusCode !== 201){

                if(!err)
                    err = data.error || data.body;

                if((!err || err === "JSON parsing failed") && body){
                    log.info("Replacing JSON parse error with body");
                    err = body.toString().substr(0, 100);
                }

                if(!httpResponse){
                    log.error("error in creating report: err:", err,
                        "response:", httpResponse, "body:", body);

                    settings.statusText.textContent =
                        "Error sending request, please try again later. error: " +
                        err;
                } else {
                    log.error("error in creating report: err:", err, "status:",
                        httpResponse.statusCode, "response:", httpResponse, "body:", body);

                    settings.statusText.textContent =
                        "Error sending request, please try again later. " +
                        "status code: " + httpResponse.statusCode + " error: " +
                        err;
                }

                settings.submit.textContent = "Retry";
                settings.uploading = false;
                return;
            }

            log.info("successfully created report:", body);
            onSuccess(url.resolve(devCenterURL, "/reports/" + data.createdId),
                url.resolve(devCenterURL, "/deleteReport/" + data.deleteKey));
        });

    settings.submit.textContent = "Waiting for server response...";

    settings.statusText.textContent = "Please allow up to a few minutes for your report to " +
        "be uploaded";
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
            // TODO: this doesn't seem to work on Linux anymore...
            shell.openPath(log.path).then((error) => {
                if(!error)
                    return;

                log.error(`Failed to open file (${log.path}) for viewing due to:`, error);
            });
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

function findAllLogFiles(settings){
    setLogFileStatus(settings, "Searching for log files");

    findLogsInFolder(getDefaultLogsFolder()).then((logs) => {
        if(settings.detectedLogFile && fs.existsSync(settings.detectedLogFile)){
            log.debug("Using detected log file from game output");
            settings.selectedLogs = [
                {
                    name: path.basename(settings.detectedLogFile),
                    path: settings.detectedLogFile,
                },
            ];
        } else {
            settings.selectedLogs = logs;
        }

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

    findAllLogFiles(settings);

    crashReportingContent.append(settings.logsWidget);

    const rescanButton = document.createElement("span");
    rescanButton.textContent = "Scan again for logs";
    rescanButton.classList.add("BottomButton");

    rescanButton.addEventListener("click", function(){

        findAllLogFiles(settings);
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

function createDumpControls(dump, parent, settings){
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
    parent.append(li);
}

function onReporterOpened(settings){

    crashReportingContent.innerHTML = "";

    crashReportingContent.append(document.createElement("br"));

    crashReportingContent.append(document.createTextNode("Select a crash to report"));

    // List dumps
    const ul = document.createElement("ul");

    // Extra dump is detected from the latest run so it is always shown first
    if(settings.extraDump){
        createDumpControls(settings.extraDump, ul, settings);
    }

    for(const dump of settings.dumps){
        createDumpControls(dump, ul, settings);
    }

    crashReportingContent.append(ul);

    if(settings.dumps.length < 1 && !settings.extraDump)
        return;

    const button = document.createElement("span");
    button.classList.add("VersionDeleteButton");
    button.append(document.createTextNode("Delete All Crashdumps"));

    button.addEventListener("click", function(){

        new Promise((resolve) => {

            log.debug("deleting crash report related stuff");

            for(const dump of settings.dumps){
                fs.unlinkSync(dump.path);
            }

            if(settings.extraDump){
                // Extra dump may be included in the dumps list so it may be already deleted
                if(fs.existsSync(settings.extraDump))
                    fs.unlinkSync(settings.extraDump);
                settings.extraDump = null;
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

function showDumpsDialog(dumpFolder, exitCode, gameVersion, store, keptOutput,
    detectedLogFile, extraDump){

    crashReporterModal.show();
    crashReportingContent.innerHTML = "Finding dump files";

    if(!exitCode)
        exitCode = "unknown";

    if(extraDump){
        // Convert the extra dump path to a proper dump object
        try{
            const stats = fs.lstatSync(extraDump);
            extraDump = {
                name: "(detected from output) " + path.basename(extraDump),
                path: extraDump, mtimeMs: stats.mtimeMs,
            };
        } catch(error){
            log.warn("Could not add extra dump to list of dumps:", error);
            extraDump = null;
        }
    }

    const settings = {
        dumpFolder: dumpFolder,
        dumps: [],
        exitCode: exitCode,
        gameVersion: gameVersion,
        store: store,
        keptOutput: keptOutput,
        detectedLogFile: detectedLogFile,
        extraDump: extraDump,
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
// gameVersion is not usable for store releases reporting, as it is a magic constant,
// so use the store if that is not-false
function onGameEnded(binFolder, exitCode, buttonContainer, gameVersion, store, adviceBox,
    elapsed, keptOutput, detectedLogFile){
    // Detect problems

    // Detect crash dumps logged by the game
    let detectedDump = null;

    if(keptOutput){
        const match = keptOutput.match(crashDumpRegex);

        if(match){
            detectedDump = match[1];

            if(!fs.existsSync(detectedDump)){
                log.error("Game printed out non-existent crash dump file:", detectedDump);
                detectedDump = null;
            } else {
                log.info("Detected crash dump created by the game:", detectedDump);
            }
        }
    }

    startError(exitCode, "", adviceBox);

    let crashesFolder = getPlatformDefaultThriveCrashesFolder();

    if(detectedLogFile){
        log.debug("Log file location detected as:", detectedLogFile);
        crashesFolder = path.join(detectedLogFile, "..", "..", "crashes");
    } else {
        log.warn("No log file location could be detected from game output");
    }

    // Look for .dmp files
    getCrashDumpsInFolder(crashesFolder).then((dumps) => {

        if(dumps.length > 0 || detectedDump){

            log.info("thrive has generated crash dump(s)");

            const button = document.createElement("span");
            button.classList.add("AfterPlayReport");
            button.append(document.createTextNode("Report Crash"));

            button.addEventListener("click", function(){

                showDumpsDialog(crashesFolder, exitCode, gameVersion, store, keptOutput,
                    detectedLogFile, detectedDump);
            });

            buttonContainer.append(button);
        } else if(globalSettings.closeLauncherAfterGameExit){
            if(elapsed < autoCloseMinimumGameDuration){
                log.warn("Game ran so little time that there was likely a problem " +
                    "not closing the launcher automatically");
            } else {
                log.info("Closing launcher after game exit (without error to report) as" +
                    " configured");
                win.close();
            }
        }

    }).catch((err) => {

        log.error("failed to read files for crash dump detection:", err);
    });
}

module.exports.onGameEnded = onGameEnded;
