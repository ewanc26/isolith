using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;

namespace Isolith.Sync.Interop;

/// <summary>
/// Locates and loads <c>libwolfram</c> — the native AT Protocol SDK this game
/// talks to. Nothing here reimplements protocol logic; it only resolves the
/// shared library so the P/Invoke declarations in <see cref="WolframNative"/>
/// bind.
/// </summary>
/// <remarks>
/// Search order, first hit wins:
/// <list type="number">
///   <item><c>WOLFRAM_NATIVE_LIB</c> — absolute path override (same env var the
///   upstream <c>Wolfram.Interop</c> wrapper honours).</item>
///   <item><c>res://native/</c> — the checked-out project's vendored copy.
///   Works in the editor and in exported builds.</item>
///   <item>The directory holding the running executable.</item>
///   <item>The bare library name, letting the OS loader search its own paths.</item>
/// </list>
/// </remarks>
public static class WolframLibrary
{
    /// <summary>The name used in <c>[LibraryImport]</c> declarations.</summary>
    public const string ImportName = "wolfram";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Absolute path of the library that was actually loaded, once known.</summary>
    public static string? ResolvedPath { get; private set; }

    /// <summary>
    /// Installs the resolver. Idempotent and safe to call from any static
    /// constructor that is about to touch native code.
    /// </summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered)
                return;

            NativeLibrary.SetDllImportResolver(typeof(WolframLibrary).Assembly, Resolve);
            _registered = true;
        }
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, ImportName, StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        foreach (string candidate in Candidates())
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                ResolvedPath = candidate;
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string[] Candidates()
    {
        string fileName = PlatformFileName();

        string? env = System.Environment.GetEnvironmentVariable("WOLFRAM_NATIVE_LIB");

        // ProjectSettings.GlobalizePath resolves res:// in the editor; in an
        // exported build res:// lives inside the .pck, so the vendored copy is
        // shipped next to the executable instead (see the export step in
        // README.md) and picked up by the executable-directory candidate.
        string vendored = ProjectSettings.GlobalizePath($"res://native/{fileName}");

        string executableDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? string.Empty;
        string besideExecutable = executableDir.Length > 0
            ? Path.Combine(executableDir, fileName)
            : string.Empty;

        return new[] { env ?? string.Empty, vendored, besideExecutable, fileName };
    }

    private static string PlatformFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "wolfram.dll";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "libwolfram.dylib";
        return "libwolfram.so";
    }
}
