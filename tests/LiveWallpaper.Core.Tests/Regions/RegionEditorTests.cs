using System.Drawing;
using LiveWallpaper.Core.Regions;

namespace LiveWallpaper.Core.Tests.Regions;

public sealed class RegionEditorTests
{
    private static readonly RegionView FullView = new(new RectangleF(0, 0, 1000, 1000), new Size(1000, 1000), 1);

    // A 2000x1000 image shown with Center scaling on a 1000x1000 view: only x 0.25–0.75 is visible.
    private static readonly RegionView CroppedView = new(new RectangleF(-500, 0, 2000, 1000), new Size(1000, 1000), 1);

    private static RegionEditor EditorWithQuad(RegionView? view = null)
    {
        var editor = new RegionEditor(RegionSet.Empty);
        editor.SetView(view ?? FullView);
        editor.AddRegion();
        return editor;
    }

    private static void Drag(RegionEditor editor, RegionPoint from, params RegionPoint[] path)
    {
        editor.PointerDown(from);
        foreach (var point in path)
        {
            editor.PointerMove(point);
        }
        editor.PointerUp();
    }

    private static int Count(RegionEditor editor, OverlayShapeKind kind) => editor.Overlay().Count(s => s.Kind == kind);

    private static void AssertPoint(RegionPoint expected, RegionPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
    }

    [Fact]
    public void AddRegion_SelectsNewQuadAndCommits()
    {
        var editor = EditorWithQuad();

        Assert.Single(editor.State.Regions);
        Assert.Equal(0, editor.SelectedRegion);
        Assert.True(editor.CanUndo);
        editor.Undo();
        Assert.Empty(editor.State.Regions);
    }

    [Fact]
    public void AddRegion_AtLimit_DoesNothing()
    {
        var editor = new RegionEditor(RegionSet.Empty);
        for (var i = 0; i < RegionSet.MaxRegions; i++)
        {
            editor.AddRegion();
        }

        Assert.False(editor.CanAddRegion);
        editor.AddRegion();
        Assert.Equal(RegionSet.MaxRegions, editor.State.Regions.Count);
    }

    [Fact]
    public void AddRegion_UsesVisiblePartOfImage()
    {
        var editor = EditorWithQuad(CroppedView);

        AssertPoint(new RegionPoint(0.425, 0.35), editor.State.Regions[0].Points[0]);
    }

    [Fact]
    public void DragCorner_MovesOnlyThatCorner()
    {
        var editor = EditorWithQuad();

        Drag(editor, new(350, 350), new RegionPoint(300, 300));

        AssertPoint(new RegionPoint(0.3, 0.3), editor.State.Regions[0].Points[0]);
        AssertPoint(new RegionPoint(0.65, 0.35), editor.State.Regions[0].Points[1]);
        editor.Undo();
        AssertPoint(new RegionPoint(0.35, 0.35), editor.State.Regions[0].Points[0]);
    }

    [Fact]
    public void DragBody_TranslatesRegion()
    {
        var editor = EditorWithQuad();

        Drag(editor, new(500, 500), new RegionPoint(600, 500));

        AssertPoint(new RegionPoint(0.45, 0.35), editor.State.Regions[0].Points[0]);
    }

    [Fact]
    public void DragBody_StopsAtVisibleEdge()
    {
        var editor = EditorWithQuad(CroppedView);

        Drag(editor, new(500, 500), new RegionPoint(2000, 500));

        Assert.Equal(0.75, editor.State.Regions[0].Points.Max(p => p.X), 9);
    }

    [Fact]
    public void DragCorner_OutsideViewport_ClampsToVisiblePart()
    {
        var editor = EditorWithQuad(CroppedView);

        Drag(editor, new(350, 350), new RegionPoint(-400, 350));

        Assert.Equal(0.25, editor.State.Regions[0].Points[0].X, 9);
    }

    [Fact]
    public void ClickEdge_InsertsPointAndDragsIt()
    {
        var editor = EditorWithQuad();

        editor.PointerMove(new(500, 352));
        Assert.Equal(1, Count(editor, OverlayShapeKind.Ghost));
        editor.PointerDown(new(500, 352));
        Assert.Equal(5, editor.State.Regions[0].Points.Count);
        Assert.Equal(1, editor.SelectedVertex);
        editor.PointerMove(new(500, 300));
        editor.PointerUp();

        AssertPoint(new RegionPoint(0.5, 0.3), editor.State.Regions[0].Points[1]);
        editor.Undo();
        Assert.Equal(4, editor.State.Regions[0].Points.Count);
    }

