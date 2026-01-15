namespace UnoDoom;

/// <summary>
/// Game page that hosts the DOOM game control.
/// </summary>
public sealed partial class GamePage : Page
{
    public GamePage()
    {
        this.InitializeComponent();

        // Focus the game control for keyboard input
        this.Loaded += (s, e) =>
        {
            DoomGame.Focus(FocusState.Programmatic);
        };

        // Handle exit request from game control
        DoomGame.ExitRequested += DoomGame_ExitRequested;
    }

    private void DoomGame_ExitRequested(object? sender, EventArgs e)
    {
        // Navigate back to PreGamePage
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(PreGamePage));
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Pass the WAD path to the game control
        if (e.Parameter is string wadPath)
        {
            DoomGame.WadPath = wadPath;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // Unsubscribe from events
        DoomGame.ExitRequested -= DoomGame_ExitRequested;
    }
}
