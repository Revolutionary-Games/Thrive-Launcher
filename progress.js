"use strict";

/**
 * Helper class for dealing with HTML progress bars.
 */
class Progress {
    /**
     * `Progress :: String -> Progress`
     *
     * Get a reference to a progress bar by name, creating it if it does not
     * already exist.
     *
     * @param {string} name Name of the progress bar.
     */
    constructor(name) {
        if (Progress.progressBars[name]) {
            return Progress.progressBars[name];
        }

        this.attached = false;
        this.formatter = (value) => value.toString();
        this.name = name;

        Progress.progressBars[name] = this;
    }

    _updateLabel() {
        // Cast to string before setting.
        this._progress.setAttribute("aria-valuenow", String(this.value));
        this._progress.setAttribute("aria-valuemax", String(this.max));

        this._progressLabel.textContent =
            `${this.formattedValue()} of ${this.formattedMax()}`;
    }

    _updateView() {
        this._progressBar.style.width = `${(this.position * 100).toFixed(2)}%`;
    }

    /**
     * Only construct the DOM tree when it is accessed.
     */
    get domTree() {
        if (!this._progress) {
            // Only the container should be visible to ARIA. The contents should
            // be hidden from assistive technologies.
            const container = document.createElement("div");
            container.setAttribute("id", `progress-${this.name}`);
            container.setAttribute("role", "progressbar");
            container.setAttribute("aria-valuemin", "0");
            container.setAttribute("aria-valuetext", "Downloading...");
            container.classList.add("progress-bar");

            const bar = document.createElement("div");
            bar.setAttribute("id", `progress-${this.name}-bar`);
            bar.setAttribute("aria-hidden", "true");
            bar.classList.add("progress-inner");
            container.append(bar);

            const label = document.createElement("span");
            label.setAttribute("id", `progress-${this.name}-label`);
            label.setAttribute("aria-hidden", "true");
            label.classList.add("progress-label");
            container.append(label);

            this._progress = container;
            this._progressBar = bar;
            this._progressLabel = label;
        }

        return this._progress;
    }

    /** @type {number} */
    get max() {
        if (!("_max" in this)) {
            return 0;
        }

        return this._max;
    }

    set max(number) {
        this._max = number;
        this._updateLabel();
    }

    get position() {
        return this.value / this.max;
    }

    /** @type {number} */
    get value() {
        return this._value;
    }

    set value(number) {
        // Clamp the value to prevent overflow.
        this._value = Math.min(number, this.max);
        this._updateView();
        this._updateLabel();
    }

    /**
     * `formattedMax :: () -> Any`
     *
     * Transform the maximum progress using a custom format function, and return
     * the result.
     *
     * @returns {any} The result of executing the formatter against `this.value`.
     */
    formattedMax() {
        return this.formatter(this.max);
    }

    /**
     * `formattedValue :: () -> Any`
     *
     * Transform the current progress using a custom format function, and return
     * the result.
     *
     * @returns {any} The result of executing the formatter against `this.value`.
     */
    formattedValue() {
        return this.formatter(this.value);
    }

    /**
     * `render :: HTMLElement -> ()`
     *
     * Render the progress bar in an element. Triggers the DOM tree build, if not
     * already performed.
     *
     * @param {HTMLElement} base Where to render this progress bar.
     */
    render(base) {
        base.append(this.domTree);
    }
}

// We don't have to keep a permanent reference to the progress bar that we want;
// we can just run the constructor with the same name to get the same object
// out.
Progress.progressBars = {};

exports.Progress = function(name) {
    return new Progress(name);
};
