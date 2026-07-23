window.blocklyInterop = {
    workspace: null,

    init: function (elementId) {
        if (typeof Blockly === "undefined") {
            console.error("Blockly ist nicht geladen.");
            return;
        }
        const container = document.getElementById(elementId);
        if (!container) {
            console.error("Element #" + elementId + " nicht gefunden.");
            return;
        }
        this.workspace = Blockly.inject(container, {
            toolbox: {
                kind: "flyoutToolbox",
                contents: [
                    { kind: "block", type: "controls_if" },
                    { kind: "block", type: "controls_repeat_ext" },
                    { kind: "block", type: "logic_compare" },
                    { kind: "block", type: "math_number" },
                    { kind: "block", type: "math_arithmetic" },
                    { kind: "block", type: "text" },
                    { kind: "block", type: "text_print" }
                ]
            },
            scrollbars: true,
            trashcan: true
        });
    },

    getXml: function () {
        if (!this.workspace) return "";
        const xml = Blockly.Xml.workspaceToDom(this.workspace);
        return Blockly.Xml.domToText(xml);
    },

    dispose: function () {
        if (this.workspace) {
            this.workspace.dispose();
            this.workspace = null;
        }
    }
};
