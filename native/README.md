# native/

The native AT Protocol SDK goes here.

Isolith's optional stat sync calls into [`libwolfram`][wolfram], a C11
implementation of the AT Protocol wire layer. The shared library is **not**
vendored into this repository — it is built from its own source tree and dropped
in here.

```bash
git clone https://github.com/ewanc26/wolfram.git
cd wolfram
cmake -S . -B build && cmake --build build
```

Then copy the result into this directory:

| Platform | File                    |
| -------- | ----------------------- |
| macOS    | `build/libwolfram.dylib`|
| Linux    | `build/libwolfram.so`   |
| Windows  | `build/wolfram.dll`     |

```bash
cp build/libwolfram.dylib /path/to/isolith/native/
```

Alternatively, point `WOLFRAM_NATIVE_LIB` at an absolute path and skip the copy:

```bash
export WOLFRAM_NATIVE_LIB=/abs/path/to/libwolfram.dylib
```

**The game does not need this file.** Without it, sync is unavailable and
everything else — every course, every run, the full local history — works
exactly as normal. See `src/Sync/Interop/WolframLibrary.cs` for the full
search order.

For an exported build, place the library next to the game executable rather
than in `res://`, since `res://` lives inside the `.pck`.

[wolfram]: https://github.com/ewanc26/wolfram
