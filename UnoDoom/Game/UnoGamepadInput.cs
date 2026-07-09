using ManagedDoom;
using Microsoft.UI.Dispatching;
using Windows.Gaming.Input;

namespace UnoDoom.Game;

public class UnoGamepadInput : IDisposable
{
    private readonly DispatcherTimer _updateTimer;
    private readonly Config _config;
    private readonly UnoUserInput _userInput;
    private Doom? _doom;
    
    // Gamepad state tracking
    private GamepadButtons _previousButtons = GamepadButtons.None;
    private Dictionary<GamepadButtons, DoomKey> _buttonMapping = new();
    
    // Deadzone and sensitivity settings
    private const double ThumbstickDeadzone = 0.2;
    private const double TriggerDeadzone = 0.1;
    private const double MovementSensitivity = 1.5;
    private const double LookSensitivity = 2.0;
    
    // Movement state
    private bool _isMovingForward;
    private bool _isMovingBackward;
    private bool _isStrafingLeft;
    private bool _isStrafingRight;
    private bool _isTurningLeft;
    private bool _isTurningRight;
    private bool _wasRunning;
    private bool _wasFiring;

    public UnoGamepadInput(Config config, UnoUserInput userInput)
    {
        _config = config;
        _userInput = userInput;
        
        InitializeButtonMapping();
        
        // Set up timer for continuous gamepad reading
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
        };
        _updateTimer.Tick += UpdateGamepadState;
        
        // Subscribe to gamepad events
        Gamepad.GamepadAdded += OnGamepadAdded;
        Gamepad.GamepadRemoved += OnGamepadRemoved;
        
