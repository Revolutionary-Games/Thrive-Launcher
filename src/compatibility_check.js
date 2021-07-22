// Code for checking if the user's computer can probably run thrive or not
"use strict";

const si = require("@electron/remote").require("systeminformation");

const {checkGraphicsCard} = require("./config");
const {Modal, showGenericError} = require("./modal");
const {setPlayButtonText} = require("./version_select_button");

let showIncompatiblePopup = false;

let showHelpText = false;

const cardsModel = [];

const incompatibleModal = new Modal("incompatibleModal", "incompatibleModalDialog", {
    autoClose: true,
    closeButton: "incompatibleModalClose",
    onClose: function(){
        showIncompatiblePopup = false;
    },
});

function showCompatibilityProblems(onFinish){
    incompatibleModal.show();

    const incompatibleBox = document.getElementById("incompatibleModalContent");
    incompatibleBox.innerHTML = "<p id='text'></p>";

    const box = document.getElementById("text");

    box.innerHTML = "WARNING: Intel Integrated Graphics card may cause Thrive to " +
        "have low performance or even crash.</a>";

    box.append(document.createElement("br"));
    box.append(document.createElement("br"));

    box.append(document.createTextNode("Detected graphics card(s):" + cardsModel));

    // Shows the help text if a non-Intel card is detected
    if(showHelpText){
        box.append(document.createElement("br"));
        box.append(document.createElement("br"));
        box.append(document.createTextNode("Another graphics card detected, " +
            "you should configure " +
            "Thrive to run with that instead!"));
    }

    box.append(document.createElement("br"));

    const closeContainer = document.createElement("div");
    closeContainer.style.marginTop = "20px";
    closeContainer.style.textAlign = "center";
    const close = document.createElement("div");
    close.textContent = "Continue";
    close.className = "CloseButton";

    close.addEventListener("click", () => {
        incompatibleModal.hide();
        showIncompatiblePopup = false;
        onFinish();
    });

    closeContainer.append(close);
    box.append(closeContainer);
    showIncompatiblePopup = false;
}

// Checks the graphics card
async function checkIfCompatible(){
    if(!checkGraphicsCard)
        return;

    try{
        setPlayButtonText("Checking graphics hardware...");

        const data = await si.graphics();
        const identifier = ["nvidia", "advanced micro devices", "amd"]; // And so on...

        for(let i = 0; i < data.controllers.length; i++){

            const vendor = data.controllers[i].vendor.toLowerCase();

            cardsModel.push(" " + data.controllers[i].model);

            // Is incompatible if Intel is found in a substring
            if(vendor.includes("intel")){

                console.log("hardware is not compatible");
                showIncompatiblePopup = true;
            }

            for(let n = 0; n < identifier.length; n++){
                if(vendor.includes(identifier[n])){
                    showHelpText = true;
                }
            }
        }

        console.log("finished checking the graphics hardware");

    } catch(err){
        console.log(err);
        showGenericError("Failed to check the graphics hardware: " + err);
    }
}

module.exports.checkIfCompatible = checkIfCompatible;
module.exports.performCompatibilityCheck = function(onFinish){
    if(showIncompatiblePopup){
        showCompatibilityProblems();
    } else {
        onFinish();
    }
};
