using ManagedDoom;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
#if WINDOWS || __MACCATALYST__ || __MACOS__
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace UnoDoom.Game;

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

    // XamlDoom: Doom renders at 640x400 by default. Downsample controls the grid:
    //   2 -> 320x200 = 64,000 rects   4 -> 160x100 = 16,000 rects   8 -> 80x50 = 4,000 rects
    // Overridable via the XAMLDOOM_DOWNSAMPLE env var (for profiling sweeps).
    private static readonly int PixelDownsample =
        int.TryParse(Environment.GetEnvironmentVariable("XAMLDOOM_DOWNSAMPLE"), out var d) && d > 0 ? d : 4;

    // --- profiling instrumentation (enable with XAMLDOOM_PERF=1) ---
    private readonly bool _perfEnabled = Environment.GetEnvironmentVariable("XAMLDOOM_PERF") == "1";
    private long _perfWindowStart;
    private int _renderFrames;   // real presented frames (CompositionTarget.Rendering)
    private int _tickFrames;     // game ticks in the window
    private double _frameCpuMsSum;
    private long _changedPxSum;

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
    private readonly Viewbox _scalingBox;
    private DoomRectangleCanvas? _rectCanvas;

    public UnoDoomGame()
    {
        this.InitializeComponent();

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        _scalingBox = new Viewbox
        {
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            UseLayoutRounding = false,
        };
        CanvasRoot.Child = _scalingBox;

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
        SetupRenderCanvas();
        StartGameLoop();
    }

    private void SetupRenderCanvas()
    {
        if (_video == null || _rectCanvas != null)
            return;

        _rectCanvas = new DoomRectangleCanvas(_video.ScreenWidth, _video.ScreenHeight, PixelDownsample);
        _scalingBox.Child = _rectCanvas.Root;
        Console.WriteLine($"XamlDoom canvas ready: {_rectCanvas.PixelCount} rectangles");

        // Count real presented frames for an accurate FPS measurement.
        if (_perfEnabled)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnCompositionRendering;
            _perfWindowStart = System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    private void OnCompositionRendering(object? sender, object e) => _renderFrames++;

    private void LogPerfIfDue()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double sec = (now - _perfWindowStart) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (sec < 1.0)
            return;

        double renderFps = _renderFrames / sec;
        double tickFps = _tickFrames / sec;
        double avgCpu = _tickFrames > 0 ? _frameCpuMsSum / _tickFrames : 0;
        double avgChanged = _tickFrames > 0 ? (double)_changedPxSum / _tickFrames : 0;
        Console.WriteLine(
            $"[perf] px={_rectCanvas!.PixelCount} renderFPS={renderFps:F1} tickFPS={tickFps:F1} " +
            $"frameCpu={avgCpu:F2}ms churn={avgChanged:F0}px ({100.0 * avgChanged / _rectCanvas.PixelCount:F0}%)");

        _perfWindowStart = now;
        _renderFrames = 0;
        _tickFrames = 0;
        _frameCpuMsSum = 0;
        _changedPxSum = 0;
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
        _gameTimer.Tick += GameTimer_Tick;
        _gameTimer.Start();
        LoadingText.Visibility = Visibility.Collapsed;
    }

    private void StopGameLoop()
    {
        if (_gameTimer != null)
        {
            _gameTimer.Stop();
            _gameTimer.Tick -= GameTimer_Tick;
            _gameTimer = null;
        }

        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnCompositionRendering;
    }

    private void GameTimer_Tick(object? sender, object e)
    {
        if (!_initialized || _doom == null || _video == null || _rectCanvas == null)
            return;

        frameCount++;

        // Update touch overlay frame count
        TouchOverlay.SetFrameCount(frameCount);

        // Update menu state for touch overlay
        _input?.SetMenuState(_doom.Menu.Active);

        var frameFrac = Fixed.FromInt(1);

        // Update game every N frames based on fpsScale
        if (frameCount % fpsScale == 0)
        {
            _input?.Update(_doom, new EventTimestamp(frameCount));

            if (_doom.Update() == UpdateResult.Completed)
            {
                // Game completed - return to WAD selection
                RequestExit();
                return;
            }

            frameFrac /= 2;
        }

        // Render the game into the rectangle grid.
        long t0 = _perfEnabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        var palette = _video.RenderScreen(_doom, frameFrac);
        int changed = _rectCanvas.UpdateFrame(_video.ScreenData, palette);

        if (_perfEnabled)
        {
            double cpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _tickFrames++;
            _frameCpuMsSum += cpuMs;
            _changedPxSum += changed;
            LogPerfIfDue();
        }
    }

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
