using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace BOM.Editor
{
    public sealed class BomProfileEditor : VisualElement
    {
        private readonly PieceDraft draft;
        private readonly Action beforeEdit, changed;
        private Vector2 centre;
        private float pixelsPerMetre = 100;
        public int SelectedRing { get; private set; }
        public int SelectedVertex { get; private set; } = -1;
        public bool Snap { get; set; }
        public float GridSize { get; set; } = .1f;
        public Action SelectionChanged;
        public Action DragCancelled;
        public Action InteractionFinished;
        public bool IsDragging {get; private set;}
        private Contour Ring => SelectedRing == 0 ? draft.profile.outer : draft.profile.holes[SelectedRing - 1];
        public bool HasSelection => SelectedVertex >= 0 && SelectedVertex < Ring.points.Count;
        public Vector2 SelectedPoint => HasSelection ? Ring.points[SelectedVertex] : Vector2.zero;
        public bool CanRemove => HasSelection && Ring.points.Count > 3;

        public BomProfileEditor(PieceDraft draft, Action beforeEdit, Action changed)
        {
            this.draft = draft; this.beforeEdit = beforeEdit; this.changed = changed;
            name = "profileEditor"; focusable = true; AddToClassList("bom-profile-editor");
            tooltip = "Drag a point to edit. Double-click an edge to insert a point. Delete removes the selected point. Escape cancels a drag. Scroll to zoom; middle-drag to pan.";
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            this.AddManipulator(new PointManipulator(this));
        }

        public Vector2 ProfileToCanvas(Vector2 point) => contentRect.center + new Vector2(point.x - centre.x, centre.y - point.y) * pixelsPerMetre;
        public Vector2 CanvasToProfile(Vector2 point)
        {var d = (point - contentRect.center) / pixelsPerMetre; return centre + new Vector2(d.x, -d.y);}
        public void Fit()
        {
            var points = draft.profile.Contours.Concat(draft.disconnectedProposal.SelectMany(r => r.Contours))
                .SelectMany(c => c.points).Where(Geometry.Finite).ToArray();
            if (points.Length == 0 || contentRect.width < 1 || contentRect.height < 1) return;
            var min = new Vector2(points.Min(p => p.x), points.Min(p => p.y));
            var max = new Vector2(points.Max(p => p.x), points.Max(p => p.y)); centre = (min + max) * .5f;
            pixelsPerMetre = Mathf.Max(.001f, Mathf.Min(Mathf.Max(1, contentRect.width - 60) / Mathf.Max(.01f, max.x - min.x),
                Mathf.Max(1, contentRect.height - 60) / Mathf.Max(.01f, max.y - min.y)));
            MarkDirtyRepaint();
        }
        public void SelectRing(int ring)
        {SelectedRing = Mathf.Clamp(ring, 0, draft.profile.holes.Count); SelectedVertex = -1; SelectionChanged?.Invoke(); MarkDirtyRepaint();}
        public void RefreshSelection()
        {
            int ring=Mathf.Clamp(SelectedRing,0,draft.profile.holes.Count);
            if(ring!=SelectedRing){SelectedRing=ring;SelectedVertex=-1;}
            if(SelectedVertex>=Ring.points.Count)SelectedVertex=-1;
            SelectionChanged?.Invoke();
        }
        public void PointRemoved(int ring,int index)
        {
            if(SelectedRing!=ring)return;
            if(SelectedVertex==index)SelectedVertex=-1;else if(SelectedVertex>index)SelectedVertex--;
        }
        public void SetSelectedPoint(Vector2 point)
        {
            if (!HasSelection || !Geometry.Finite(point) || point == SelectedPoint) return;
            beforeEdit(); Ring.points[SelectedVertex] = point; Edited();
        }
        public void RemoveSelected()
        {
            if (!CanRemove) return;
            beforeEdit(); Ring.points.RemoveAt(SelectedVertex); SelectedVertex = -1; Edited();
        }
        private void Edited()
        {Ring.cutEdges.Clear(); draft.import.manuallyCorrected = true; changed(); SelectionChanged?.Invoke(); MarkDirtyRepaint();}
        private Vector2 PointAt(Vector2 pixel,Vector2 offset=default)
        {
            var point = CanvasToProfile(pixel)+offset;
            if (Snap && Geometry.Positive(GridSize)) point = new Vector2(Mathf.Round(point.x / GridSize) * GridSize, Mathf.Round(point.y / GridSize) * GridSize);
            return new Vector2(Mathf.Clamp(point.x, -10000, 10000), Mathf.Clamp(point.y, -10000, 10000));
        }
        private bool Pick(Vector2 pixel)
        {
            float distance = 12 * 12; int ring = -1, vertex = -1, r = 0;
            foreach (var contour in draft.profile.Contours)
            {
                for (int i = 0; i < contour.points.Count; i++)
                {float d = (ProfileToCanvas(contour.points[i]) - pixel).sqrMagnitude; if (d < distance) {distance = d; ring = r; vertex = i;}}
                r++;
            }
            if (ring < 0) {SelectedVertex=-1;SelectionChanged?.Invoke();MarkDirtyRepaint();return false;}
            SelectedRing = ring; SelectedVertex = vertex; SelectionChanged?.Invoke(); MarkDirtyRepaint(); return true;
        }
        private bool Insert(Vector2 pixel)
        {
            float best = 12 * 12; int index = -1; Vector2 projected = default;
            for (int i = 0; i < Ring.points.Count; i++)
            {
                var a = ProfileToCanvas(Ring.points[i]); var b = ProfileToCanvas(Ring.points[(i + 1) % Ring.points.Count]);
                var d = b - a; float t = d.sqrMagnitude == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(pixel - a, d) / d.sqrMagnitude);
                var p = a + t * d; float distance = (p - pixel).sqrMagnitude;
                if (t > .001f && t < .999f && distance < best) {best = distance; index = i + 1; projected = p;}
            }
            if (index < 0) return false;
            IsDragging=true;beforeEdit(); Ring.points.Insert(index, PointAt(projected)); SelectedVertex = index; Edited(); return true;
        }
        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width < 1 || contentRect.height < 1) return;
            var p = context.painter2D;
            double step = Math.Pow(10, Math.Floor(Math.Log10(60 / pixelsPerMetre)));
            if (step * pixelsPerMetre < 30) step *= 5;
            var min = CanvasToProfile(new Vector2(contentRect.xMin, contentRect.yMax));
            var max = CanvasToProfile(new Vector2(contentRect.xMax, contentRect.yMin));
            p.lineWidth = 1; p.strokeColor = new Color(.22f, .29f, .34f); p.BeginPath();
            for (double x = Math.Ceiling(min.x / step) * step; x <= max.x; x += step)
            {p.MoveTo(ProfileToCanvas(new Vector2((float)x, min.y))); p.LineTo(ProfileToCanvas(new Vector2((float)x, max.y)));}
            for (double y = Math.Ceiling(min.y / step) * step; y <= max.y; y += step)
            {p.MoveTo(ProfileToCanvas(new Vector2(min.x, (float)y))); p.LineTo(ProfileToCanvas(new Vector2(max.x, (float)y)));}
            p.Stroke();
            void Trace(Contour contour)
            {
                if (contour.points.Count == 0 || contour.points.Any(point=>!Geometry.Finite(point))) return;
                p.MoveTo(ProfileToCanvas(contour.points[0]));
                foreach (var point in contour.points.Skip(1)) p.LineTo(ProfileToCanvas(point)); p.ClosePath();
            }
            p.fillColor = new Color(.12f, .42f, .49f, .6f); p.BeginPath();
            foreach (var contour in draft.profile.Contours) Trace(contour); p.Fill(FillRule.OddEven);
            int ring = 0;
            foreach (var contour in draft.profile.Contours)
            {
                p.lineWidth = ring == SelectedRing ? 3 : 2; p.strokeColor = ring == 0 ? Color.cyan : new Color(1, .7f, .25f);
                p.BeginPath(); Trace(contour); p.Stroke();
                for (int i = 0; i < contour.points.Count; i++)
                {
                    if(!Geometry.Finite(contour.points[i]))continue;
                    p.fillColor = ring == SelectedRing && i == SelectedVertex ? Color.white : p.strokeColor;
                    p.BeginPath(); p.Arc(ProfileToCanvas(contour.points[i]), 6, Angle.Degrees(0), Angle.Degrees(360)); p.Fill();
                }
                ring++;
            }
            p.strokeColor = new Color(1, .45f, .35f); p.lineWidth = 2;
            foreach (var region in draft.disconnectedProposal) foreach (var contour in region.Contours)
            {p.BeginPath(); Trace(contour); p.Stroke();}
        }

        private sealed class PointManipulator : PointerManipulator
        {
            private readonly BomProfileEditor editor;
            private int pointer = -1;
            private bool pan;
            private Vector2 original, lastPixel;
            private Region originalProfile;
            private bool originalCorrection;
            public PointManipulator(BomProfileEditor editor) {this.editor = editor;}
            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(Down); target.RegisterCallback<PointerMoveEvent>(Move);
                target.RegisterCallback<PointerUpEvent>(Up); target.RegisterCallback<PointerCancelEvent>(Cancel);
                target.RegisterCallback<PointerCaptureOutEvent>(Lost); target.RegisterCallback<KeyDownEvent>(Key);
                target.RegisterCallback<WheelEvent>(Wheel);
            }
            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(Down); target.UnregisterCallback<PointerMoveEvent>(Move);
                target.UnregisterCallback<PointerUpEvent>(Up); target.UnregisterCallback<PointerCancelEvent>(Cancel);
                target.UnregisterCallback<PointerCaptureOutEvent>(Lost); target.UnregisterCallback<KeyDownEvent>(Key);
                target.UnregisterCallback<WheelEvent>(Wheel);
            }
            private void Down(PointerDownEvent e)
            {
                if (pointer >= 0 || (e.button != 0 && e.button != 2)) return;
                var pixel = (Vector2)e.localPosition; pan = e.button == 2;
                originalProfile=editor.draft.profile.Clone();originalCorrection=editor.draft.import.manuallyCorrected;
                bool inserted=false;
                if(!pan&&!editor.Pick(pixel)){if(e.clickCount<2||!editor.Insert(pixel))return;inserted=true;}
                if (!pan) {if(!inserted)editor.beforeEdit(); original = editor.SelectedPoint-editor.CanvasToProfile(pixel);}
                lastPixel = pixel; pointer = e.pointerId;editor.IsDragging=true;target.AddToClassList("dragging");target.Focus(); target.CapturePointer(pointer); e.StopPropagation();
            }
            private void Move(PointerMoveEvent e)
            {
                if (e.pointerId != pointer) return;
                var pixel = (Vector2)e.localPosition;
                if (pan) {var d = (pixel - lastPixel) / editor.pixelsPerMetre; editor.centre += new Vector2(-d.x, d.y); editor.MarkDirtyRepaint();}
                else {editor.Ring.points[editor.SelectedVertex] = editor.PointAt(pixel,original); editor.Edited();}
                lastPixel = pixel; e.StopPropagation();
            }
            private void Finish(bool cancel)
            {
                if (pointer < 0) return;
                int id = pointer; pointer = -1;
                if (cancel && !pan)
                {editor.draft.profile=originalProfile;editor.draft.import.manuallyCorrected=originalCorrection;editor.SelectedVertex=-1;editor.changed();editor.SelectionChanged?.Invoke();editor.DragCancelled?.Invoke();editor.MarkDirtyRepaint();}
                editor.IsDragging=false;target.RemoveFromClassList("dragging");target.ReleasePointer(id);editor.InteractionFinished?.Invoke();
            }
            private void Up(PointerUpEvent e) {if (e.pointerId == pointer) {Finish(false); e.StopPropagation();}}
            private void Cancel(PointerCancelEvent e) {if (e.pointerId == pointer) Finish(true);}
            private void Lost(PointerCaptureOutEvent e) {if (e.pointerId == pointer) Finish(true);}
            private void Key(KeyDownEvent e)
            {
                if (e.keyCode == KeyCode.Escape && pointer >= 0) {Finish(true); e.StopPropagation();}
                else if (pointer < 0 && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)) {editor.RemoveSelected(); e.StopPropagation();}
            }
            private void Wheel(WheelEvent e)
            {
                if (pointer >= 0) return;
                var pixel = e.localMousePosition; var before = editor.CanvasToProfile(pixel);
                editor.pixelsPerMetre = Mathf.Clamp(editor.pixelsPerMetre * Mathf.Exp(-e.delta.y * .08f), .001f, 1000000);
                editor.centre += before - editor.CanvasToProfile(pixel); editor.MarkDirtyRepaint(); e.StopPropagation();
            }
        }
    }
}
