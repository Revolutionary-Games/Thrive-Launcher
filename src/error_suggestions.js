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
supported version of Windows for Thrive is Windows 10, older versions may have issues. \
If you get an error about \"api-ms-win-core-file-l2-1-2.dll\" \
missing, then you need to check that you have all Windows updates installed. Installing \
<a href='https://www.microsoft.com/en-us/download/details.aspx?id=48234'>\
Windows 10 Universal C Runtime</a> or <a \
href='https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/'>\
Windows 10 SDK</a> may allow the game to run on older Windows versions. \
It should also be available through \
<a href='https://docs.microsoft.com/en-us/cpp/windows/universal-crt-deployment?view=\
msvc-170#central-deployment'>Windows Update as a recommended update</a>. \
If these don't help please contact us for additional assistance.", element);
    }
}

module.exports.unpackError = unpackError;
module.exports.startError = startError;
