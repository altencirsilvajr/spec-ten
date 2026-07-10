window.specpenShare = {
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

window.specpenCompareSelection = {
    load() {
        try {
            return window.localStorage.getItem("specpen.compareSelection")
                ?? window.localStorage.getItem("phoneCatalog.compareSelection")
                ?? "";
        } catch {
            return "";
        }
    },
    save(serialized) {
        try {
            if (!serialized) {
                window.localStorage.removeItem("specpen.compareSelection");
                window.localStorage.removeItem("phoneCatalog.compareSelection");
                return;
            }

            window.localStorage.setItem("specpen.compareSelection", serialized);
        } catch {
        }
    }
};

window.specpenRecentlyViewed = {
    load() {
        try {
            return window.localStorage.getItem("specpen.recentlyViewed")
                ?? window.localStorage.getItem("phoneCatalog.recentlyViewed")
                ?? "";
        } catch {
            return "";
        }
    },
    save(serialized) {
        try {
            if (!serialized) {
                window.localStorage.removeItem("specpen.recentlyViewed");
                window.localStorage.removeItem("phoneCatalog.recentlyViewed");
                return;
            }

            window.localStorage.setItem("specpen.recentlyViewed", serialized);
        } catch {
        }
    }
};
