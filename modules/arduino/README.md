# Arduino module

Earlier Arduino-driven LED experiments live in this directory. If FocusRGB later supports an
Arduino output, create a `FocusRGB-Plugin-Arduino` repository when that work is scheduled. The
core and app reference only shared contracts, never integration-specific source. No controller
plugin exists yet.

## Layout

```text
sketches/   standalone Arduino sketches; each sketch keeps its own same-named folder
```

`sketches/LEDStripTester/` is an earlier, independent hardware experiment. It cycles test
patterns on a 32-LED WS2812B strip on pin 5 and needs the `WS2812BStrip` library. It does
not talk to FocusRGB or SignalRGB.

Define a future plugin's transport (for example serial) and device ownership through the
published plugin contract before adding firmware that depends on it.
