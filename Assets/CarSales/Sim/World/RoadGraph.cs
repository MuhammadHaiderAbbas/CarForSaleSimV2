using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    [Serializable]
    public struct V2
    {
        public float x, z;
        public V2(float x, float z) { this.x = x; this.z = z; }
        public static V2 operator +(V2 a, V2 b) => new V2(a.x + b.x, a.z + b.z);
        public static V2 operator -(V2 a, V2 b) => new V2(a.x - b.x, a.z - b.z);
        public static V2 operator *(V2 a, float k) => new V2(a.x * k, a.z * k);
        public float Length => (float)Math.Sqrt(x * x + z * z);
        public V2 Normalized { get { float l = Length; return l > 1e-5f ? new V2(x / l, z / l) : new V2(0f, 0f); } }
        public static float Dot(V2 a, V2 b) => a.x * b.x + a.z * b.z;
        public static float Distance(V2 a, V2 b) => (a - b).Length;
    }

    /// <summary>A straight piece of road between two junctions or ends.</summary>
    [Serializable]
    public struct RoadSegment { public V2 a, b; public float speedLimit; public RoadSegment(V2 a, V2 b, float limit = 13.4f) { this.a = a; this.b = b; speedLimit = limit; } }

    public sealed class Lane
    {
        public int id;
        public V2 start, end;
        public float speedLimit;
        public bool alongX;                       // for the traffic-light phase
        public bool endsAtJunction;
        public readonly List<int> next = new List<int>();
        public float Length => V2.Distance(start, end);
        public V2 Dir => (end - start).Normalized;
        public V2 PointAt(float d) => start + Dir * Math.Max(0f, Math.Min(Length, d));
    }

    /// <summary>
    /// Builds the traffic lane graph from road segments (spec §6.13): two lanes per segment, driving on the LEFT
    /// (UK), offset by a quarter of the road width; lanes that end where another segment starts are joined
    /// (straight on, left, right - never a U-turn); a node shared by three or more segments is a junction.
    /// </summary>
    public static class RoadGraph
    {
        public static List<Lane> Build(IList<RoadSegment> segments, float roadWidth = 9f, float joinRadius = 6f)
        {
            var lanes = new List<Lane>();
            float off = roadWidth / 4f;
            foreach (var s in segments)
                foreach (bool forward in new[] { true, false })
                {
                    V2 a = forward ? s.a : s.b, b = forward ? s.b : s.a;
                    V2 dir = (b - a).Normalized;
                    var left = new V2(-dir.z, dir.x);      // left of travel
                    lanes.Add(new Lane
                    {
                        id = lanes.Count, start = a + left * off, end = b + left * off, speedLimit = s.speedLimit,
                        alongX = Math.Abs(dir.x) >= Math.Abs(dir.z),
                    });
                }
            // Junction nodes: road ends shared by 3+ segment ends.
            var ends = new List<V2>();
            foreach (var s in segments) { ends.Add(s.a); ends.Add(s.b); }
            bool IsJunction(V2 p)
            {
                int n = 0;
                foreach (var e in ends) if (V2.Distance(e, p) < joinRadius) n++;
                return n >= 3;
            }
            foreach (var l in lanes)
            {
                // The road node this lane ends at (lane end minus the side offset).
                var dir = l.Dir;
                var node = l.end - new V2(-dir.z, dir.x) * off;
                l.endsAtJunction = IsJunction(node);
                foreach (var m in lanes)
                {
                    if (m == l) continue;
                    var mdir = m.Dir;
                    var mNode = m.start - new V2(-mdir.z, mdir.x) * off;
                    if (V2.Distance(node, mNode) > joinRadius) continue;
                    if (V2.Dot(dir, mdir) < -0.5f) continue;   // no U-turns
                    l.next.Add(m.id);
                }
            }
            return lanes;
        }

        /// <summary>The default v4 town: two long roads (z = -27 and 95) and two cross roads (x = ±70).</summary>
        public static List<RoadSegment> DefaultTown()
        {
            var list = new List<RoadSegment>();
            foreach (float z in new[] { -27f, 95f })
            {
                list.Add(new RoadSegment(new V2(-250f, z), new V2(-70f, z)));
                list.Add(new RoadSegment(new V2(-70f, z), new V2(70f, z)));
                list.Add(new RoadSegment(new V2(70f, z), new V2(250f, z)));
            }
            foreach (float x in new[] { -70f, 70f }) list.Add(new RoadSegment(new V2(x, -27f), new V2(x, 95f), 11f));
            return list;
        }
    
    }
    /// <summary>Editing the road network (World Builder, phase 8b): snapping new roads and splitting roads at T-junctions.</summary>
    public static class RoadEditing
    {
        public const float SnapRadius = 8f;
        public const float MinLength = 10f;

        static float DistToSegment(V2 p, V2 a, V2 b, out float t)
        {
            var ab = b - a;
            float len2 = V2.Dot(ab, ab);
            t = len2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, V2.Dot(p - a, ab) / len2));
            return V2.Distance(p, a + ab * t);
        }

        /// <summary>Moves a point onto the nearest road end (within the snap radius) or onto the nearest road.</summary>
        public static V2 Snap(IList<RoadSegment> roads, V2 p, float radius = SnapRadius)
        {
            float best = radius; V2 snapped = p; bool found = false;
            foreach (var r in roads)
                foreach (var e in new[] { r.a, r.b })
                    if (V2.Distance(e, p) < best) { best = V2.Distance(e, p); snapped = e; found = true; }
            if (found) return snapped;
            best = radius;
            foreach (var r in roads)
            {
                float d = DistToSegment(p, r.a, r.b, out float t);
                if (d < best) { best = d; snapped = r.a + (r.b - r.a) * t; }
            }
            return snapped;
        }

        /// <summary>
        /// Adds a road: both ends are snapped; where an end lands in the middle of an existing road, that road is split
        /// so a junction appears in the lane graph. Returns false if the new road is too short.
        /// </summary>
        public static bool Add(List<RoadSegment> roads, V2 a, V2 b, float speedLimit = 13.4f)
        {
            a = Snap(roads, a);
            b = Snap(roads, b);
            if (V2.Distance(a, b) < MinLength) return false;
            SplitAt(roads, a);
            SplitAt(roads, b);
            roads.Add(new RoadSegment(a, b, speedLimit));
            return true;
        }

        /// <summary>Splits the road that passes through <paramref name="p"/> (not at its ends) into two pieces.</summary>
        public static bool SplitAt(List<RoadSegment> roads, V2 p)
        {
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i];
                if (V2.Distance(r.a, p) < 1f || V2.Distance(r.b, p) < 1f) continue;
                if (DistToSegment(p, r.a, r.b, out float t) > 0.5f || t <= 0.01f || t >= 0.99f) continue;
                roads[i] = new RoadSegment(r.a, p, r.speedLimit);
                roads.Insert(i + 1, new RoadSegment(p, r.b, r.speedLimit));
                return true;
            }
            return false;
        }

        /// <summary>Points every <paramref name="spacing"/> metres along both sides of a road (for trees and lamps).</summary>
        public static List<(V2 point, V2 facing)> Scatter(RoadSegment r, float spacing, float sideOffset, float endGap = 10f)
        {
            var list = new List<(V2, V2)>();
            var dir = (r.b - r.a).Normalized;
            var left = new V2(-dir.z, dir.x);
            float len = V2.Distance(r.a, r.b);
            for (float d = endGap; d <= len - endGap; d += spacing)
            {
                var c = r.a + dir * d;
                list.Add((c + left * sideOffset, left * -1f));
                list.Add((c - left * sideOffset, left));
            }
            return list;
        }
    }

}
