using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace UnoDoom.Game;

/// <summary>
/// XamlDoom's renderer: a <see cref="Canvas"/> holding one <see cref="Rectangle"/> per
/// pixel. The framebuffer is painted each frame by swapping each rectangle's Fill brush.
/// Wrap <see cref="Root"/> in a Viewbox to scale it up to the window.
/// </summary>
internal sealed class DoomRectangleCanvas
{
    private readonly int _gridWidth;
    private readonly int _gridHeight;

    private readonly Rectangle[] _rects;
    private readonly int[] _srcOffset;
    private readonly int[] _lastIndex;

    // One shared brush per palette color. Mutating a brush's Color repaints every
    // rectangle that references it for free (used for damage/bonus palette flashes).
    private readonly SolidColorBrush[] _brushes = new SolidColorBrush[256];
    private uint[]? _lastPalette;

    /// <summary>The Canvas element to place into the visual tree (inside a Viewbox).</summary>
    public Canvas Root { get; }

    /// <summary>Number of rectangles ("pixels") in the grid.</summary>
    public int PixelCount => _rects.Length;

    public DoomRectangleCanvas(int screenWidth, int screenHeight, int downsample)
    {
        downsample = Math.Max(1, downsample);
        _gridWidth = screenWidth / downsample;
        _gridHeight = screenHeight / downsample;

        Root = new Canvas
        {
            Width = _gridWidth,
            Height = _gridHeight,
            // Let rects land on exact (fractional) device pixels under the Viewbox scale
            // so adjacent quads abut instead of leaving rounding seams.
            UseLayoutRounding = false,
        };

        for (int i = 0; i < _brushes.Length; i++)
        {
            _brushes[i] = new SolidColorBrush();
        }

        int count = _gridWidth * _gridHeight;
        _rects = new Rectangle[count];
        _srcOffset = new int[count];
        _lastIndex = new int[count];

        var children = Root.Children;
        for (int gy = 0; gy < _gridHeight; gy++)
        {
            int sy = gy * downsample;
            for (int gx = 0; gx < _gridWidth; gx++)
            {
                int cell = gy * _gridWidth + gx;
                int sx = gx * downsample;

                var rect = new Rectangle
                {
                    Width = 1,
                    Height = 1,
                };
                Canvas.SetLeft(rect, gx);
                Canvas.SetTop(rect, gy);

                _rects[cell] = rect;
                // Doom's framebuffer is column-major: data[x * height + y].
                _srcOffset[cell] = sx * screenHeight + sy;
                _lastIndex[cell] = -1; // force a Fill assignment on the first frame
                children.Add(rect);
            }
        }
    }

    /// <summary>
    /// Repaints the grid from a Doom framebuffer. Only rectangles whose palette index
    /// changed since the last frame have their Fill reassigned. Returns the number of
    /// rectangles whose Fill was reassigned this frame (pixel churn).
    /// </summary>
    public int UpdateFrame(byte[] data, uint[] palette)
    {
        // Palette swapped (flash/gamma): recolor the shared brushes in place, which
        // repaints all referencing rectangles without touching their Fill.
        if (!ReferenceEquals(palette, _lastPalette))
        {
            for (int i = 0; i < _brushes.Length; i++)
            {
                uint c = palette[i];
                _brushes[i].Color = new Windows.UI.Color
                {
                    A = 255,
                    R = (byte)(c & 0xFF),
                    G = (byte)((c >> 8) & 0xFF),
                    B = (byte)((c >> 16) & 0xFF),
                };
            }
            _lastPalette = palette;
        }

        var rects = _rects;
        var srcOffset = _srcOffset;
        var lastIndex = _lastIndex;
        var brushes = _brushes;

        int changed = 0;
        for (int cell = 0; cell < rects.Length; cell++)
        {
            int idx = data[srcOffset[cell]];
            if (idx != lastIndex[cell])
            {
                rects[cell].Fill = brushes[idx];
                lastIndex[cell] = idx;
                changed++;
            }
        }

        return changed;
    }
}
