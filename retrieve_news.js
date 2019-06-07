//
// Module for retrieving Thrive news
//
"use strict";

const request = require('request');
const assert = require('assert');

const $ = require('jquery');

const FeedParser = require('feedparser');

const moment = require('moment');

const truncate = require('./truncate');

// For user agent version
var pjson = require('./package.json');


//
// These should be configuration options shown to users
//

// If true all feed items appear at once
const feedWaitForAll = true;

// Most images are disabled if this is false
const showImagesInFeed = true;

// Rewrites youtube embeds as links
const rewriteYoutube = true;

// If false youtube videos won't play
// Content Security Policy also blocks them anyway. And for it to work all Google's ad
// scripts etc. need to be loaded...
const showIFramesInFeed = false;

// If true previews of feed items will be truncated
const truncateLongFeedItems = true;

// This is the truncate length. Should be long enough to have some
// text and a message about truncation
const truncateLength = 450;

//
// End configuration variables
//

const youtubeURLRegex = /^http.*youtube.com\/.*embed\/(\w+)\?.*/;


// If true the link to the post is in the title and not separately after it
const linkInTitle = true;

const language = window.navigator.userLanguage || window.navigator.language;
moment.locale(language);


// Never set to true
const allowXSSAttacks = false;


//! Does anything it takes to parse a date string (I gave up and used
//! the default format which resorts to Date.parse if it can't figure it out)
function parseFeedDate(str){

    return moment.parseZone(str, 'en');
}

// Extra sanitization from here
// https://gist.github.com/ufologist/5a0da51b2b9ef1b861c30254172ac3c9
function trimAttributes(node) {
    
    $.each(node.attributes, function() {
        var attrName = this.name;
        var attrValue = this.value;
        // remove attribute name start with "on", possible unsafe,
        // for example: onload, onerror...
        //
        // remvoe attribute value start with "javascript:" pseudo protocol, possible unsafe,
        // for example href="javascript:alert(1)"
        if (attrName.indexOf('on') == 0 || attrValue.indexOf('javascript:') == 0) {
            $(node).removeAttr(attrName);
        }
    });
}


