    // module export
    if (typeof define === "function" && define.amd) {
        // AMD
        define("tps", [], function () { return Transpose; });
    } else if (typeof module !== "undefined" && module.exports) {
        // Node
        module.exports = Transpose;
    }
