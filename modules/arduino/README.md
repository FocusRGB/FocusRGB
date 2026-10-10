# Arduino module

Everything specific to Arduino-driven LED hardware lives in this directory. A future FocusRGB
controller plugin for Arduino outputs will be added here as a separate plugin, following the
same rule as other modules: the core and app projects reference only the shared contracts,
never this module. No controller plugin exists yet.

## Layout

```text
sketches/   standalone Arduino sketches; each sketch keeps its own same-named folder
```

`sketches/LEDStripTester/` is an earlier, independent hardware experiment. It cycles test
patterns on a 32-LED WS2812B strip on pin 5 and needs the `WS2812BStrip` library. It does
not talk to FocusRGB or SignalRGB.

A future `controller-plugin/` directory will hold the Arduino output plugin. Define its
transport (for example serial) and device ownership through the plugin contract before
adding firmware that depends on it.
