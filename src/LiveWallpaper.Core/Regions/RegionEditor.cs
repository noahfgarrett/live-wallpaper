using LiveWallpaper.Core.Models;
using LiveWallpaper.Core.Water;

namespace LiveWallpaper.Core.Regions;

public enum OverlayShapeKind
{
    Edge,
    SelectedEdge,
    Handle,
    SelectedHandle,
    Ghost,
    Arrow,
    ArrowHandle,
}

/// <summary>
/// Free: polygons with corners added and removed (snow, rain). Plane: fixed 4-corner panes (glass).
/// Flow: polygons with a flow arrow each (water).
/// </summary>
public enum RegionEditMode
{
    Free,
    Plane,
    Flow,
}

/// <summary>
/// One shape to draw over the preview, in image coordinates. Circles (handles, the ghost) have <c>A == B</c>;
/// an arrow runs from its tail <c>A</c> to its head <c>B</c>.
/// </summary>
public readonly record struct OverlayShape(OverlayShapeKind Kind, RegionPoint A, RegionPoint B);

/// <summary>
/// The region editing rules from the browser preview (prototype/regionEditor.js), with its known bugs fixed:
/// undo and redo drop the selection and cancel a drag in progress, drags stay on the visible part of the image,
/// and nothing can be edited while the outlines are hidden. Pointer positions are in view pixels.
/// </summary>
public sealed class RegionEditor
{
    public const double HandleRadiusDip = 6;
    public const double HandleHitDip = 9;
    public const double EdgeReachDip = 6;
    public const double VertexClearanceDip = 12;

    private readonly History<RegionSet> _history;
    private readonly double _imageAspect;
    private RegionSet _working;
    private RegionView? _view;
    private Drag? _drag;
    private bool _dragChanged;
    private Ghost? _ghost;
    private bool _enabled = true;