function parseFeed(feed, resultObj){

    return new Promise((resolve, reject) => {

        let errorCallback = (error) => {
            
            resultObj.htmlNodes = null;
            resultObj.error = "Error failed to get content: " + error;

            resolve();
            return;
        };
        
        // Define our streams
        var req = request(feed, {timeout: 10000, pool: false});
        req.setMaxListeners(50);
        // Some feeds do not respond without user-agent and accept headers.
        req.setHeader('user-agent', "Thrive-Launcher " + pjson.version);
        req.setHeader('accept', 'text/html,application/xhtml+xml');

        let feedparser = new FeedParser();

        // Define our handlers
        req.on('error', errorCallback);
        req.on('response', function(res) {
            
            if(res.statusCode != 200)
                return this.emit('error', new Error('Bad status code'));

            // There could be charset translation here, but iconv requires native extensions
            //var charset = getParams(res.headers['content-type'] || '').charset;
            //res = maybeTranslate(res, charset);
            
            // And boom goes the dynamite
            res.pipe(feedparser);
            return null;
        });

        feedparser.on('error', errorCallback);

        
        feedparser.on('end', function(){

            resolve();
        });
        
        feedparser.on('readable', function() {
            var post;
            while ((post = this.read())) {

                // Use this to find properties you want to output
                //console.log(JSON.stringify(post, ' ', 4));

                let span = document.createElement("span");
                span.classList.add("FeedPost");

                // Title
                {
                    let title = document.createElement("h3");

                    if(linkInTitle){

                        let linkA = document.createElement("a");

                        linkA.textContent = post.title;
                        linkA.href = post.link;

                        title.append(linkA);
                        
                    } else {
                    
                        title.textContent = post.title;
                    }
                    
                    title.classList.add("FeedTitle");

                    span.append(title);
                }

                let infoBox = document.createElement("span");
                infoBox.classList.add("FeedInfoBox");
                

                // Link
                if(!linkInTitle){
                    
                    let link = document.createElement("span");

                    link.classList.add("FeedLink");
                    
                    let linkA = document.createElement("a");

                    linkA.textContent = post.link;
                    linkA.href = post.link;

                    link.append(linkA);

                    infoBox.append(link);
                }

                {

                    let dateStr = post.pubdate || post.date;
                    let date = parseFeedDate(dateStr);

                    if(date){
                        
                        // Plain javascript method
                        // dateStr = date.toLocaleDateString(language) + " " +
                        //     date.toLocaleTimeString(language) + " (UTC" +
                        //     (date.getTimezoneOffset() / 60) + ")";


                        dateStr = date.fromNow() + ", " +
                            // Long date format
                            date.format('dddd Do MMM YYYY HH:mm:ss Z', language);
                            // Short format
                            //date.format('DD.MM.YYYY HH:mm:ss Z', language);
                        
                    } else {

                        dateStr = "(Unknown format) " + dateStr;
                    }

                    let postedByAndDate = document.createElement("span");
                    postedByAndDate.classList.add("FeedAuthorAndDate");

                    let creatorName = document.createElement("span");

                    creatorName.classList.add("FeedAuthor");
                    creatorName.textContent = (post.creator || post.author );


                    postedByAndDate.append(document.createTextNode("posted by "));

                    postedByAndDate.append(creatorName);

                    postedByAndDate.append(document.createTextNode(" " + dateStr));
                    
                    infoBox.append(postedByAndDate);
                }

                //console.log(JSON.stringify(post.categories, ' ', 4));

                if(post.categories && post.categories.length > 0){

                    let categories = document.createElement("span");

                    categories.textContent = "Categories: " + post.categories.join(", ");
                    categories.classList.add("FeedCategories");

                    infoBox.append(categories);
                }

                span.append(infoBox);

                // Looks like the urls are now fixed so this would actually break them
                // // Force HTTP protocol to avoid file protocol
                // // Dirty hack to make forum images valid
                // if(post.description){
                //     // If uncommented this should be moved to the top of this file
                //     const forumImageFixRegex =
                //         /\/\/(forum\.revolutionarygamesstudio.com)\//igm;
                //     post.description = post.description.replace(forumImageFixRegex,
                //                                                 "http://$1/");
                // }


                let content = document.createElement("span");
                content.classList.add("FeedPreview");

                // This needs to be sanitized! //
                let remoteData = $.parseHTML(post.description,
                                             null,
                                             false);

                // Sanitize some attributes that might execute stuff //
                if(!allowXSSAttacks){
                    $( remoteData ).find('*').each(function() {
                        trimAttributes(this);
                    });
                }

                if(!showImagesInFeed){

                    $( remoteData ).find("img").remove();
                }

                if(rewriteYoutube){
                    $( remoteData ).find("iframe").each( function(){
                        let matches = this.src.match(youtubeURLRegex);
                        if(matches){
                            let url = "https://www.youtube.com/watch?v=" + matches[1];
                            let anchor = document.createElement("a");
                            anchor.href = url;
                            anchor.append(document.createTextNode(url));
                            this.replaceWith(anchor);
                        }
                    });
                }

                if(!showIFramesInFeed){
                    
                    $( remoteData ).find("iframe").remove();
                }

                let truncated = false;

                if(truncateLongFeedItems){

                    truncated = truncate(remoteData, truncateLength);
                }
                
                $( content ).append(remoteData);

                if(truncated){

                    let truncateMessage = document.createElement("span");
                    truncateMessage.classList.add("FeedTruncated");

                    truncateMessage.append(document.createTextNode(
                        "This post was "));

                    {
                        let span = document.createElement("span");
                        span.classList.add("RedWordFeedTruncated");

                        span.textContent = "truncated! ";
                        truncateMessage.append(span);
                    }

                    // Link to full post
                    {
                        let link = document.createElement("a");
                        link.classList.add("FeedLink");
                        
                        link.textContent = "click here";
                        link.href = post.link;

                        truncateMessage.append(link);
                    }

                    truncateMessage.append(document.createTextNode(
                        " to read the full post. "));

                    $( content ).append($( truncateMessage ));   
                }
                
                span.append(content);

                // post.description should be truncated and sanitized to get a partial text


                // post["content:encoded"] might have more text
                resultObj.htmlNodes.append(span);
            }
        });
    });
}

function retrieveNews(callback, url = "http://revolutionarygamesstudio.com/"){

    let news = {
        htmlNodes: document.createElement("div")
    };
    
    let devposts = {
        htmlNodes: document.createElement("div")
    };

    let newsPromise = parseFeed("http://revolutionarygamesstudio.com/feed/", news);
    let devsPromise = parseFeed("https://forum.revolutionarygamesstudio.com/posts.rss",
                                devposts);


    if(feedWaitForAll){

        newsPromise.then(() => {
            devsPromise.then(() => {

                callback(news, devposts);
            });
        });
        
    } else {

        callback(news, devposts);
    }
}


module.exports.retrieveNews = retrieveNews;

