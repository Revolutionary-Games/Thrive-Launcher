// Provides suggestions for errors
"use strict";

//! \param suggestionText is parsed as html so it SHOULDN'T contain any user input
function appendSuggestion(suggestionText, element){

    const div = document.createElement("div");

    div.classList.add("ErrorSuggestions");

    div.append(document.createTextNode("Suggestions:"));
    div.append(document.createElement("br"));

    $(div).append($.parseHTML(suggestionText));


    element.append(div);
}

function unpackError(message, element){
    if(!message)
        return;

    if(message.includes("ENOENT")){
        // ENOENT, missing 32bit support

        appendSuggestion("ENOENT error can mean that you are missing 32-bit library support. \
You should try installing 'glibc.i686' or if you are on ubuntu follow instructions here: \
<a href='https://blog.teststation.org/ubuntu/2016/05/12/\
installing-32-bit-software-on-ubuntu-16.04/'>Installing 32-bit libraries on Ubuntu 16.04</a>. \
If these don't help try searching for 'YOUROSHERE install 32 bit library support'. You can\
 also install p7zip package and restart the launcher to use the system version.", element);
    }
}

function startError(exitCode, lastOutput, element){
    // Do a bit of an inaccurate comparison here to ensure we can't miss this problem
    // noinspection EqualityComparisonWithCoercionJS
    if(exitCode == "3221225781"){
        // Missing DLL, most likely Visual C++ Redistributable
        appendSuggestion("3221225781 exit code can mean that you are missing \
a required DLL file. It may be Visual C++ Redistributable 2019, which you can download an \
installer for from: <a href='https://aka.ms/vs/17/release/vc_redist.x64.exe'>\
https://aka.ms/vs/17/release/vc_redist.x64.exe</a> in order to install / repair it. \
You can also try running Thrive.exe manually as that may show an error dialog with the \
missing DLL file name in it. \
If you are not running <strong>Windows 10</strong>, that may be the issue. The only \
supported version of Windows is Windows 10, the game may not work with older versions. \
If these don't help please contact us for additional assistance.", element);
    }
}

module.exports.unpackError = unpackError;
module.exports.startError = startError;
