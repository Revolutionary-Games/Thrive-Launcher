// Modal dialog class for use with the play and links buttons
"use strict";

const assert = require('assert');
const $ = require('jquery');

class Modal {

    //! backdropID is the parent dark span on top of which the dialogID is shown
    //!
    //! Valid properties:
    //! autoClose: if true will close when clicked outside the dialog
    //! closeButton: id of element that when clicked closes this modal dialog
    constructor(backdropID, dialogID, properties = {}) {

        console.log("stuff");
        
        this.backdrop = document.getElementById(backdropID);
        this.dialog = document.getElementById(dialogID);

        // Default properties //
        this.autoClose = true;

        // Override properties
        if(properties != undefined && properties != null){

            if(properties.autoClose != undefined){
                
                this.autoClose = properties.autoClose;
                
            } 

            if(properties.closeButton != undefined){

                this.closeButton = document.getElementById(properties.closeButton);
            }
        }
        
        this.invariant();

        // Register click handlers

        // Click outside the dialog and on the background, close if autoClose
        if(this.autoClose){
            
            this.backdrop.onclick = (event) => {
                
                if(event.target == this.backdrop){

                    this.hide();
                }
            }
        }

        if(this.closeButton){

            this.closeButton.addEventListener("click", (event) => {

                this.hide();
            });
        }
    }

    invariant(){

        assert(this.backdrop);
        assert(this.dialog);
    }

    //! Shows this dialog
    show(){

        this.backdrop.style.display = "block";
        
        $( this.dialog ).slideDown( 400, function() {
            // Animation complete.
        });   
    }

    //! Hides this dialog
    hide(){

        $( this.dialog ).slideUp( 400, () => {
            this.backdrop.style.display = "none";
        });        
    }
    
}

module.exports.Modal = Modal;
