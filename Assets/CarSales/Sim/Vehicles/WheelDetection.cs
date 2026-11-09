using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CarSales.Sim
{
    public enum WheelCorner { FrontLeft = 0, FrontRight = 1, RearLeft = 2, RearRight = 3 }

    /// <summary>A mesh that might be a wheel: an id the caller understands, its name, centre (car space, +z forward, +x right) and radius.</summary>
    public struct WheelCandidate
    {
        public int Id;
        public string Name;
        public float X, Y, Z, Radius;
    }

    /// <summary>
    /// Finds a car's 4 wheels in an imported model: by name ("wheel", "tyre", "tire", "FL", "rear_left"...) and,
    /// if names don't say which corner, by position. Used by the Asset Studio and the runtime vehicle setup.
    /// </summary>
    public static class WheelDetection
    {
        static readonly Regex Wheel = new Regex("(wheel|tyre|tire|rueda|reifen|roue)", RegexOptions.IgnoreCase);
        static readonly Regex NotWheel = new Regex("(steer|spare|cover|arch|well|caliper|brake|disc|disk|hub_?cap|bolt|nut)", RegexOptions.IgnoreCase);
        public static bool LooksLikeWheel(string name) => !string.IsNullOrEmpty(name) && Wheel.IsMatch(name) && !NotWheel.IsMatch(name);

        /// <summary>Corner suggested by the name only, or null if the name doesn't say.</summary>
        public static WheelCorner? CornerFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.ToLowerInvariant();
            bool left = n.Contains("left") || Regex.IsMatch(n, @"(^|[^a-z])(fl|lf|rl|lr|bl|lb)([^a-z]|$)") || Regex.IsMatch(n, @"(^|[^a-z])[fr][_\-\.]l([^a-z]|$)");
            bool right = n.Contains("right") || Regex.IsMatch(n, @"(^|[^a-z])(fr|rf|rr|br|rb)([^a-z]|$)") || Regex.IsMatch(n, @"(^|[^a-z])[fr][_\-\.]r([^a-z]|$)");
            if (left == right) return null;
            bool front = n.Contains("front") || Regex.IsMatch(n, @"(^|[^a-z])(fl|fr|lf|rf)([^a-z]|$)") || Regex.IsMatch(n, @"(^|[^a-z])f[_\-\.][lr]([^a-z]|$)");
            bool rear = n.Contains("rear") || n.Contains("back") || Regex.IsMatch(n, @"(^|[^a-z])(rl|lr|bl|br|lb|rb)([^a-z]|$)") ||
                        Regex.IsMatch(n, @"(^|[^a-z])r[_\-\.][lr]([^a-z]|$)") || (n.Contains("rr") && Regex.IsMatch(n, @"(^|[^a-z])rr([^a-z]|$)"));
            if (front == rear) return null;
            return front ? (left ? WheelCorner.FrontLeft : WheelCorner.FrontRight) : (left ? WheelCorner.RearLeft : WheelCorner.RearRight);
        }

        /// <summary>
        /// Picks one candidate per corner (array index = <see cref="WheelCorner"/>), or null if it can't find all four.
        /// Position decides the corner (names only break ties), the lowest mesh wins inside a corner.
        /// </summary>
        public static int[] AssignCorners(IList<WheelCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 4) return null;
            float cx = 0f, cz = 0f;
            foreach (var c in candidates) { cx += c.X; cz += c.Z; }
            cx /= candidates.Count; cz /= candidates.Count;

            var result = new int[4];
            for (int corner = 0; corner < 4; corner++)
            {
                bool wantFront = corner < 2, wantLeft = corner % 2 == 0;
                int best = -1;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    if ((c.Z > cz) != wantFront || (c.X < cx) != wantLeft) continue;
                    if (best < 0) { best = i; continue; }
                    var b = candidates[best];
                    // Prefer a name that agrees with this corner, then the lowest, then the biggest.
                    bool cName = CornerFromName(c.Name) == (WheelCorner)corner, bName = CornerFromName(b.Name) == (WheelCorner)corner;
                    if (cName != bName) { if (cName) best = i; continue; }
                    if (c.Y < b.Y - 0.01f || (Math.Abs(c.Y - b.Y) <= 0.01f && c.Radius > b.Radius)) best = i;
                }
                if (best < 0) return null;
                result[corner] = best;
            }
            return result;
        }
    }
}
