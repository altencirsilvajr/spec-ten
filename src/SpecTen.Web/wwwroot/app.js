window.spectenShare = {
    async shareOrCopy(url, title, text) {
        const absoluteUrl = new URL(url, window.location.origin).toString();
        const payload = {
            title: title ?? document.title,
            text: text ?? "",
            url: absoluteUrl
        };

        if (navigator.share) {
            try {
                await navigator.share(payload);
                return "shared";
            } catch (error) {
                if (error && error.name === "AbortError") {
                    return "cancelled";
                }
            }
        }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(absoluteUrl);
            return "copied";
        }

        const input = document.createElement("input");
        input.value = absoluteUrl;
        input.setAttribute("readonly", "");
        input.style.position = "absolute";
        input.style.left = "-9999px";
        document.body.appendChild(input);
        input.select();
        document.execCommand("copy");
        document.body.removeChild(input);
        return "copied";
    }
};

window.spectenCompareSelection = {
    load() {
        try {
            return window.localStorage.getItem("specten.compareSelection") ?? "";
        } catch {
            return "";
        }
    },
    save(serialized) {
        try {
            if (!serialized) {
                window.localStorage.removeItem("specten.compareSelection");
                return;
            }

            window.localStorage.setItem("specten.compareSelection", serialized);
        } catch {
        }
    }
};

window.spectenRecentlyViewed = {
    load() {
        try {
            return window.localStorage.getItem("specten.recentlyViewed") ?? "";
        } catch {
            return "";
        }
    },
    save(serialized) {
        try {
            if (!serialized) {
                window.localStorage.removeItem("specten.recentlyViewed");
                return;
            }

            window.localStorage.setItem("specten.recentlyViewed", serialized);
        } catch {
        }
    }
};

window.spectenViewport = {
    focusAndScrollTo(elementId) {
        const element = document.getElementById(elementId);
        if (!element) {
            return;
        }

        element.focus({ preventScroll: true });
        element.scrollIntoView({ behavior: "smooth", block: "start" });
    }
};

window.spectenSearchInput = {
    connect(input, dotNetReference, delayMilliseconds) {
        if (!input || input.dataset.spectenSearchConnected === "true") {
            return;
        }

        let debounceHandle;
        const notify = () => {
            window.clearTimeout(debounceHandle);
            debounceHandle = undefined;
            dotNetReference.invokeMethodAsync("OnSearchInput", input.value);
        };
        const onInput = () => {
            window.clearTimeout(debounceHandle);
            debounceHandle = window.setTimeout(notify, delayMilliseconds);
        };
        const onBlur = () => {
            window.clearTimeout(debounceHandle);
            debounceHandle = undefined;
            dotNetReference.invokeMethodAsync("OnSearchBlur", input.value);
        };
        const onKeyDown = (event) => {
            if (![
                "ArrowDown",
                "ArrowUp",
                "Escape",
                "Enter"
            ].includes(event.key)) {
                return;
            }

            event.preventDefault();
            window.clearTimeout(debounceHandle);
            debounceHandle = undefined;
            dotNetReference.invokeMethodAsync("OnSearchKey", event.key, input.value);
        };

        input.addEventListener("input", onInput);
        input.addEventListener("blur", onBlur);
        input.addEventListener("keydown", onKeyDown);
        input.dataset.spectenSearchConnected = "true";
    }
};
