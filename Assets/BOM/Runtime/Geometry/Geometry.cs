using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using UnityEngine;

namespace BOM
{
    public readonly struct Segment
    {
        public readonly Vector2 a, b;
        public readonly bool cut;
        public Segment(Vector2 a, Vector2 b, bool cut = false) { this.a = a; this.b = b; this.cut = cut; }
        public float Length => Vector2.Distance(a, b);
    }

    public readonly struct Range
    {
        public readonly double lo, hi;
        public Range(double lo, double hi) { this.lo = lo; this.hi = hi; }
    }

    public static class Geometry
    {
        public const double Units = 100000;
        public const float Tolerance = .00001f;
        public const float ContactTolerance = .0001f;
        public const double AreaTolerance = .00000001;
        public const float CoordinateLimit = 10000;
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool Finite(Color v) => Finite(v.r) && Finite(v.g) && Finite(v.b) && Finite(v.a);
        public static bool Positive(float v) => Finite(v) && v > 0;
        public static bool Nonnegative(float v) => Finite(v) && v >= 0;
        public static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
        public static Vector2 Rotate(Vector2 v, float radians)
        {
            double c = Math.Cos(radians), s = Math.Sin(radians);
            return new Vector2((float)(c * v.x - s * v.y), (float)(s * v.x + c * v.y));
        }
        public static Point64 Point(Vector2 p) => new Point64((long)Math.Round(p.x * Units, MidpointRounding.AwayFromZero),
            (long)Math.Round(p.y * Units, MidpointRounding.AwayFromZero));
        public static Vector2 Vector(Point64 p) => new Vector2((float)(p.X / Units), (float)(p.Y / Units));
        public static Path64 Path(Contour contour) => new Path64(contour.points.Select(Point));
        public static Paths64 Paths(Region region) => new Paths64(Normalize(region).Contours.Select(Path));
        public static double SignedArea(IReadOnlyList<Vector2> ring)
        {
            if (ring.Count < 3) return 0;
            double a = 0, x = ring[0].x, y = ring[0].y;
            for (int i = 0; i < ring.Count; i++)
            { var p=ring[i];var q=ring[(i+1)%ring.Count];a+=((double)p.x-x)*((double)q.y-y)-((double)p.y-y)*((double)q.x-x); }
            return a * .5;
        }
        public static double Area(Region region) => Math.Abs(SignedArea(region.outer.points)) - region.holes.Sum(h => Math.Abs(SignedArea(h.points)));

        public static Region Normalize(Region region)
        {
            Contour Ring(Contour input, bool positive)
            {
                var result = new Contour();
                for (int i = 0; i < input.points.Count; i++)
                {
                    var p = Vector(Point(input.points[i]));
                    if (result.points.Count > 0 && result.points[result.points.Count - 1] == p) continue;
                    result.points.Add(p);
                    result.cutEdges.Add(input.cutEdges.Count == input.points.Count && input.cutEdges[i]);
                }
                if (result.points.Count > 1 && result.points[0] == result.points[result.points.Count - 1])
                { result.points.RemoveAt(result.points.Count - 1); result.cutEdges.RemoveAt(result.cutEdges.Count - 1); }
                if ((SignedArea(result.points) > 0) != positive)
                {
                    var old = result.cutEdges.ToArray(); result.points.Reverse();
                    for (int i = 0; i < old.Length; i++) result.cutEdges[i] = old[(old.Length - 2 - i + old.Length) % old.Length];
                }
                return result;
            }
            return new Region { outer = Ring(region.outer, true), holes = region.holes.Select(h => Ring(h, false)).ToList() };
        }

