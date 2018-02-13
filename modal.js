// Modal dialog class for use with the play and links buttons
"use strict";

const assert = require('assert');
const $ = require('jquery');

function documentHeight(){

    let body = document.body,
        html = document.documentElement;
    
    return Math.max(body.scrollHeight, body.offsetHeight, 
                    html.clientHeight, html.scrollHeight, html.offsetHeight);
}

class Modal {

    //! backdropID is the parent dark span on top of which the dialogID is shown
    //!
    //! Valid properties:
    //! autoClose: if true will close when clicked outside the dialog
    //! closeButton: id of element that when clicked closes this modal dialog
    //! onClose: close callback
    constructor(backdropID, dialogID, properties = {}) {

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

            if(properties.onClose != undefined){

                this.onClose = properties.onClose;
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

    visible(){
        return this.backdrop.style.display == "block";
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

        if(this.onClose){
            
            let prevent = this.onClose();

            if(prevent)
                return;
        }

        $( this.dialog ).slideUp( 400, () => {
            this.backdrop.style.display = "none";
        }); 
    }
    
}

//! \note Unlike Modal this uses element objects instead of strings
class ComboBox{

    //! backdropID is the parent dark span on top of which the dialogID is shown
    //!
    //! Valid properties:
    //! closeButton: element that is clicked to toggle this
    //! onClose: close callback
    //! onOpen: open callback
    constructor(backdropElement, popupElement, properties = {}) {

        this.backdrop = backdropElement;
        this.dialog = popupElement;

        // Default properties //

        // Override properties
        if(properties != undefined && properties != null){

            if(properties.closeButton != undefined){

                this.closeButton = properties.closeButton;
            }

            if(properties.onClose != undefined){

                this.onClose = properties.onClose;
            }

            if(properties.onOpen != undefined){

                this.onOpen = properties.onOpen;
            }
        }
        
        this.invariant();

        // Register click handlers

        // Click outside the dialog and on the background, close if autoClose
        this.backdrop.onclick = (event) => {
            
            if(event.target == this.backdrop){

                this.hide();
            }
        }


        if(this.closeButton){

            this.closeButton.addEventListener("click", (event) => {

                if(this.isShown()){
                    
                    this.hide();
                } else {
                    
                    this.show();
                }
            });
        }
    }

    invariant(){

        assert(this.backdrop);
        assert(this.dialog);
    }

    //! Returns true if this is currently shown
    isShown(){

        return $( this.backdrop ).is(":visible"); 
    }

    //! Shows this dialog
    show(){

        this.backdrop.style.display = "block";
        
        $( this.dialog ).show();

        if(this.onOpen){
            
            let prevent = this.onOpen();

            if(prevent){

                // Undo the open //
                this.backdrop.style.display = "none";
                $( this.dialog ).hide();
            }
        }
    }

    //! Hides this dialog
    hide(){

        if(this.onClose){
            
            let prevent = this.onClose();

            if(prevent)
                return;
        }

        $( this.dialog ).slideUp( 50, () => {
            this.backdrop.style.display = "none";
        }); 
    }

    //! Positions and sizes this dialog above an element
    position(element){

        console.log("positioning thing");
        
        assert(element);

        $(this.dialog).width($(element).width());

        const offset = $(element).offset();

        this.dialog.style.left = offset.left + "px";
        this.dialog.style.bottom = (documentHeight() - offset.top - 1) + "px";
    }
    
}

module.exports.Modal = Modal;
module.exports.ComboBox = ComboBox;
