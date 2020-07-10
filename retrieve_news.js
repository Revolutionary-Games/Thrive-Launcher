//
// Module for retrieving Thrive news
//
"use strict";

const request = require("request");

const FeedParser = require("feedparser");

const moment = require("moment");

const truncate = require("./truncate");

// For user agent version
const pjson = require("./package.json");


// Feed URL configuration
const devForumFeedURL = "https://forum.revolutionarygamesstudio.com/posts.rss";
const mainSiteFeedURL = "https://revolutionarygamesstudio.com/feed/";

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

    return moment.parseZone(str, "en");
}

// Extra sanitization from here
// https://gist.github.com/ufologist/5a0da51b2b9ef1b861c30254172ac3c9
function trimAttributes(node){

    $.each(node.attributes, function(){
        const attrName = this.name;
        const attrValue = this.value;


        // Remove attribute name start with "on", possible unsafe,
        // for example: onload, onerror...
        //
        // remvoe attribute value start with "javascript:" pseudo protocol, possible unsafe,
        // for example href="javascript:alert(1)"
        if(attrName.indexOf("on") == 0 || attrValue.indexOf("javascript:") == 0){
            $(node).removeAttr(attrName);
        }
    });
}


function parseFeed(feed, resultObj){

    return new Promise((resolve) => {

        const errorCallback = (error) => {

            resultObj.htmlNodes = null;
            resultObj.error = "Error failed to get content: " + error;

            resolve();

        };

        // Define our streams
        const req = request(feed, {timeout: 10000, pool: false});
        req.setMaxListeners(50);

        // Some feeds do not respond without user-agent and accept headers.
        req.setHeader("user-agent", "Thrive-Launcher " + pjson.version);
        req.setHeader("accept", "text/html,application/xhtml+xml");

        const feedparser = new FeedParser();

        // Define our handlers
        req.on("error", errorCallback);
        req.on("response", function(res){

            if(res.statusCode != 200)
                return this.emit("error", new Error("Bad status code"));

            // There could be charset translation here, but iconv requires native extensions
            // var charset = getParams(res.headers['content-type'] || '').charset;
            // res = maybeTranslate(res, charset);

            // And boom goes the dynamite
            res.pipe(feedparser);
            return null;
        });

        feedparser.on("error", errorCallback);


        feedparser.on("end", function(){

            resolve();
        });

        feedparser.on("readable", function(){
            let post = this.read();

            while(post !== null){

                // Use this to find properties you want to output
                // console.log(JSON.stringify(post, ' ', 4));

                const span = document.createElement("span");
                span.classList.add("FeedPost");

                // Title
                {
                    const title = document.createElement("h3");

                    if(linkInTitle){

                        const linkA = document.createElement("a");

                        linkA.textContent = post.title;
                        linkA.href = post.link;

                        title.append(linkA);

                    } else {

                        title.textContent = post.title;
                    }

                    title.classList.add("FeedTitle");

                    span.append(title);
                }

                const infoBox = document.createElement("span");
                infoBox.classList.add("FeedInfoBox");


                // Link
                if(!linkInTitle){

                    const link = document.createElement("span");

                    link.classList.add("FeedLink");

                    const linkA = document.createElement("a");

                    linkA.textContent = post.link;
                    linkA.href = post.link;

                    link.append(linkA);

                    infoBox.append(link);
                }

                {

                    let dateStr = post.pubdate || post.date;
                    const date = parseFeedDate(dateStr);

                    if(date){

                        // Plain javascript method
                        // dateStr = date.toLocaleDateString(language) + " " +
                        //     date.toLocaleTimeString(language) + " (UTC" +
                        //     (date.getTimezoneOffset() / 60) + ")";


                        dateStr = date.fromNow() + ", " +

                        // Long date format
                        date.format("dddd Do MMM YYYY HH:mm:ss Z", language);

                        // Short format
                        // date.format('DD.MM.YYYY HH:mm:ss Z', language);

                    } else {

                        dateStr = "(Unknown format) " + dateStr;
                    }

                    const postedByAndDate = document.createElement("span");
                    postedByAndDate.classList.add("FeedAuthorAndDate");

                    const creatorName = document.createElement("span");

                    creatorName.classList.add("FeedAuthor");
                    creatorName.textContent = post.creator || post.author;


                    postedByAndDate.append(document.createTextNode("posted by "));

                    postedByAndDate.append(creatorName);

                    postedByAndDate.append(document.createTextNode(" " + dateStr));

                    infoBox.append(postedByAndDate);
                }

                // Console.log(JSON.stringify(post.categories, ' ', 4));

                if(post.categories && post.categories.length > 0){

                    const categories = document.createElement("span");

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


                const content = document.createElement("span");
                content.classList.add("FeedPreview");

                // This needs to be sanitized! //
                const remoteData = $.parseHTML(post.description,
                    null,
                    false);

                // Sanitize some attributes that might execute stuff //
                if(!allowXSSAttacks){
                    $(remoteData).find("*").each(function(){
                        trimAttributes(this);
                    });
                }

                if(!showImagesInFeed){

                    $(remoteData).find("img").remove();
                } else {
                    // Fix images with relative links
                    $(remoteData).find("img").each(function(){
                        this.src = new URL(this.src, feed);
                    });
                }

                if(rewriteYoutube){
                    $(remoteData).find("iframe").each(function(){
                        const matches = this.src.match(youtubeURLRegex);

                        if(matches){
                            const url = "https://www.youtube.com/watch?v=" + matches[1];
                            const anchor = document.createElement("a");
                            anchor.href = url;
                            anchor.append(document.createTextNode(url));
                            this.replaceWith(anchor);
                        }
                    });
                }

                if(!showIFramesInFeed){

                    $(remoteData).find("iframe").remove();
                }

                let truncated = false;

                if(truncateLongFeedItems){

                    truncated = truncate(remoteData, truncateLength);
                }

                $(content).append(remoteData);

                if(truncated){

                    const truncateMessage = document.createElement("span");
                    truncateMessage.classList.add("FeedTruncated");

                    truncateMessage.append(document.createTextNode("This post was "));

                    {
                        const span = document.createElement("span");
                        span.classList.add("RedWordFeedTruncated");

                        span.textContent = "truncated! ";
                        truncateMessage.append(span);
                    }

                    // Link to full post
                    {
                        const link = document.createElement("a");
                        link.classList.add("FeedLink");

                        link.textContent = "click here";
                        link.href = post.link;

                        truncateMessage.append(link);
                    }

                    truncateMessage.append(document.
                        createTextNode(" to read the full post. "));

                    $(content).append($(truncateMessage));
                }

                span.append(content);

                // Post.description should be truncated and sanitized to get a partial text


                // post["content:encoded"] might have more text
                resultObj.htmlNodes.append(span);

                post = this.read();
            }
        });
    });
}

function retrieveNews(callback){

    const news = {htmlNodes: document.createElement("div")};

    const devposts = {htmlNodes: document.createElement("div")};

    const newsPromise = parseFeed(mainSiteFeedURL, news);
    const devsPromise = parseFeed(devForumFeedURL, devposts);


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