        public static bool TryValidate(Region region, out string error)
        {
            error = "Missing outer contour or hole list.";
            if (region?.outer?.points == null || region.holes == null || region.holes.Any(h => h?.points == null)) return false;
            foreach (var ring in region.Contours)
                for (int i = 0; i < ring.points.Count; i++)
                    if (!Finite(ring.points[i]) || Mathf.Abs(ring.points[i].x) > CoordinateLimit || Mathf.Abs(ring.points[i].y) > CoordinateLimit)
                    { error = "Contour vertex " + i + " must be finite and within +/-10000 metres."; return false; }
            var r = Normalize(region);
            int number = 0;
            foreach (var ring in r.Contours)
            {
                string name = number++ == 0 ? "Outer contour" : "Hole " + (number - 1);
                if (ring.points.Count < 3 || Math.Abs(SignedArea(ring.points)) <= AreaTolerance)
                { error = name + " needs at least three distinct vertices and nonzero area."; return false; }
                for (int i = 0; i < ring.points.Count; i++)
                {
                    var a = ring.points[i]; var b = ring.points[(i + 1) % ring.points.Count];
                    var c = ring.points[(i + 2) % ring.points.Count];
                    if (Distance(a, b, c) <= Tolerance && Vector2.Dot(b - a, c - b) < 0)
                    { error = name + " doubles back at vertex " + ((i + 1) % ring.points.Count) + "."; return false; }
                    for (int j = i + 1; j < ring.points.Count; j++)
                    {
                        if (j == i + 1 || (i == 0 && j == ring.points.Count - 1)) continue;
                        if (Touches(a, b, ring.points[j], ring.points[(j + 1) % ring.points.Count]))
                        { error = name + " intersects or touches itself at edges " + i + " and " + j + "."; return false; }
                    }
                }
            }
            for (int i = 0; i < r.holes.Count; i++)
            {
                var hole = r.holes[i];
                if (Clipper.PointInPolygon(Point(hole.points[0]), Path(r.outer)) != PointInPolygonResult.IsInside)
                { error = "Hole " + i + " must be strictly inside the outer contour."; return false; }
                foreach (var other in new[] { r.outer }.Concat(r.holes.Take(i)))
                {
                    if (Edges(hole).Any(a => Edges(other).Any(b => Touches(a.a, a.b, b.a, b.b))))
                    { error = "Hole " + i + " touches or intersects another contour."; return false; }
                    if (other != r.outer && (Inside(other, hole.points[0]) || Inside(hole, other.points[0])))
                    { error = "Hole " + i + " overlaps or contains another hole."; return false; }
                }
            }
            if (Area(r) <= AreaTolerance) { error = "The solid region has no positive area."; return false; }
            error = null; return true;
        }

        public static IEnumerable<Segment> Edges(Contour contour)
        {
            for (int i = 0; i < contour.points.Count; i++)
                yield return new Segment(contour.points[i], contour.points[(i + 1) % contour.points.Count],
                    contour.cutEdges.Count == contour.points.Count && contour.cutEdges[i]);
        }
        public static IEnumerable<Segment> Edges(Region r) => r.Contours.SelectMany(Edges);
        public static float Distance(Vector2 a, Vector2 b, Vector2 p)
        {
            var d = b - a;
            return Vector2.Distance(p, a + d * (d.sqrMagnitude == 0 ? 0 : Mathf.Clamp01(Vector2.Dot(p - a, d) / d.sqrMagnitude)));
        }
        public static bool Touches(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (Mathf.Max(a.x, b.x) + Tolerance < Mathf.Min(c.x, d.x) || Mathf.Max(c.x, d.x) + Tolerance < Mathf.Min(a.x, b.x)
                || Mathf.Max(a.y, b.y) + Tolerance < Mathf.Min(c.y, d.y) || Mathf.Max(c.y, d.y) + Tolerance < Mathf.Min(a.y, b.y)) return false;
            double abC = Cross(b - a, c - a), abD = Cross(b - a, d - a), cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            return (abC * abD < 0 && cdA * cdB < 0) || Distance(a, b, c) <= Tolerance || Distance(a, b, d) <= Tolerance
                || Distance(c, d, a) <= Tolerance || Distance(c, d, b) <= Tolerance;
        }
        private static bool Inside(Contour c, Vector2 p) => Clipper.PointInPolygon(Point(p), Path(c)) != PointInPolygonResult.IsOutside;
        public static bool Contains(Region r, Vector2 p, bool includeBoundary = true)
        {
            if (includeBoundary && Edges(r).Any(e => Distance(e.a, e.b, p) <= Tolerance)) return true;
            return Inside(r.outer, p) && !r.holes.Any(h => Inside(h, p));
        }

