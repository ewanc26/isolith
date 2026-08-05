using Godot;

namespace Isolith.Core;

/// <summary>
/// Every input action the game uses, plus the default bindings.
/// </summary>
/// <remarks>
/// Bindings are registered in code rather than stored in <c>project.godot</c>.
/// The editor's serialised input map is a dense, version-sensitive blob that is
/// painful to review in a diff; declaring it here keeps the controls readable,
/// mergeable, and identical across Godot versions. Registration skips any
/// action that already exists, so a user's remapping still wins.
/// </remarks>
public static class GameInput
{
    public const string MoveLeft = "move_left";
    public const string MoveRight = "move_right";
    public const string MoveForward = "move_forward";
    public const string MoveBack = "move_back";
    public const string Jump = "jump";
    public const string CameraRotateLeft = "camera_rotate_left";
    public const string CameraRotateRight = "camera_rotate_right";
    public const string ZoomIn = "zoom_in";
    public const string ZoomOut = "zoom_out";
    public const string Restart = "restart";
    public const string Pause = "pause";
    public const string ToggleSync = "toggle_sync";

    private static bool _configured;

    /// <summary>
    /// Registers default bindings for any action not already defined.
    /// </summary>
    /// <remarks>
    /// <b>Gamepad is the primary scheme.</b> Every action lists its controller
    /// binding first and its keyboard binding as the fallback, and the analog
    /// stick drives movement directly — <see cref="Input.GetVector"/> returns
    /// the stick's magnitude, so the character walks or runs with how far it is
    /// pushed rather than snapping to full speed the way keys do.
    ///
    /// A 0.2 deadzone on the movement axes is wide enough to ignore worn stick
    /// drift without eating slow, deliberate movement.
    /// </remarks>
    public static void Configure()
    {
        if (_configured)
            return;

        _configured = true;

        // Movement — left stick, with the D-pad and keyboard as fallbacks.
        Action(MoveLeft, 0.2f,
            Axis(JoyAxis.LeftX, -1.0f), Button(JoyButton.DpadLeft),
            Key(Godot.Key.A), Key(Godot.Key.Left));

        Action(MoveRight, 0.2f,
            Axis(JoyAxis.LeftX, 1.0f), Button(JoyButton.DpadRight),
            Key(Godot.Key.D), Key(Godot.Key.Right));

        Action(MoveForward, 0.2f,
            Axis(JoyAxis.LeftY, -1.0f), Button(JoyButton.DpadUp),
            Key(Godot.Key.W), Key(Godot.Key.Up));

        Action(MoveBack, 0.2f,
            Axis(JoyAxis.LeftY, 1.0f), Button(JoyButton.DpadDown),
            Key(Godot.Key.S), Key(Godot.Key.Down));

        // Jump — the south face button, where a platformer player expects it.
        Action(Jump, 0.5f,
            Button(JoyButton.A), Key(Godot.Key.Space));

        // View rotation — shoulders, or flick the right stick.
        Action(CameraRotateLeft, 0.5f,
            Button(JoyButton.LeftShoulder), Axis(JoyAxis.RightX, -1.0f), Key(Godot.Key.Q));

        Action(CameraRotateRight, 0.5f,
            Button(JoyButton.RightShoulder), Axis(JoyAxis.RightX, 1.0f), Key(Godot.Key.E));

        // Zoom — triggers, mouse wheel, or +/-.
        Action(ZoomIn, 0.5f,
            Axis(JoyAxis.TriggerRight, 1.0f), Wheel(MouseButton.WheelUp), Key(Godot.Key.Equal));

        Action(ZoomOut, 0.5f,
            Axis(JoyAxis.TriggerLeft, 1.0f), Wheel(MouseButton.WheelDown), Key(Godot.Key.Minus));

        Action(Restart, 0.5f,
            Button(JoyButton.Y), Key(Godot.Key.R));

        Action(Pause, 0.5f,
            Button(JoyButton.Start), Key(Godot.Key.Escape));

        Action(ToggleSync, 0.5f,
            Button(JoyButton.Back), Key(Godot.Key.F1));

        // Without this, unplugging the only connected pad leaves UsingGamepad
        // stuck true until the player happens to touch a key or the mouse —
        // control hints would keep naming buttons that no longer exist.
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
    }

    private static void OnJoyConnectionChanged(long device, bool connected)
    {
        if (!connected && Input.GetConnectedJoypads().Count == 0)
            UsingGamepad = false;
    }

    /// <summary>
    /// True when the most recent input came from a gamepad, so on-screen hints
    /// can name the right buttons.
    /// </summary>
    public static bool UsingGamepad { get; private set; } = Input.GetConnectedJoypads().Count > 0;

    /// <summary>
    /// Feed input events here to keep <see cref="UsingGamepad"/> current.
    /// </summary>
    public static void Observe(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventJoypadButton:
                UsingGamepad = true;
                break;

            // Sticks emit motion events continuously; only a deliberate push
            // counts, or idle drift would keep claiming the controller is in use.
            case InputEventJoypadMotion motion when Mathf.Abs(motion.AxisValue) > 0.35f:
                UsingGamepad = true;
                break;

            case InputEventKey { Pressed: true }:
            case InputEventMouseButton { Pressed: true }:
                UsingGamepad = false;
                break;
        }
    }

    private static void Action(string name, float deadzone, params InputEvent[] events)
    {
        // An action defined in project.godot or remapped by the player takes
        // precedence — defaults only fill gaps.
        if (InputMap.HasAction(name))
            return;

        InputMap.AddAction(name, deadzone);

        foreach (InputEvent @event in events)
            InputMap.ActionAddEvent(name, @event);
    }

    /// <summary>Binds by physical position, so WASD stays WASD on AZERTY layouts.</summary>
    private static InputEventKey Key(Key key) => new() { PhysicalKeycode = key };

    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Axis(JoyAxis axis, float value) =>
        new() { Axis = axis, AxisValue = value };

    private static InputEventMouseButton Wheel(MouseButton button) =>
        new() { ButtonIndex = button, Pressed = true };
}