    /// <summary><paramref name="imageAspect"/> is the source image's width over its height, for clamping flow arrows.</summary>
    public RegionEditor(RegionSet initial, RegionEditMode mode = RegionEditMode.Free, double imageAspect = 1)
    {
        if (!double.IsFinite(imageAspect) || imageAspect <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(imageAspect), imageAspect, "The image aspect must be a positive number.");
        }
        Mode = mode;
        _imageAspect = imageAspect;
        _history = new History<RegionSet>(initial);
        _working = initial;
    }

    /// <summary>Raised after the regions, the selection, the overlay or the undo state change.</summary>
    public event EventHandler? Changed;

    /// <summary>The regions as they look now, including a drag or soft-edge change that isn't committed yet.</summary>
    public RegionSet State => _working;

    public RegionEditMode Mode { get; }

    public int? SelectedRegion { get; private set; }

    public int? SelectedVertex { get; private set; }

    public bool IsEnabled => _enabled;

    public bool CanUndo => _history.CanUndo || IsDirty;

    public bool CanRedo => _history.CanRedo && !IsDirty;

    public bool CanAddRegion => _working.Regions.Count < RegionSet.MaxRegions;

    private bool IsDirty => !_working.Equals(_history.Current);

    private NormalizedRect Visible => _view?.VisibleBounds ?? NormalizedRect.Unit;

    /// <summary>Sets how the image is shown. Raises <see cref="Changed"/> so the overlay is drawn for the new view.</summary>
    public void SetView(RegionView? view)
    {
        _view = view;
        _ghost = null;
        Raise();
    }

    /// <summary>Turns editing on or off (off while the outlines are hidden). Turning it off cancels a drag and drops the selection.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            if (_drag is not null)
            {
                _working = _history.Current;
            }
            ClearInteraction();
        }
        Raise();
    }

    public void PointerDown(RegionPoint screen)
    {
        if (!_enabled || _view is null || _drag is not null)
        {
            return;
        }
        if (SelectedRegion is { } selected)
        {
            if (ArrowHandleAt(selected, screen))
            {
                SelectedVertex = null;
                _drag = new Drag.Arrow(selected);
                _ghost = null;
                Raise();
                return;
            }
            if (HandleAt(selected, screen) is { } vertex)
            {
                SelectedVertex = vertex;
                _drag = new Drag.Vertex(selected, vertex);
                Raise();
                return;
            }
            if (GhostAt(screen) is { } ghost)
            {
                _working = MapRegion(selected, r => RegionGeometry.InsertVertex(r, ghost.EdgeIndex, ghost.Point));
                SelectedVertex = ghost.EdgeIndex + 1;
                _drag = new Drag.Vertex(selected, ghost.EdgeIndex + 1);
                _dragChanged = true;
                _ghost = null;
                Raise();
                return;
            }
        }

        SelectedVertex = null;
        SelectedRegion = RegionAt(screen);
        _drag = SelectedRegion is { } hit ? new Drag.Body(hit, _view.ToImage(screen), _working.Regions[hit]) : null;
        _ghost = null;
        Raise();
    }

    public void PointerMove(RegionPoint screen)
    {
        if (!_enabled || _view is null)
        {
            return;
        }
        var view = _view;
        switch (_drag)
        {
            case Drag.Vertex vertex:
                var target = view.ToImage(view.ClampToVisible(screen));
                _working = MapRegion(vertex.Region, r => RegionGeometry.MoveVertex(r, vertex.Index, target, view.VisibleBounds));
                _dragChanged = true;
                Raise();
                return;
            case Drag.Body body:
                var now = view.ToImage(screen);
                _working = MapRegion(body.Region, _ => RegionGeometry.Translate(body.Origin, now.X - body.Start.X, now.Y - body.Start.Y, view.VisibleBounds));
                _dragChanged = true;
                Raise();
                return;
            case Drag.Arrow arrow:
                var head = view.VisibleBounds.Clamp(view.ToImage(screen));
                _working = MapRegion(arrow.Region, r =>
                {
                    var centroid = Centroid(r);
                    return r with { Flow = WaterModel.ClampArrow(new RegionPoint(head.X - centroid.X, head.Y - centroid.Y), _imageAspect) };
                });
                _dragChanged = true;
                Raise();
                return;
            case null:
                var ghost = GhostAt(screen);
                if (ghost != _ghost)
                {
                    _ghost = ghost;
                    Raise();
                }
                return;
        }
    }

    public void PointerUp()
    {
        if (_drag is null)
        {
            return;
        }
        if (_dragChanged)
        {
            Commit();
        }
        _drag = null;
        _dragChanged = false;
        Raise();
    }

    /// <summary>Removes the selected region's vertex under the pointer (right-click).</summary>
    public void RemoveVertexAt(RegionPoint screen)
    {
        if (!_enabled || _view is null || _drag is not null || Mode == RegionEditMode.Plane
            || SelectedRegion is not { } selected || HandleAt(selected, screen) is not { } vertex)
        {
            return;
        }
        _working = MapRegion(selected, r => RegionGeometry.RemoveVertex(r, vertex));
        SelectedVertex = null;
        Commit();
        Raise();
    }

    /// <summary>Removes the selected vertex (Delete key). Returns true when one was removed.</summary>
    public bool DeleteSelectedVertex()
    {
        if (!_enabled || _drag is not null || Mode == RegionEditMode.Plane || SelectedRegion is not { } selected || SelectedVertex is not { } vertex
            || _working.Regions[selected].Points.Count <= RegionGeometry.MinPoints)
        {
            return false;
        }
        _working = MapRegion(selected, r => RegionGeometry.RemoveVertex(r, vertex));
        SelectedVertex = null;
        Commit();
        Raise();
        return true;
    }

    public void AddRegion()
    {
        if (!CanAddRegion)
        {
            return;
        }
        _drag = null;
        _dragChanged = false;
        var quad = RegionGeometry.CreateQuad(Visible);
        if (Mode == RegionEditMode.Flow)
        {
            quad = quad with { Flow = EffectRules.DefaultFlow };
        }
        _working = _working with { Regions = [.. _working.Regions, quad] };
        SelectedRegion = _enabled ? _working.Regions.Count - 1 : null;
        SelectedVertex = null;
        _ghost = null;
        Commit();
        Raise();
    }

    public void DeleteSelectedRegion()
    {
        if (SelectedRegion is not { } selected)
        {
            return;
        }
        _working = _working with { Regions = _working.Regions.Where((_, i) => i != selected).ToArray() };
        ClearInteraction();
        Commit();
        Raise();
    }

    /// <summary>Only snow and rain (<see cref="RegionEditMode.Free"/>) can be inverted; other modes ignore it.</summary>
    public void SetInvert(bool invert)
    {
        if (Mode != RegionEditMode.Free)
        {
            return;
        }
        _working = _working with { Invert = invert };
        Commit();
        Raise();
    }

    public void SetFeather(double pct, bool commit)
    {
        _working = _working with { FeatherPct = Math.Clamp(pct, 0, RegionSet.MaxFeatherPct) };
        if (commit)
        {
            Commit();
        }
        Raise();
    }

    /// <summary>Cancels an uncommitted change (a drag or a soft-edge change) if there is one, otherwise steps back.</summary>
    public void Undo()
    {
        var wasDirty = IsDirty;
        ClearInteraction();
        _working = wasDirty ? _history.Current : _history.Undo();
        Raise();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }
        ClearInteraction();
        _working = _history.Redo();
        Raise();
    }

    public IReadOnlyList<OverlayShape> Overlay()
    {
        if (!_enabled)
        {
            return [];
        }
        var shapes = new List<OverlayShape>();
        for (var i = 0; i < _working.Regions.Count; i++)
        {
            if (i != SelectedRegion)
            {
                AddEdges(shapes, _working.Regions[i], OverlayShapeKind.Edge);
            }
        }
        if (SelectedRegion is { } selected)
        {
            var region = _working.Regions[selected];
            AddEdges(shapes, region, OverlayShapeKind.SelectedEdge);
            for (var v = 0; v < region.Points.Count; v++)
            {
                var kind = v == SelectedVertex ? OverlayShapeKind.SelectedHandle : OverlayShapeKind.Handle;
                shapes.Add(new OverlayShape(kind, region.Points[v], region.Points[v]));
            }
            if (_ghost is { } ghost && _drag is null)
            {
                shapes.Add(new OverlayShape(OverlayShapeKind.Ghost, ghost.Point, ghost.Point));
            }
        }
        if (Mode == RegionEditMode.Flow)
        {
            foreach (var region in _working.Regions)
            {
                shapes.Add(new OverlayShape(OverlayShapeKind.Arrow, Centroid(region), ArrowHead(region)));
            }
            if (SelectedRegion is { } withHandle)
            {
                var head = ArrowHead(_working.Regions[withHandle]);
                shapes.Add(new OverlayShape(OverlayShapeKind.ArrowHandle, head, head));
            }
        }
        return shapes;
    }

    private static void AddEdges(List<OverlayShape> shapes, Region region, OverlayShapeKind kind)
    {
        for (var i = 0; i < region.Points.Count; i++)
        {
            shapes.Add(new OverlayShape(kind, region.Points[i], region.Points[(i + 1) % region.Points.Count]));
        }
    }

    /// <summary>The average of the corners: where a region's flow arrow starts (as in the tuning spike).</summary>
    private static RegionPoint Centroid(Region region) =>
        new(region.Points.Average(p => p.X), region.Points.Average(p => p.Y));

    private static RegionPoint ArrowHead(Region region)
    {
        var centroid = Centroid(region);
        var flow = region.Flow ?? EffectRules.DefaultFlow;
        return new RegionPoint(centroid.X + flow.X, centroid.Y + flow.Y);
    }

    private void Commit()
    {
        if (IsDirty)
        {
            _history.Push(_working);
        }
    }

    private void ClearInteraction()
    {
        _drag = null;
        _dragChanged = false;
        _ghost = null;
        SelectedRegion = null;
        SelectedVertex = null;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);

    private RegionSet MapRegion(int index, Func<Region, Region> change) =>
        _working with { Regions = _working.Regions.Select((r, i) => i == index ? change(r) : r).ToArray() };

    private RegionPoint[] ScreenPoints(Region region, RegionView view) => region.Points.Select(view.ToScreen).ToArray();

    private int? HandleAt(int regionIndex, RegionPoint screen)
    {
        if (_view is not { } view)
        {
            return null;
        }
        var points = ScreenPoints(_working.Regions[regionIndex], view);
        int? best = null;
        var bestDistance = HandleHitDip * view.Scale;
        for (var i = 0; i < points.Length; i++)
        {
            var distance = RegionGeometry.Distance(points[i], screen);
            if (distance <= bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    private bool ArrowHandleAt(int regionIndex, RegionPoint screen) =>
        Mode == RegionEditMode.Flow && _view is { } view
        && RegionGeometry.Distance(view.ToScreen(ArrowHead(_working.Regions[regionIndex])), screen) <= HandleHitDip * view.Scale;

    private Ghost? GhostAt(RegionPoint screen)
    {
        if (Mode == RegionEditMode.Plane || _view is not { } view || SelectedRegion is not { } selected)
        {
            return null;
        }
        var region = _working.Regions[selected];
        if (region.Points.Count >= RegionSet.MaxPoints)
        {
            return null;
        }
        var points = ScreenPoints(region, view);
        if (points.Any(p => RegionGeometry.Distance(p, screen) < VertexClearanceDip * view.Scale))
        {
            return null;
        }
        return RegionGeometry.NearestEdge(points, screen, EdgeReachDip * view.Scale) is { } hit
            ? new Ghost(hit.EdgeIndex, view.VisibleBounds.Clamp(view.ToImage(hit.Closest)))
            : null;
    }

    private int? RegionAt(RegionPoint screen)
    {
        if (_view is not { } view)
        {
            return null;
        }
        for (var i = _working.Regions.Count - 1; i >= 0; i--)
        {
            if (RegionGeometry.ContainsPoint(ScreenPoints(_working.Regions[i], view), screen))
            {
                return i;
            }
        }
        return null;
    }

    private readonly record struct Ghost(int EdgeIndex, RegionPoint Point);

    private abstract record Drag
    {
        private Drag()
        {
        }

        public sealed record Vertex(int Region, int Index) : Drag;

        public sealed record Body(int Region, RegionPoint Start, Region Origin) : Drag;

        public sealed record Arrow(int Region) : Drag;
    }
}
