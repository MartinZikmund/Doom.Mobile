using ManagedDoom;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using System.Diagnostics;
#if HAS_UNO
using Uno.WinUI.Graphics2DSK;
#endif
#if WINDOWS || __MACCATALYST__ || __MACOS__
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace UnoDoom.Game;

#if HAS_UNO
/// <summary>
/// Event args for canvas rendering.
/// </summary>
internal class SKCanvasElementPaintEventArgs : EventArgs
{
    public SKCanvas Canvas { get; internal set; } = null!;
    public Windows.Foundation.Size Size { get; internal set; }
}

/// <summary>
/// Custom SKCanvasElement for rendering the DOOM game.
/// </summary>
internal class DoomCanvasElement : SKCanvasElement
{
    private readonly SKCanvasElementPaintEventArgs _eventArgs = new();

    public event EventHandler<SKCanvasElementPaintEventArgs>? PaintSurface;

    protected override void RenderOverride(SKCanvas canvas, Windows.Foundation.Size size)
    {
        _eventArgs.Canvas = canvas;
        _eventArgs.Size = size;
        PaintSurface?.Invoke(this, _eventArgs);
    }
}
#endif

public partial class UnoDoomGame : UserControl
{
    private UnoVideo? _video;
    private UnoSound? _sound;
    private UnoMusic? _music;
    private UnoUserInput? _input;
    private UnoGamepadInput? _gamepadInput;

    private CommandLineArgs? args;
    private Config? _config;
    private GameContent? _content;
    private Doom? _doom;

    private int fpsScale;
    private int frameCount;
    private bool _initialized;
    private bool _touchOverlayEnabled;

    /// <summary>
    /// Gets or sets the path to the WAD file to load.
    /// Must be set before the control is loaded.
    /// </summary>
    public string? WadPath { get; set; }

    /// <summary>
    /// Event raised when the user requests to exit the game and return to WAD selection.
    /// </summary>
    public event EventHandler? ExitRequested;

    private DispatcherTimer? _gameTimer;
#if HAS_UNO
    private DoomCanvasElement _canvas;
#endif

    public UnoDoomGame()
    {
        this.InitializeComponent();
        
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var scalingBox = new Viewbox();
        scalingBox.Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform;
        CanvasRoot.Child = scalingBox;

#if HAS_UNO
        _canvas = new DoomCanvasElement();
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvas.VerticalAlignment = VerticalAlignment.Stretch;
        _canvas.Width = 320;
        _canvas.Height = 200;

        scalingBox.Child = _canvas;
#endif

        this.Loaded += UnoDoomGame_Loaded;
        this.Unloaded += UnoDoomGame_Unloaded;
        this.KeyDown += UnoDoomGame_KeyDown;
        this.KeyUp += UnoDoomGame_KeyUp;
        this.LosingFocus += UnoDoomGame_LosingFocus;

        // Enable touch overlay by default on mobile platforms
        _touchOverlayEnabled = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
    }

    /// <summary>
    /// Gets or sets whether the touch input overlay is enabled.
    /// </summary>
    public bool IsTouchOverlayEnabled
    {
        get => _touchOverlayEnabled;
        set
        {
            _touchOverlayEnabled = value;
            UpdateTouchOverlayVisibility();
        }
    }

    /// <summary>
    /// Toggles the touch input overlay on or off.
    /// </summary>
    public void ToggleTouchOverlay()
    {
        IsTouchOverlayEnabled = !IsTouchOverlayEnabled;
    }

    private void UpdateTouchOverlayVisibility()
    {
        TouchOverlay.Visibility = _touchOverlayEnabled ? Visibility.Visible : Visibility.Collapsed;
        ShowTouchOverlayButton.Visibility = _touchOverlayEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowTouchOverlayButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        IsTouchOverlayEnabled = true;
        e.Handled = true;
    }

    private void ExitButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Unsubscribe LosingFocus to allow proper focus release during navigation
        this.LosingFocus -= UnoDoomGame_LosingFocus;
        ExitRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>
    /// Requests exit from the game (called when game ends or user exits via menu).
    /// </summary>
    public void RequestExit()
    {
        // Unsubscribe LosingFocus to allow proper focus release during navigation
        this.LosingFocus -= UnoDoomGame_LosingFocus;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UnoDoomGame_LosingFocus(UIElement sender, LosingFocusEventArgs args)
    {
        // Always keep focus
        args.Cancel = true;
    }

    private async void UnoDoomGame_Loaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to touch overlay hide event
        TouchOverlay.HideRequested += TouchOverlay_HideRequested;
        
        await InitializeGameAsync();
        StartGameLoop();
    }

    private void TouchOverlay_HideRequested(object? sender, EventArgs e)
    {
        IsTouchOverlayEnabled = false;
    }

