using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mme.Data;

namespace Mme.App;

/// <summary>
/// The picMap renderer: draws the MapBuilderService grid model with the
/// original geometry (cell blocks, half-cell exit stubs into the gaps,
/// squares/circles/open-circles/stars over the block) and the QBColor
/// palette. Tooltips are hit-tested per cell like the original's tooltip
/// regions; left-click travels to the clicked room. A soft outer glow is
/// drawn behind marked room blocks (lair/NPC/command/shop/spell colors)
/// so the room type reads at a glance.
/// </summary>
public sealed class MapCanvas : FrameworkElement
{
    public const double Cell = 22;   // room block size
    public const double Gap = 8;     // corridor gap between blocks
    public const double Pitch = Cell + Gap;

    private MapBuilderService.MapGrid? _grid;
    private int _tooltipCell;
    private readonly ToolTip _tip = new() { Placement =
        System.Windows.Controls.Primitives.PlacementMode.Mouse };

    public event Action<int>? CellClicked;

    public MapCanvas()
    {
        Width = MapBuilderService.RowLength * Pitch + Gap;
        Height = 23 * Pitch + Gap;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 60000);
        ToolTip = _tip;
        _tip.Content = "";
        _tip.Visibility = Visibility.Collapsed;
    }

    public void SetGrid(MapBuilderService.MapGrid? grid)
    {
        _grid = grid;
        InvalidateVisual();
    }

    /// <summary>QBColor palette (QBColor 0..15).</summary>
    public static Color QbColor(int i) => i switch
    {
        0 => Color.FromRgb(0, 0, 0),
        1 => Color.FromRgb(0, 0, 128),
        2 => Color.FromRgb(0, 128, 0),
        3 => Color.FromRgb(0, 128, 128),
        4 => Color.FromRgb(128, 0, 0),
        5 => Color.FromRgb(128, 0, 128),
        6 => Color.FromRgb(128, 128, 0),
        7 => Color.FromRgb(192, 192, 192),
        8 => Color.FromRgb(128, 128, 128),
        9 => Color.FromRgb(0, 0, 255),
        10 => Color.FromRgb(0, 255, 0),
        11 => Color.FromRgb(0, 255, 255),
        12 => Color.FromRgb(255, 0, 0),
        13 => Color.FromRgb(255, 0, 255),
        14 => Color.FromRgb(255, 255, 0),
        _ => Color.FromRgb(255, 255, 255),
    };

    private static Color BackColor(MapBuilderService.CellBack b) => b switch
    {
        MapBuilderService.CellBack.NoUpDown => Color.FromRgb(192, 192, 192),
        MapBuilderService.CellBack.UpOnly => Color.FromRgb(0, 255, 0),
        MapBuilderService.CellBack.DownOnly => Color.FromRgb(255, 255, 0),
        MapBuilderService.CellBack.UpAndDown => Color.FromRgb(0, 255, 255),
        MapBuilderService.CellBack.Pending => Color.FromRgb(0, 0, 0),
        _ => Colors.Transparent,
    };

    private static (double X, double Y) CellOrigin(int cell)
    {
        int idx = cell - 1;
        int col = idx % MapBuilderService.RowLength;
        int row = idx / MapBuilderService.RowLength;
        return (Gap + col * Pitch, Gap + row * Pitch);
    }

    public int HitTestCell(Point p)
    {
        int col = (int)((p.X - Gap) / Pitch);
        int row = (int)((p.Y - Gap) / Pitch);
        if (col < 0 || col >= MapBuilderService.RowLength
            || row < 0 || row >= 23) return 0;
        double lx = p.X - (Gap + col * Pitch);
        double ly = p.Y - (Gap + row * Pitch);
        if (lx > Cell || ly > Cell) return 0; // in the gap
        return row * MapBuilderService.RowLength + col + 1;
    }

    protected override void OnRender(DrawingContext dc)
    {
        // OG map background is BLACK (user report; the room blocks and
        // ANSI cell colors were designed against it)
        dc.DrawRectangle(Brushes.Black, null,
            new Rect(0, 0, Width, Height));
        if (_grid is null) return;

        for (int cell = 1; cell <= MapBuilderService.SeCorner; cell++)
        {
            var c = _grid.Cells[cell];
            var (x, y) = CellOrigin(cell);
            double cx = x + Cell / 2, cy = y + Cell / 2;

            // exit stubs first: half-cell lines from center into the gap
            foreach (var g in c.Glyphs)
            {
                var pen = new Pen(new SolidColorBrush(QbColor(g.QbColor)),
                    g.Size);
                pen.Freeze();
                (double dx, double dy)? dir = g.Kind switch
                {
                    MapBuilderService.Glyph.LineN => (0d, -1d),
                    MapBuilderService.Glyph.LineS => (0d, 1d),
                    MapBuilderService.Glyph.LineE => (1d, 0d),
                    MapBuilderService.Glyph.LineW => (-1d, 0d),
                    MapBuilderService.Glyph.LineNE => (1d, -1d),
                    MapBuilderService.Glyph.LineNW => (-1d, -1d),
                    MapBuilderService.Glyph.LineSE => (1d, 1d),
                    MapBuilderService.Glyph.LineSW => (-1d, 1d),
                    _ => null,
                };
                if (dir is null) continue;
                double len = Cell / 2 + Gap / 2 + 1;
                dc.DrawLine(pen, new Point(cx, cy),
                    new Point(cx + dir.Value.dx * len,
                              cy + dir.Value.dy * len));
            }

            // room-type outer glow behind marked blocks
            Color? glow = null;
            foreach (var g in c.Glyphs)
            {
                if (g.Kind is MapBuilderService.Glyph.Circle
                    or MapBuilderService.Glyph.OpenCircle
                    or MapBuilderService.Glyph.Star
                    or MapBuilderService.Glyph.Square)
                { glow = QbColor(g.QbColor); break; }
            }
            if (c.Back != MapBuilderService.CellBack.Empty
                && glow is not null)
            {
                var gb = new RadialGradientBrush(
                    Color.FromArgb(150, glow.Value.R, glow.Value.G,
                        glow.Value.B),
                    Color.FromArgb(0, glow.Value.R, glow.Value.G,
                        glow.Value.B));
                gb.Freeze();
                dc.DrawEllipse(gb, null, new Point(cx, cy),
                    Cell * 0.95, Cell * 0.95);
            }

            // the room block
            if (c.Back != MapBuilderService.CellBack.Empty)
            {
                var brush = new SolidColorBrush(BackColor(c.Back));
                brush.Freeze();
                dc.DrawRectangle(brush,
                    new Pen(Brushes.DimGray, 0.5),
                    new Rect(x, y, Cell, Cell));
            }

            // block glyphs on top
            foreach (var g in c.Glyphs)
            {
                var col = new SolidColorBrush(QbColor(g.QbColor));
                col.Freeze();
                switch (g.Kind)
                {
                    case MapBuilderService.Glyph.Square:
                        double inset = g.Size >= 8 ? 0 : Cell / 4;
                        dc.DrawRectangle(g.Size >= 8 ? col : null,
                            new Pen(col, 2),
                            new Rect(x + inset, y + inset,
                                Cell - 2 * inset, Cell - 2 * inset));
                        break;
                    case MapBuilderService.Glyph.Circle:
                        dc.DrawEllipse(null, new Pen(col, 2),
                            new Point(cx, cy), Cell * 0.55, Cell * 0.55);
                        break;
                    case MapBuilderService.Glyph.OpenCircle:
                        dc.DrawEllipse(null, new Pen(col, 1.5),
                            new Point(cx, cy), Cell * 0.35, Cell * 0.35);
                        break;
                    case MapBuilderService.Glyph.Star:
                        DrawStar(dc, col, cx, cy, Cell * 0.62);
                        break;
                }
            }
        }
    }

    /// <summary>The five-stroke star from MapDrawOnRoom Case 1.</summary>
    private static void DrawStar(DrawingContext dc, Brush b,
        double cx, double cy, double r)
    {
        var pen = new Pen(b, 1.6);
        pen.Freeze();
        Point P(double angleDeg)
        {
            double a = (angleDeg - 90) * Math.PI / 180;
            return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }
        // classic 5-point star strokes
        Point p0 = P(0), p1 = P(144), p2 = P(288), p3 = P(72), p4 = P(216);
        dc.DrawLine(pen, p0, p1);
        dc.DrawLine(pen, p1, p2);
        dc.DrawLine(pen, p2, p3);
        dc.DrawLine(pen, p3, p4);
        dc.DrawLine(pen, p4, p0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int cell = HitTestCell(e.GetPosition(this));
        if (cell == _tooltipCell) return;
        _tooltipCell = cell;
        string tip = cell > 0 ? _grid?.Cells[cell].ToolTip ?? "" : "";
        if (tip.Length == 0)
        {
            _tip.IsOpen = false;
            _tip.Visibility = Visibility.Collapsed;
        }
        else
        {
            _tip.Content = tip;
            _tip.Visibility = Visibility.Visible;
            _tip.IsOpen = false;
            _tip.IsOpen = true;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _tooltipCell = 0;
        _tip.IsOpen = false;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        int cell = HitTestCell(e.GetPosition(this));
        if (cell > 0) CellClicked?.Invoke(cell);
    }
}
