window.blocklyInterop = {
    workspace: null,
    blocksRegistered: false,
    resizeHandler: null,
    changeListener: null,
    suppressChangeEvent: false,

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

        if (this.workspace) {
            this.workspace.dispose();
            this.workspace = null;
        }

        if (this.resizeHandler) {
            window.removeEventListener("resize", this.resizeHandler);
            this.resizeHandler = null;
        }

        this.registerCustomBlocks();

        this.workspace = Blockly.inject(container, {
            toolbox: this.createToolboxXml(),
            toolboxPosition: "start",
            horizontalLayout: false,
            rtl: false,
            grid: {
                spacing: 24,
                length: 3,
                colour: "#dfe4ee",
                snap: false
            },
            move: {
                drag: true,
                wheel: true,
                scrollbars: true
            },
            zoom: {
                controls: true,
                wheel: true,
                startScale: 0.9,
                maxScale: 1.6,
                minScale: 0.45,
                scaleSpeed: 1.08,
                pinch: true
            },
            trashcan: true,
            renderer: "geras"
        });

        const toolboxDiv = container.querySelector(".blocklyToolboxDiv");
        if (toolboxDiv) {
            toolboxDiv.style.display = "block";
            toolboxDiv.style.visibility = "visible";
        }

        this.resizeHandler = () => Blockly.svgResize(this.workspace);
        window.addEventListener("resize", this.resizeHandler);
        Blockly.svgResize(this.workspace);
    },

    setXml: function (xmlText) {
        if (!this.workspace) {
            return;
        }

        this.suppressChangeEvent = true;
        try {
            this.workspace.clear();

            if (xmlText && xmlText.trim().length > 0) {
                const xml = Blockly.utils.xml.textToDom(xmlText);
                Blockly.Xml.domToWorkspace(xml, this.workspace);
            }
        } finally {
            this.suppressChangeEvent = false;
        }
    },

    subscribeWorkspaceChanges: function (dotNetRef) {
        if (!this.workspace || !dotNetRef) {
            return;
        }

        if (this.changeListener) {
            this.workspace.removeChangeListener(this.changeListener);
            this.changeListener = null;
        }

        this.changeListener = () => {
            if (this.suppressChangeEvent) {
                return;
            }

            const xml = this.getXml();
            dotNetRef.invokeMethodAsync("OnBlocklyXmlChanged", xml);
        };

        this.workspace.addChangeListener(this.changeListener);
    },

    exportXmlFile: function (fileName) {
        const xml = this.getXml();
        this.exportXmlContent(xml, fileName);
    },

    exportXmlContent: function (xmlText, fileName) {
        const blob = new Blob([xmlText || ""], { type: "application/xml;charset=utf-8" });
        const url = URL.createObjectURL(blob);

        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName || "blockly-workspace.xml";
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);

        URL.revokeObjectURL(url);
    },

    importXmlFile: function () {
        return new Promise((resolve) => {
            const input = document.createElement("input");
            input.type = "file";
            input.accept = ".xml,text/xml,application/xml";
            input.style.display = "none";

            input.addEventListener("change", async () => {
                if (!input.files || input.files.length === 0) {
                    resolve("");
                    return;
                }

                const file = input.files[0];
                const xmlText = await file.text();
                this.setXml(xmlText);
                resolve(xmlText);
            });

            document.body.appendChild(input);
            input.click();
            document.body.removeChild(input);
        });
    },

    createToolboxXml: function () {
        const xmlText = `<xml>
            <category name="Beckhoff Module" colour="#9f1d35">
                <block type="ethercat_master"></block>
                <block type="beckhoff_epp1322_0001"></block>
                <block type="beckhoff_epp1809_0021"></block>
                <block type="beckhoff_epp2316_0008"></block>
            </category>
            <category name="Logik" colour="#4064d7">
                <block type="controls_if"></block>
                <block type="controls_repeat_ext"></block>
                <block type="logic_compare"></block>
            </category>
            <category name="Mathematik" colour="#2e7d5a">
                <block type="math_number"></block>
                <block type="math_arithmetic"></block>
            </category>
            <category name="Text" colour="#7b5ea7">
                <block type="text"></block>
                <block type="text_print"></block>
            </category>
        </xml>`;

        return Blockly.utils.xml.textToDom(xmlText);
    },

    registerCustomBlocks: function () {
        if (this.blocksRegistered) {
            return;
        }

        const epp1322ConnectionType = "Epp1322Child";
        const moduleConnectionType = "BeckhoffModule";

        const createModuleBlock = (typeName, config) => {
            Blockly.Blocks[typeName] = {
                init: function () {
                    this.appendDummyInput()
                        .appendField(new Blockly.FieldImage(config.image, 72, 28, config.altText))
                        .appendField(config.moduleId);

                    this.appendDummyInput()
                        .appendField(config.shortLabel);

                    this.appendDummyInput()
                        .appendField("BMK")
                        .appendField(new Blockly.FieldTextInput(""), "BMK");

                    this.appendDummyInput()
                        .appendField("Wert")
                        .appendField(new Blockly.FieldNumber(0, 0, 32768, 0.01), "VALUE");

                    this.setColour(config.colour);
                    this.setPreviousStatement(true, moduleConnectionType);
                    this.setNextStatement(true, moduleConnectionType);
                    this.setInputsInline(true);
                    this.setTooltip(config.tooltip);
                    this.setHelpUrl(config.helpUrl);
                }
            };
        };

        Blockly.Blocks["ethercat_master"] = {
            init: function () {
                this.appendDummyInput()
                    .appendField("EtherCAT Master");

                this.appendDummyInput()
                    .appendField("AMS Net ID")
                    .appendField(new Blockly.FieldTextInput("5.1.204.160.1.1"), "AMS_NET_ID");

                this.appendDummyInput()
                    .appendField("Adapter")
                    .appendField(new Blockly.FieldTextInput("eth0"), "ADAPTER");

                this.appendDummyInput()
                    .appendField("BMK")
                    .appendField(new Blockly.FieldTextInput(""), "BMK");

                this.appendStatementInput("CHILDREN")
                    .setCheck(epp1322ConnectionType)
                    .appendField("Module");

                this.setColour("#1f2240");
                this.setTooltip("EtherCAT Master: akzeptiert nur EPP1322-0001 als Kindblöcke.");
                this.setHelpUrl("https://www.beckhoff.com");
            }
        };

        Blockly.Blocks["beckhoff_epp1322_0001"] = {
            init: function () {
                this.appendDummyInput()
                    .appendField(new Blockly.FieldImage("/images/blockly/epp1322-0001.svg", 88, 34, "EPP1322-0001"))
                    .appendField("EPP1322-0001")
                    .appendField("2-Kanal");

                this.appendDummyInput()
                    .appendField("BMK")
                    .appendField(new Blockly.FieldTextInput(""), "BMK");

                this.appendStatementInput("CHANNEL_1")
                    .setCheck(moduleConnectionType)
                    .appendField("CH1");

                this.appendStatementInput("CHANNEL_2")
                    .setCheck(moduleConnectionType)
                    .appendField("CH2");

                this.setColour("#9f1d35");
                this.setPreviousStatement(true, epp1322ConnectionType);
                this.setNextStatement(true, epp1322ConnectionType);
                this.setTooltip("Beckhoff EPP1322-0001 als kompakter Container-Block mit zwei Kanälen für EPP1809-0021 und EPP2316-0008.");
                this.setHelpUrl("https://www.beckhoff.com/de-de/produkte/i-o/ethercat-box/eppxxxx-industriegehaeuse/eppxxxx-system/epp1322-0001.html");

                this.setOnChange(function () {
                    const parent = this.getSurroundParent();
                    const isNestedInSameBlock = parent && parent.type === "beckhoff_epp1322_0001";

                    if (isNestedInSameBlock) {
                        this.unplug(true);
                        this.setWarningText("EPP1322-0001 darf nicht in einem EPP1322-0001 eingehängt werden.");
                    } else {
                        this.setWarningText(null);
                    }
                });
            }
        };

        createModuleBlock("beckhoff_epp1809_0021", {
            moduleId: "EPP1809-0021",
            shortLabel: "16x DI",
            image: "/images/blockly/epp1809-0021.svg",
            altText: "EPP1809-0021",
            colour: "#d58d00",
            tooltip: "Beckhoff EPP1809-0021 als kompakter Statement-Block für die Kanäle des EPP1322-0001.",
            helpUrl: "https://www.beckhoff.com/de-de/produkte/i-o/ethercat-box/eppxxxx-industriegehaeuse/epp1xxx-digital-eingang/epp1809-0021.html"
        });

        createModuleBlock("beckhoff_epp2316_0008", {
            moduleId: "EPP2316-0008",
            shortLabel: "16x DO",
            image: "/images/blockly/epp2316-0008.svg",
            altText: "EPP2316-0008",
            colour: "#b24b00",
            tooltip: "Beckhoff EPP2316-0008 als kompakter Statement-Block für die Kanäle des EPP1322-0001.",
            helpUrl: "https://www.beckhoff.com/de-de/produkte/i-o/ethercat-box/eppxxxx-industriegehaeuse/epp2xxx-digital-ausgang/epp2316-0008.html"
        });

        this.blocksRegistered = true;
    },

    getXml: function () {
        if (!this.workspace) return "";
        const xml = Blockly.Xml.workspaceToDom(this.workspace);
        return Blockly.Xml.domToText(xml);
    },

    dispose: function () {
        if (this.resizeHandler) {
            window.removeEventListener("resize", this.resizeHandler);
            this.resizeHandler = null;
        }

        if (this.workspace && this.changeListener) {
            this.workspace.removeChangeListener(this.changeListener);
            this.changeListener = null;
        }

        if (this.workspace) {
            this.workspace.dispose();
            this.workspace = null;
        }
    }
};
