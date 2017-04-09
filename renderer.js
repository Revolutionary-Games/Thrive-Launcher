// This file is required by the index.html file and will
// be executed in the renderer process for that window.
// All of the Node.js APIs are available in this process.
"use strict";

const fs = require('fs');


// document.addEventListener("DOMContentLoaded", function(event) {
// }

//document.createElement("div")
document.getElementById("text").textContent =
    "This would be result of some discourse API call.";

// Buttons
let playButton = document.getElementById("playButton");

let playButtonText = document.getElementById("playText");

playButtonText.textContent = "Play 0.3.3 (Current)";