        public static List<Region> Boolean(Paths64 subject, Paths64 clip, ClipType operation)
        {
            var tree = new PolyTree64();
            Clipper.BooleanOp(operation, subject, clip, tree, FillRule.NonZero);
            var regions = new List<Region>();
            void Visit(PolyPath64 node)
            {
                if (node.Polygon != null && !node.IsHole)
                {
                    var region = new Region { outer = new Contour(node.Polygon.Select(Vector)) };
                    for (int i = 0; i < node.Count; i++) if (node[i].IsHole) region.holes.Add(new Contour(node[i].Polygon.Select(Vector)));
                    regions.Add(Normalize(region));
                }
                for (int i = 0; i < node.Count; i++) Visit(node[i]);
            }
            Visit(tree);
            return regions.OrderBy(r => r.outer.points.Min(p => p.x)).ThenBy(r => r.outer.points.Min(p => p.y)).ToList();
        }
        public static List<Region> Difference(Region source, Region cutter)
        {
            var regions = Boolean(Paths(source), Paths(cutter), ClipType.Difference);
            foreach (var r in regions)
                foreach (var contour in r.Contours)
                {
                    contour.cutEdges.Clear();
                    foreach (var edge in Edges(contour).ToArray())
                    {
                        var prior = Edges(source).Where(p => Distance(p.a, p.b, edge.a) <= ContactTolerance
                            && Distance(p.a, p.b, edge.b) <= ContactTolerance).ToArray();
                        contour.cutEdges.Add(prior.Length == 0 || prior.Any(p => p.cut));
                    }
                }
            return regions;
        }
        public static List<Vector2[]> Triangles(Region region)
        {
            if (!TryValidate(region, out var error)) throw new ArgumentException(error);
            var status = Clipper.Triangulate(Paths(region), out var triangles);
            if (status != TriangulateResult.success) throw new InvalidOperationException("Profile triangulation failed: " + status);
            var result = triangles.Select(p => p.Select(Vector).ToArray()).ToList();
            double area = result.Sum(p => Math.Abs(SignedArea(p)));
            if (Math.Abs(area - Area(region)) > Math.Max(AreaTolerance, Area(region) * .00001))
                throw new InvalidOperationException("Triangulation does not preserve the solid area, including holes.");
            return result;
        }
        public static Vector2 Centroid(Region region)
        {
            double total = 0, x = 0, y = 0, originX=region.outer.points[0].x, originY=region.outer.points[0].y;
            foreach (var c in Normalize(region).Contours)
                foreach (var e in Edges(c))
                { double ax=(double)e.a.x-originX, ay=(double)e.a.y-originY, bx=(double)e.b.x-originX, by=(double)e.b.y-originY;
                    double k=ax*by-ay*bx;total+=k;x+=(ax+bx)*k;y+=(ay+by)*k; }
            return new Vector2((float)(originX+x/(3*total)),(float)(originY+y/(3*total)));
        }
        public static double Moment(Region region)
        {
            double sum = 0;var centre=Centroid(region);
            // Integrate about the current COM directly to avoid subtracting large nearly equal moments.
            foreach(var c in Normalize(region).Contours) foreach(var e in Edges(c))
            {
                double ax=(double)e.a.x-centre.x,ay=(double)e.a.y-centre.y,bx=(double)e.b.x-centre.x,by=(double)e.b.y-centre.y;
                sum+=(ax*by-ay*bx)*(ax*ax+ay*ay+ax*bx+ay*by+bx*bx+by*by)/12;
            }
            return sum;
        }
        public static Region Transform(Region region, Func<Vector2, Vector2> transform)
        {
            var r = region.Clone(); foreach (var c in r.Contours) c.points = c.points.Select(transform).ToList(); return r;
        }

        public static List<Range> SegmentRanges(Region region, Vector2 a, Vector2 b)
        {
            var t = new List<double> { 0, 1 }; var delta = b - a;
            foreach (var e in Edges(region))
            {
                double denominator = Cross(delta, e.b - e.a);
                double lineTolerance=Tolerance*Math.Sqrt(delta.sqrMagnitude);
                if(delta.sqrMagnitude>0)
                {
                    if(Math.Abs(Cross(delta,e.a-a))<=lineTolerance)t.Add(Math.Max(0,Math.Min(1,Vector2.Dot(e.a-a,delta)/delta.sqrMagnitude)));
                    if(Math.Abs(Cross(delta,e.b-a))<=lineTolerance)t.Add(Math.Max(0,Math.Min(1,Vector2.Dot(e.b-a,delta)/delta.sqrMagnitude)));
                }
                if (Math.Abs(denominator) > 1e-12)
                {
                    double u = Cross(e.a - a, e.b - e.a) / denominator, v = Cross(e.a - a, delta) / denominator;
                    if (u > 0 && u < 1 && v >= 0 && v <= 1) t.Add(u);
                }
            }
            t = t.Distinct().OrderBy(v => v).ToList(); var ranges = new List<Range>();
            for (int i = 0; i + 1 < t.Count; i++)
            {
                if (t[i + 1] - t[i] < 1e-10 || !Contains(region, a + delta * (float)((t[i] + t[i + 1]) * .5))) continue;
                if (ranges.Count > 0 && Math.Abs(ranges[ranges.Count - 1].hi - t[i]) < 1e-8)
                    ranges[ranges.Count - 1] = new Range(ranges[ranges.Count - 1].lo, t[i + 1]);
                else ranges.Add(new Range(t[i], t[i + 1]));
            }
            return ranges;
        }
        public static bool FullBoundary(Region region, Vector2 a, Vector2 b)
        {
            if (Vector2.Distance(a, b) <= ContactTolerance) return false;
            var ranges = SegmentRanges(region, a, b);
            return ranges.Count == 1 && ranges[0].lo <= 1e-6 && ranges[0].hi >= 1 - 1e-6
                && Edges(region).Any(e => Distance(e.a, e.b, (a + b) * .5f) <= ContactTolerance);
        }
        public static Region Rectangle(float x0, float y0, float x1, float y1) => new Region
        { outer = new Contour(new[] { new Vector2(x0,y0), new Vector2(x1,y0), new Vector2(x1,y1), new Vector2(x0,y1) }) };
    }
}
