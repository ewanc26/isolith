using System;
using Godot;

namespace Isolith.Core;

/// <summary>
/// Player settings, persisted to <c>user://settings.cfg</c>.
/// </summary>
/// <remarks>
/// Godot's <see cref="ConfigFile"/> rather than JSON: it is an INI the player
/// can open and edit by hand, which is the right affordance for a settings file
/// and costs nothing here.
///
/// Every setter applies its change immediately and then saves. Settings that
/// need doing once at startup — audio buses, window mode — go through
/// <see cref="Apply"/>, which is called before the first menu is shown.
/// </remarks>
public static class Settings
{
    /// <summary>Where preferences live. Public so the smoke test can assert on it.</summary>
    public const string FilePath = "user://settings.cfg";

    private const string Section = "isolith";

    private static ConfigFile _file = new();
    private static bool _loaded;

    /// <summary>Raised after any setting changes, so open UI can refresh.</summary>
    public static event Action? Changed;

    // --- Values --------------------------------------------------------------

    /// <summary>Overall volume, 0–1.</summary>
    public static float MasterVolume
    {
        get => Get(nameof(MasterVolume), 0.8f);
        set => Set(nameof(MasterVolume), Mathf.Clamp(value, 0f, 1f));
    }

    /// <summary>Sound-effect volume relative to master, 0–1.</summary>
    public static float EffectsVolume
    {
        get => Get(nameof(EffectsVolume), 0.9f);
        set => Set(nameof(EffectsVolume), Mathf.Clamp(value, 0f, 1f));
    }

    /// <summary>Default orthographic view height, in metres.</summary>
    public static float CameraZoom
    {
        get => Get(nameof(CameraZoom), 17.0f);
        set => Set(nameof(CameraZoom), Mathf.Clamp(value, 9.0f, 30.0f));
    }

    public static bool Fullscreen
    {
        get => Get(nameof(Fullscreen), false);
        set => Set(nameof(Fullscreen), value);
    }

    /// <summary>Whether the endless director explains each adjustment on screen.</summary>
    public static bool ShowDirectorNotes
    {
        get => Get(nameof(ShowDirectorNotes), true);
        set => Set(nameof(ShowDirectorNotes), value);
    }

    /// <summary>Publish every completed run to the repo when signed in.</summary>
    public static bool AutoSync
    {
        get => Get(nameof(AutoSync), false);
        set => Set(nameof(AutoSync), value);
    }

    /// <summary>Handle remembered between sessions. The app password never is.</summary>
    public static string SyncHandle
    {
        get => Get(nameof(SyncHandle), string.Empty);
        set => Set(nameof(SyncHandle), value);
    }

    public static string SyncService
    {
        get => Get(nameof(SyncService), "https://bsky.social");
        set => Set(nameof(SyncService), value);
    }

    // --- Lifecycle -----------------------------------------------------------

    /// <summary>Applies everything that has to be pushed into the engine.</summary>
    public static void Apply()
    {
        Load();

        int master = AudioServer.GetBusIndex("Master");
        if (master >= 0)
        {
            // Linear-to-dB: a volume slider that moves in decibels feels dead
            // across most of its travel.
            AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb(Mathf.Max(MasterVolume, 0.0001f)));
            AudioServer.SetBusMute(master, MasterVolume <= 0.001f);
        }

        DisplayServer.WindowMode desired = Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed;

        // Only when it actually differs. Apply runs after every setter, and
        // camera zoom is a setter the player can hit several times a second —
        // re-asserting the window mode on each one makes the window flicker.
        if (DisplayServer.WindowGetMode() != desired)
            DisplayServer.WindowSetMode(desired);
    }

    /// <summary>Restores every setting to its default.</summary>
    public static void Reset()
    {
        _file = new ConfigFile();
        _loaded = true;
        Save();
        Apply();
        Changed?.Invoke();
    }

    // --- Storage -------------------------------------------------------------

    private static void Load()
    {
        if (_loaded)
            return;

        _loaded = true;

        // A missing or unreadable file is the normal first-run case, not an
        // error: defaults simply stand.
        if (_file.Load(FilePath) != Error.Ok)
            _file = new ConfigFile();
    }

    // [MustBeVariant] is what lets a generic type parameter cross the Variant
    // boundary at all: Variant.From and Variant.As are both constrained to it,
    // and without the annotation the Godot analyser rejects the call (GD0302).
    private static T Get<[MustBeVariant] T>(string key, T fallback)
    {
        Load();
        return _file.GetValue(Section, key, Variant.From(fallback)).As<T>();
    }

    private static void Set<[MustBeVariant] T>(string key, T value)
    {
        Load();
        _file.SetValue(Section, key, Variant.From(value));
        Save();
        Apply();
        Changed?.Invoke();
    }

    private static void Save()
    {
        Error error = _file.Save(FilePath);

        if (error != Error.Ok)
            GD.PushWarning($"Isolith: could not save settings ({error}).");
    }
}