        _updateTimer.Start();
    }

    private void InitializeButtonMapping()
    {
        // Map gamepad buttons to actual DOOM keys based on default key bindings
        _buttonMapping = new Dictionary<GamepadButtons, DoomKey>
        {
            { GamepadButtons.A, DoomKey.LControl }, // Fire (default: Ctrl)
            { GamepadButtons.B, DoomKey.Space }, // Use (default: Space)
            { GamepadButtons.X, DoomKey.A }, // Strafe left
            { GamepadButtons.Y, DoomKey.D }, // Strafe right
            
            { GamepadButtons.LeftShoulder, DoomKey.Num1 }, // Previous weapon (weapon keys)
            { GamepadButtons.RightShoulder, DoomKey.Num2 }, // Next weapon
            
            // D-Pad mapping is context-sensitive (handled in ProcessDPad)
            // In menu: Up/Down for navigation, in-game: movement/turning
            
            { GamepadButtons.Menu, DoomKey.Escape }, // Menu/Back
            { GamepadButtons.View, DoomKey.Tab }, // Automap
            
            { GamepadButtons.LeftThumbstick, DoomKey.LShift }, // Run
            { GamepadButtons.RightThumbstick, DoomKey.LAlt } // Strafe modifier
        };
        
        // For menu navigation, we also need to map A button to Enter for confirmation
        // This will be handled in ProcessDPad method based on context
    }

    public void SetDoom(Doom doom)
    {
        _doom = doom;
    }

    private void OnGamepadAdded(object? sender, Gamepad gamepad)
    {
        Console.WriteLine($"Gamepad added: {gamepad}");
    }

    private void OnGamepadRemoved(object? sender, Gamepad gamepad)
    {
        Console.WriteLine($"Gamepad removed: {gamepad}");
    }

    private void UpdateGamepadState(object? sender, object e)
    {
        if (_doom == null)
            return;

        IReadOnlyList<Gamepad> gamepads;
        try
        {
            gamepads = Gamepad.Gamepads;
        }
        catch (NotImplementedException)
        {
            // Gamepad API is unavailable on this platform (e.g. Uno Win32 desktop).
            // Stop polling so we don't throw on every tick.
            _updateTimer.Stop();
            return;
        }

        if (gamepads.Count == 0)
            return;

        var gamepad = gamepads[0]; // Use first gamepad
        var reading = gamepad.GetCurrentReading();
        
        ProcessButtons(reading);
        ProcessAnalogInputs(reading);
    }

    private void ProcessButtons(GamepadReading reading)
    {
        var currentButtons = reading.Buttons;
        var changedButtons = currentButtons ^ _previousButtons;
        bool isInMenu = _doom?.Menu.Active ?? false;
        
        // Handle D-Pad context-sensitively
        ProcessDPad(reading, changedButtons);
        
        // Handle A button context-sensitively (Enter in menu, Fire in game)
        if ((changedButtons & GamepadButtons.A) != 0)
        {
            bool isPressed = (currentButtons & GamepadButtons.A) != 0;
            var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
            var doomKey = isInMenu ? DoomKey.Enter : DoomKey.LControl;
            _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
        }
        
        // Handle other buttons
        foreach (var mapping in _buttonMapping)
        {
            var button = mapping.Key;
            var doomKey = mapping.Value;
            
            // Skip A button as it's handled above context-sensitively
            if (button == GamepadButtons.A)
                continue;
            
            if ((changedButtons & button) != 0)
            {
                bool isPressed = (currentButtons & button) != 0;
                var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
                
                _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
            }
        }
        
        _previousButtons = currentButtons;
    }
    
    private void ProcessDPad(GamepadReading reading, GamepadButtons changedButtons)
    {
        var currentButtons = reading.Buttons;
        bool isInMenu = _doom?.Menu.Active ?? false;
        
        // D-Pad Up
        if ((changedButtons & GamepadButtons.DPadUp) != 0)
        {
            bool isPressed = (currentButtons & GamepadButtons.DPadUp) != 0;
            var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
            var doomKey = isInMenu ? DoomKey.Up : DoomKey.W; // Menu navigation or forward movement
            _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
        }
        
        // D-Pad Down  
        if ((changedButtons & GamepadButtons.DPadDown) != 0)
        {
            bool isPressed = (currentButtons & GamepadButtons.DPadDown) != 0;
            var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
            var doomKey = isInMenu ? DoomKey.Down : DoomKey.S; // Menu navigation or backward movement
            _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
        }
        
        // D-Pad Left
        if ((changedButtons & GamepadButtons.DPadLeft) != 0)
        {
            bool isPressed = (currentButtons & GamepadButtons.DPadLeft) != 0;
            var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
            var doomKey = DoomKey.Left; // Always turn left
            _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
        }
        
        // D-Pad Right
        if ((changedButtons & GamepadButtons.DPadRight) != 0)
        {
            bool isPressed = (currentButtons & GamepadButtons.DPadRight) != 0;
            var eventType = isPressed ? EventType.KeyDown : EventType.KeyUp;
            var doomKey = DoomKey.Right; // Always turn right
            _userInput.SetKeyStatus(eventType, doomKey, _doom!, new EventTimestamp());
        }
    }

    private void ProcessAnalogInputs(GamepadReading reading)
    {
        // Process left thumbstick (movement)
        ProcessMovementStick(reading.LeftThumbstickX, reading.LeftThumbstickY);
        
        // Process right thumbstick (looking/turning)
        ProcessLookStick(reading.RightThumbstickX, reading.RightThumbstickY);
        
        // Process triggers
        ProcessTriggers(reading.LeftTrigger, reading.RightTrigger);
    }

    private void ProcessMovementStick(double x, double y)
    {
        // Apply deadzone
        if (Math.Abs(x) < ThumbstickDeadzone) x = 0;
        if (Math.Abs(y) < ThumbstickDeadzone) y = 0;
        
        // Forward/Backward movement (Y-axis)
        bool shouldMoveForward = y > ThumbstickDeadzone;
        bool shouldMoveBackward = y < -ThumbstickDeadzone;
        
        if (shouldMoveForward != _isMovingForward)
        {
            var eventType = shouldMoveForward ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.W, _doom!, new EventTimestamp());
            _isMovingForward = shouldMoveForward;
        }
        
        if (shouldMoveBackward != _isMovingBackward)
        {
            var eventType = shouldMoveBackward ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.S, _doom!, new EventTimestamp());
            _isMovingBackward = shouldMoveBackward;
        }
        
        // Strafe movement (X-axis)
        bool shouldStrafeRight = x > ThumbstickDeadzone;
        bool shouldStrafeLeft = x < -ThumbstickDeadzone;
        
        if (shouldStrafeRight != _isStrafingRight)
        {
            var eventType = shouldStrafeRight ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.D, _doom!, new EventTimestamp());
            _isStrafingRight = shouldStrafeRight;
        }
        
        if (shouldStrafeLeft != _isStrafingLeft)
        {
            var eventType = shouldStrafeLeft ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.A, _doom!, new EventTimestamp());
            _isStrafingLeft = shouldStrafeLeft;
        }
    }

    private void ProcessLookStick(double x, double y)
    {
        // Apply deadzone
        if (Math.Abs(x) < ThumbstickDeadzone) x = 0;
        
        // Turning (X-axis)
        bool shouldTurnRight = x > ThumbstickDeadzone;
        bool shouldTurnLeft = x < -ThumbstickDeadzone;
        
        if (shouldTurnRight != _isTurningRight)
        {
            var eventType = shouldTurnRight ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.Right, _doom!, new EventTimestamp());
            _isTurningRight = shouldTurnRight;
        }
        
        if (shouldTurnLeft != _isTurningLeft)
        {
            var eventType = shouldTurnLeft ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.Left, _doom!, new EventTimestamp());
            _isTurningLeft = shouldTurnLeft;
        }
        
        // Y-axis could be used for looking up/down if the engine supported it
        // For now, we'll ignore it since classic DOOM doesn't have vertical look
    }

    private void ProcessTriggers(double leftTrigger, double rightTrigger)
    {
        // Left trigger for running
        bool shouldRun = leftTrigger > TriggerDeadzone;
        
        if (shouldRun != _wasRunning)
        {
            var eventType = shouldRun ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.LShift, _doom!, new EventTimestamp());
            _wasRunning = shouldRun;
        }
        
        // Right trigger for firing
        bool shouldFire = rightTrigger > TriggerDeadzone;
        
        if (shouldFire != _wasFiring)
        {
            var eventType = shouldFire ? EventType.KeyDown : EventType.KeyUp;
            _userInput.SetKeyStatus(eventType, DoomKey.LControl, _doom!, new EventTimestamp());
            _wasFiring = shouldFire;
        }
    }

    public void Dispose()
    {
        _updateTimer?.Stop();
        Gamepad.GamepadAdded -= OnGamepadAdded;
        Gamepad.GamepadRemoved -= OnGamepadRemoved;
        _updateTimer?.Tick -= UpdateGamepadState;
    }
}