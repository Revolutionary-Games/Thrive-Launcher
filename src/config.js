// Renderer configuration options for easy changing when debugging
"use strict";

module.exports = {
    // Shows output from 7z. Not really useful as it shows no actual progress
    showUnpackMessages: false,

    // If true then the testing data (local, unsigned) file is loaded
    loadTestVersionData: false,

    // If true will only attempt reading the prepackaged version data
    // Can be changed by user if no internet / download fails
    loadPrePackagedVersionData: false,

    // When true checks if computer has intel graphics that likely cause problems
    checkGraphicsCard: false,

    // For local testing
    // devCenterURL: "http://localhost:5000",
    devCenterURL: "https://dev.revolutionarygamesstudio.com/",
};
