# Python plugin example

This minimal plugin implements the FocusRGB 1.0 protocol with Python's standard library. It
does not use `FocusRgb.PluginSdk`, demonstrating that the wire contract is language-neutral.

Run its conformance check from the repository root:

```powershell
python tools/plugin-conformance.py --plugin-id python-example -- python examples/python-plugin/plugin.py
```

The manifest targets Linux and macOS because those platforms can launch the executable script
directly through its shebang. Windows packages should use a native executable or the .NET SDK
template rather than relying on file associations.