    private void UnoDoomGame_Unloaded(object sender, RoutedEventArgs e)
    {
        TouchOverlay.HideRequested -= TouchOverlay_HideRequested;
        StopGameLoop();
        CleanupGame();
    }

    private async Task InitializeGameAsync()
    {
        if (_initialized)
            return;

        try
        {
#if __WASM__ || __ANDROID__ || __IOS__
            // For WASM and Android prepare assets first
            await ConfigUtilities.PrepareAssetsAsync();
#endif

            PlatformHelpers.ConfigUtilities = new ConfigUtilities();

            // Create command line args with WAD path if specified
            string[] cmdArgs = string.IsNullOrEmpty(WadPath)
                ? Array.Empty<string>()
                : new string[] { "-iwad", WadPath };

            args = new CommandLineArgs(cmdArgs);
            var configUtilities = new ConfigUtilities();
            _config = configUtilities.GetConfig();
            _content = new GameContent(args);

            _config.video_screenwidth = Math.Clamp(_config.video_screenwidth, 320, 3200);
            _config.video_screenheight = Math.Clamp(_config.video_screenheight, 200, 2000);
            _config.video_fpsscale = Math.Clamp(_config.video_fpsscale, 1, 100);

            _video = new UnoVideo(_config, _content);
            _sound = new UnoSound(_config, _content);
            _music = new UnoMusic(_config, _content);
            _input = new UnoUserInput(_config, !args.nomouse.Present);

            // Initialize gamepad input
            _gamepadInput = new UnoGamepadInput(_config, _input);

            _doom = new Doom(args, _config, _content, _video, _sound, _music, _input);

            // Set the DOOM instance for gamepad input
            _gamepadInput.SetDoom(_doom);

            // Initialize touch overlay
            TouchOverlay.Initialize(_input, _doom);
            _input.SetTouchOverlay(TouchOverlay);
            UpdateTouchOverlayVisibility();

            fpsScale = args.timedemo.Present ? 1 : _config.video_fpsscale;
            frameCount = -1;

            _initialized = true;
            Console.WriteLine("Game initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize game: {ex.Message}");
            Debug.WriteLine($"Game initialization error: {ex}");
        }
    }

    private void StartGameLoop()
    {
        _gameTimer = new DispatcherTimer();
        _gameTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0); // 60 FPS target
#if HAS_UNO
        _gameTimer.Tick += GameTimer_Tick;
#endif
        _gameTimer.Start();
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private void StopGameLoop()
    {
        if (_gameTimer != null)
        {
            _gameTimer.Stop();
#if HAS_UNO
            _gameTimer.Tick -= GameTimer_Tick;
#endif
            _gameTimer = null;
        }
    }

#if HAS_UNO
    private void GameTimer_Tick(object? sender, object e)
    {
        _canvas?.Invalidate();
    }

    private void OnPaintSurface(object? sender, SKCanvasElementPaintEventArgs e)
    {
        if (!_initialized || _doom == null)
            return;

        var canvas = e.Canvas;
        canvas.Clear(SKColors.Black);

        var destination = new SKRect(0, 0, (float)e.Size.Width, (float)e.Size.Height);

        frameCount++;

        // Update touch overlay frame count
        TouchOverlay.SetFrameCount(frameCount);

        // Update menu state for touch overlay
        _input?.SetMenuState(_doom!.Menu.Active);

        var frameFrac = Fixed.FromInt(1);

        // Update game every N frames based on fpsScale
        if (frameCount % fpsScale == 0)
        {
            _input?.Update(_doom!, new EventTimestamp(frameCount));

            if (_doom!.Update() == UpdateResult.Completed)
            {
                // Game completed - return to WAD selection
                RequestExit();
                return;
            }

            frameFrac /= 2;
        }

        // Render the game
        _video?.Render(canvas, destination, _doom!, frameFrac);
    }
#endif

    private void UnoDoomGame_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_doom == null || _input == null || !_initialized)
            return;

        var doomKey = UnoUserInput.VirtualKeyToDoom(e.Key);
        if (doomKey != DoomKey.Unknown)
        {
            _input.SetKeyStatus(EventType.KeyDown, doomKey, _doom, new EventTimestamp(frameCount));
            e.Handled = true;
        }
    }

    private void UnoDoomGame_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_doom == null || _input == null || !_initialized)
            return;

        var doomKey = UnoUserInput.VirtualKeyToDoom(e.Key);
        if (doomKey != DoomKey.Unknown)
        {
            _input.SetKeyStatus(EventType.KeyUp, doomKey, _doom, new EventTimestamp(frameCount));
            e.Handled = true;
        }
    }

    private void CleanupGame()
    {
        _gamepadInput?.Dispose();
        _gamepadInput = null;
        
        _doom = null;
        _video?.Dispose();
        _sound?.Dispose();
        _music?.Dispose();
        _input?.Dispose();
        _content = null;
        _initialized = false;
    }
}
