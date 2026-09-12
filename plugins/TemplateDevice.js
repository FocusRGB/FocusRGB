// SignalRGB device plugin template.
// Copy, rename, fill in VID/PID/endpoint/packet format. Loaded from Documents\WhirlwindFX\Plugins\.
// Reference: https://docs.signalrgb.com/developer/plugins/plugin-exports/
// Tutorial (capture USB traffic with Wireshark, then map it): https://docs.signalrgb.com/developer/plugins/tutorial/

export function Name()            { return "Template Device"; }
export function Publisher()       { return "you"; }
export function VendorId()        { return 0x0000; }   // USB VID, e.g. 0x1532 for Razer
export function ProductId()       { return 0x0000; }   // USB PID
export function Documentation()   { return "troubleshooting/brand"; }
export function Type()            { return "Hid"; }    // "Hid" (default) or "RawUsb"
export function Size()            { return [8, 2]; }   // canvas cells [width, height] the device occupies
export function DefaultPosition() { return [120, 80]; }
export function DefaultScale()    { return 8.0; }
export function ImageUrl()        { return ""; }       // 1024x1024 png URL, optional

// Executables that must not be running for this plugin to initialize.
export function ConflictingProcesses() { return []; }

/* global
shutdownColor:readonly
LightingMode:readonly
forcedColor:readonly
*/
export function ControllableParameters() {
  return [
    { "property": "shutdownColor", "group": "lighting", "label": "Shutdown Color", "min": "0", "max": "360", "type": "color",    "default": "#009bde" },
    { "property": "LightingMode",  "group": "lighting", "label": "Lighting Mode",                            "type": "combobox", "values": ["Canvas", "Forced"], "default": "Canvas" },
    { "property": "forcedColor",   "group": "lighting", "label": "Forced Color",  "min": "0", "max": "360", "type": "color",    "default": "#009bde" },
  ];
}

// One entry per LED. Positions are [x, y] inside Size().
const vLedNames = [
  "Led 1", "Led 2", "Led 3", "Led 4", "Led 5", "Led 6", "Led 7", "Led 8",
];
const vLedPositions = [
  [0, 0], [1, 0], [2, 0], [3, 0], [4, 0], [5, 0], [6, 0], [7, 0],
];
export function LedNames()     { return vLedNames; }
export function LedPositions() { return vLedPositions; }

// Pick which HID interface/collection to open. Get these numbers from the
// device's HID report descriptor or from SignalRGB's device log.
export function Validate(endpoint) {
  return endpoint.interface === 0 && endpoint.usage === 0x0000 && endpoint.usage_page === 0x0000 && endpoint.collection === 0x0000;
}

export function Initialize() {
  // Send any handshake / "enter software mode" packets here.
  device.log("Initialize");
}

export function Render() {
  sendColors();
  device.pause(1);   // ms; keeps the render loop from saturating the USB bus
}

export function Shutdown(SystemSuspending) {
  if (SystemSuspending) {
    sendColors("#000000");        // go dark on sleep/shutdown
  } else {
    sendColors(shutdownColor);    // user-chosen color when SignalRGB exits
  }
}

// Called by SignalRGB right after a user changes the matching control.
export function onLightingModeChanged() { device.log("LightingMode -> " + LightingMode); }

function sendColors(overrideColor) {
  // Example: [reportId, header..., r,g,b, r,g,b, ...]  -- replace with your device's real format.
  const packet = [0x00, 0x00, 0x00, 0x00];
  const headerLen = packet.length;

  for (let i = 0; i < vLedPositions.length; i++) {
    const [x, y] = vLedPositions[i];
    let rgb;
    if (overrideColor)                 rgb = hexToRgb(overrideColor);
    else if (LightingMode === "Forced") rgb = hexToRgb(forcedColor);
    else                                rgb = device.color(x, y);   // sample the canvas
    packet[headerLen + i * 3 + 0] = rgb[0];
    packet[headerLen + i * 3 + 1] = rgb[1];
    packet[headerLen + i * 3 + 2] = rgb[2];
  }

  // device.write(packet, 65)        -> HID output report
  // device.send_report(packet, 65)  -> HID feature report
  device.send_report(packet, 65);
}

function hexToRgb(hex) {
  const m = /^#?([a-f\d]{2})([a-f\d]{2})([a-f\d]{2})$/i.exec(hex);
  return m ? [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)] : [0, 0, 0];
}
