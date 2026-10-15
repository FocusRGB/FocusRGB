# SignalRGB developer cheat sheet

## Paths

| What | Path |
|---|---|
| Custom effects | `%USERPROFILE%\Documents\WhirlwindFX\Effects\` |
| User plugins | `%USERPROFILE%\Documents\WhirlwindFX\Plugins\` |
| Components | `%USERPROFILE%\Documents\WhirlwindFX\Components\` |
| Built-in plugins/effects | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Plugins\`, `...\effects\` |
| Logs | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Logs\` |
| Canvas API | `POST http://localhost:16034/canvas/event?sender=<app>&event=<event>` |

New effect files: restart SignalRGB. User plugins override built-ins with the same VID/PID.

## Effects (lightscripts)

An effect is an HTML page. SignalRGB renders it on a 320x200 canvas and each device samples
its own rectangle of that canvas.

```html
<head>
  <title>Name</title>
  <meta description="..." />
  <meta publisher="..." />
  <meta property="speed" label="Speed" type="number" min="1" max="20" default="5" />
  <meta property="c1"    label="Color" type="color" default="#00a8ff" />
  <meta property="mode"  label="Mode"  type="combobox" values="A,B" default="A" />
  <meta property="on"    label="On"    type="boolean" default="1" />
  <meta property="name"  label="Name"  type="textfield" default="x" />
</head>
<body style="margin:0;padding:0;">
  <canvas id="exCanvas" width="320" height="200"></canvas>
  <script>
    // each <meta property> is exposed as a global of that name
    function draw(){ /* ... */ window.requestAnimationFrame(draw); }
    window.requestAnimationFrame(draw);
    function onCanvasApiEvent(e){ /* e.sender, e.event */ }   // integrations
  </script>
</body>
```

Game integrations are effects that either (a) implement `onCanvasApiEvent` and get HTTP POSTs
from a game/mod/script, or (b) use a UI analyzer to read screen pixels (see docs).

## Plugins (device drivers)

Exports SignalRGB calls:

| Export | Returns |
|---|---|
| `Name()`, `Publisher()`, `Documentation()` | strings |
| `VendorId()`, `ProductId()` | `0xNNNN` |
| `Type()` | `"Hid"` (default) or `"RawUsb"` |
| `Size()` | `[w, h]` canvas cells |
| `DefaultPosition()`, `DefaultScale()` | `[x, y]`, float 1..30 |
| `LedNames()`, `LedPositions()` | arrays, same length, positions `[x, y]` within Size |
| `ControllableParameters()` | array of `{property,label,type,default,min,max,step,values,group}` |
| `Validate(endpoint)` | bool; `endpoint.interface / usage / usage_page / collection` |
| `ConflictingProcesses()` | `["Foo.exe"]` |
| `ImageUrl()` | string |
| `Initialize()` | once on connect |
| `Render()` | every frame (~30 ms) |
| `Shutdown(SystemSuspending)` | on exit / sleep |
| `on<Property>Changed()` | after a control changes |

Device object inside a plugin:

| Call | Meaning |
|---|---|
| `device.color(x, y)` | `[r,g,b]` sampled from the canvas |
| `device.write(bytes, len)` | HID output report |
| `device.send_report(bytes, len)` | HID feature report |
| `device.read(bytes, len, timeoutMs?)` | HID input read (blocking; `0` = peek) |
| `device.get_report(bytes, len)` | HID feature read |
| `device.getLastReadSize()` | bytes returned by last read |
| `device.clearReadBuffer()`, `device.flush()` | drop queued input |
| `device.control_transfer(...)` | raw USB control transfer (RawUsb) |
| `device.pause(ms)` | throttle the render loop |
| `device.log(text)` | to device log / console |

Control types: `boolean`, `number`, `hue`, `color`, `combobox`, `textfield`.
Declare a top-level `let <property> = <default>` for each control.

## Finding VID/PID and endpoints on this PC

```powershell
Get-PnpDevice -PresentOnly | Where-Object InstanceId -match 'VID_1532' |
  ForEach-Object { $_.InstanceId; (Get-PnpDeviceProperty $_.InstanceId DEVPKEY_Device_HardwareIds).Data -match 'UP:' }
```

`UP:xxxx_U:yyyy` in the hardware ID is the usage page / usage a `Validate()` can match on.
Razer vendor collections are usually `usage_page 0xFF00`-range. Wireshark + USBPcap captures the
packets the vendor app sends; see the plugin tutorial.

## Known devices here

| Device | VID | PID | Notes |
|---|---|---|---|
| Razer BlackWidow V4 | 0x1532 | 0x0287 | LampArray interface observed on MI_04 (UP:0059); prior setup used SignalRGB with Dynamic Lighting disabled. Native LampArray control is unverified. |
| Razer Basilisk V3 | 0x1532 | 0x0099 | No LampArray. Built-in SignalRGB Razer plugin drives it directly. |
| WLED Desk | net | 10.0.0.36 | 300 LEDs, WLED 16.0.1, discovered over the network |
| WLED Wall | net | 10.0.0.37 | 300 LEDs, WLED 16.0.1, discovered over the network |

## Links

- https://docs.signalrgb.com/developer/
- https://docs.signalrgb.com/developer/lightscripts/
- https://docs.signalrgb.com/developer/lightscripts/creating-dev-integrations/
- https://docs.signalrgb.com/developer/plugins/plugin-exports/
- https://docs.signalrgb.com/developer/plugins/writes-and-reads/
- https://docs.signalrgb.com/developer/plugins/user-controls/
- https://docs.signalrgb.com/developer/plugins/tutorial/
- https://docs.signalrgb.com/guides/installation-setup/file-locations/
- https://github.com/SRGBmods/public
- https://github.com/Derek4aty1/signalrgb-extras