    [Fact]
    public void RemoveVertexAt_Handle_RemovesIt()
    {
        var editor = EditorWithQuad();

        editor.RemoveVertexAt(new(350, 350));

        Assert.Equal(3, editor.State.Regions[0].Points.Count);
        AssertPoint(new RegionPoint(0.65, 0.35), editor.State.Regions[0].Points[0]);
    }

    [Fact]
    public void DeleteSelectedVertex_KeepsThreePoints()
    {
        var editor = EditorWithQuad();
        Drag(editor, new(350, 350));

        Assert.True(editor.DeleteSelectedVertex());
        Assert.Equal(3, editor.State.Regions[0].Points.Count);
        Drag(editor, new(650, 350));
        Assert.False(editor.DeleteSelectedVertex());
        Assert.Equal(3, editor.State.Regions[0].Points.Count);
    }

    [Fact]
    public void ClickOutside_ClearsSelection()
    {
        var editor = EditorWithQuad();

        Drag(editor, new(100, 100));

        Assert.Null(editor.SelectedRegion);
    }

    [Fact]
    public void ClickWithoutMoving_AddsNoHistory()
    {
        var editor = EditorWithQuad();

        Drag(editor, new(500, 500));
        editor.Undo();

        Assert.Empty(editor.State.Regions);
    }

    [Fact]
    public void Undo_DuringDrag_CancelsDragAndKeepsHistory()
    {
        var editor = EditorWithQuad();
        var afterAdd = editor.State;

        editor.PointerDown(new(350, 350));
        editor.PointerMove(new(300, 300));
        editor.Undo();
        Assert.Equal(afterAdd, editor.State);
        Assert.Null(editor.SelectedRegion);
        editor.PointerMove(new(200, 200));
        editor.PointerUp();

        Assert.Equal(afterAdd, editor.State);
        editor.Undo();
        Assert.Empty(editor.State.Regions);
    }

    [Fact]
    public void Undo_WithUncommittedFeather_RevertsOnlyThatChange()
    {
        var editor = EditorWithQuad();

        editor.SetFeather(5, commit: false);
        Assert.True(editor.CanUndo);
        editor.Undo();

        Assert.Equal(RegionSet.DefaultFeatherPct, editor.State.FeatherPct);
        Assert.Single(editor.State.Regions);
    }

    [Fact]
    public void SetFeather_ClampsToRange()
    {
        var editor = EditorWithQuad();

        editor.SetFeather(25, commit: true);

        Assert.Equal(RegionSet.MaxFeatherPct, editor.State.FeatherPct);
    }

    [Fact]
    public void UndoRedo_DropsSelection()
    {
        var editor = EditorWithQuad();
        editor.SetInvert(true);
        Drag(editor, new(350, 350));
        Assert.Equal(0, editor.SelectedVertex);

        editor.Undo();
        Assert.Null(editor.SelectedRegion);
        Assert.Null(editor.SelectedVertex);
        Drag(editor, new(350, 350));
        editor.Redo();
        Assert.Null(editor.SelectedRegion);
        Assert.Null(editor.SelectedVertex);
        Assert.True(editor.State.Invert);
    }

    [Fact]
    public void Disabled_IgnoresPointerAndDeleteAndHasNoOverlay()
    {
        var editor = EditorWithQuad();
        var before = editor.State;

        editor.SetEnabled(false);
        Drag(editor, new(350, 350), new RegionPoint(100, 100));

        Assert.Equal(before, editor.State);
        Assert.Null(editor.SelectedRegion);
        Assert.False(editor.DeleteSelectedVertex());
        Assert.Empty(editor.Overlay());
    }

    [Fact]
    public void OneHistoryEntryPerDrag()
    {
        var editor = EditorWithQuad();
        var afterAdd = editor.State;

        Drag(editor, new(350, 350), Enumerable.Range(1, 10).Select(i => new RegionPoint(350 - (i * 5), 350)).ToArray());
        editor.Undo();

        Assert.Equal(afterAdd, editor.State);
    }

    [Fact]
    public void Overlay_ShowsEdgesHandlesAndGhost()
    {
        var editor = EditorWithQuad();
        Drag(editor, new(350, 350));

        Assert.Equal(4, Count(editor, OverlayShapeKind.SelectedEdge));
        Assert.Equal(3, Count(editor, OverlayShapeKind.Handle));
        Assert.Equal(1, Count(editor, OverlayShapeKind.SelectedHandle));
        Assert.Equal(0, Count(editor, OverlayShapeKind.Ghost));
        editor.PointerMove(new(500, 352));
        Assert.Equal(1, Count(editor, OverlayShapeKind.Ghost));
    }

    [Fact]
    public void Changes_RaiseChanged()
    {
        var editor = new RegionEditor(RegionSet.Empty);
        var raised = 0;
        editor.Changed += (_, _) => raised++;

        editor.AddRegion();

        Assert.True(raised > 0);
    }
}
