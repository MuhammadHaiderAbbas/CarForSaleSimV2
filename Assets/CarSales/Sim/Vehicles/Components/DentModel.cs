using System;
using System.Collections.Generic;

namespace CarSales.Sim
{
    /// <summary>
    /// Visible body damage worked out from the health of the body panels: no extra save data, so a car looks the same
    /// parked, listed or driven, and a repair removes the dents. Pure maths (the runtime applies it to the meshes).
    /// </summary>
    public static class DentModel
    {
        /// <summary>Deepest dent at a fully wrecked panel, metres.</summary>
        public const float MaxDepth = 0.22f;
        /// <summary>How far into the car a front/rear dent reaches, metres.</summary>
        public const float EndBand = 0.75f;
        /// <summary>How far into the car a side dent reaches, metres.</summary>
        public const float SideBand = 0.35f;

        static float Damage(List<ComponentState> list, params ComponentId[] ids)
        {
            float sum = 0f;
            int n = 0;
            foreach (var id in ids)
            {
                if (!ComponentSet.Has(list, id)) continue;
                sum += 1f - ComponentSet.Get(list, id) / 100f;
                n++;
            }
            float d = n > 0 ? sum / n : 0f;
            // Light wear (scratches) doesn't dent: start at 15% damage.
            return d < 0.15f ? 0f : Math.Min(1f, (d - 0.15f) / 0.85f);
        }

        /// <summary>Dent depth in metres for each <see cref="ImpactZone"/> (index = zone).</summary>
        public static float[] ZoneDepths(List<ComponentState> list)
        {
            var depths = new float[6];
            if (list == null || list.Count == 0) return depths;
            depths[(int)ImpactZone.Front] = MaxDepth * Damage(list, ComponentId.BumperFront, ComponentId.Bonnet, ComponentId.WingsFront);
            depths[(int)ImpactZone.Rear] = MaxDepth * Damage(list, ComponentId.BumperRear, ComponentId.BootLid, ComponentId.QuarterPanelsRear);
            depths[(int)ImpactZone.Left] = MaxDepth * 0.6f * Damage(list, ComponentId.DoorsLeft);
            depths[(int)ImpactZone.Right] = MaxDepth * 0.6f * Damage(list, ComponentId.DoorsRight);
            depths[(int)ImpactZone.Roof] = MaxDepth * 0.5f * Damage(list, ComponentId.Roof);
            return depths;
        }

        /// <summary>Rounded summary of the depths, to skip re-deforming a model that hasn't changed.</summary>
        public static int Signature(float[] depths)
        {
            int h = 17;
            foreach (var d in depths) h = h * 31 + (int)Math.Round(d * 200f);
            return h;
        }

        public static bool Any(float[] depths)
        {
            foreach (var d in depths) if (d > 0.002f) return true;
            return false;
        }

        /// <summary>
        /// Displacement for one vertex at (x, y, z) in the car's local space (origin at ground centre, +z forward),
        /// given the car's body bounds. Writes the offset into dx, dy, dz.
        /// </summary>
        public static void Displace(float x, float y, float z, float minX, float maxX, float maxY, float minZ, float maxZ,
                                    float[] depths, out float dx, out float dy, out float dz)
        {
            dx = dy = dz = 0f;
            float crumple = 0.75f + 0.5f * Noise(x, y, z);   // uneven, crumpled look

            float front = depths[(int)ImpactZone.Front];
            if (front > 0f) { float t = Band(z, maxZ - EndBand, maxZ); dz -= front * t * t * crumple; dy -= front * 0.25f * t * crumple; }
            float rear = depths[(int)ImpactZone.Rear];
            if (rear > 0f) { float t = Band(-z, -(minZ + EndBand), -minZ); dz += rear * t * t * crumple; dy -= rear * 0.2f * t * crumple; }
            float left = depths[(int)ImpactZone.Left];
            if (left > 0f) { float t = Band(-x, -(minX + SideBand), -minX) * MidLength(z, minZ, maxZ); dx += left * t * crumple; }
            float right = depths[(int)ImpactZone.Right];
            if (right > 0f) { float t = Band(x, maxX - SideBand, maxX) * MidLength(z, minZ, maxZ); dx -= right * t * crumple; }
            float roof = depths[(int)ImpactZone.Roof];
            if (roof > 0f) { float t = Band(y, maxY - 0.3f, maxY) * MidLength(z, minZ, maxZ); dy -= roof * t * crumple; }
        }

        /// <summary>0 below <paramref name="from"/>, rising to 1 at <paramref name="to"/>.</summary>
        static float Band(float v, float from, float to)
        {
            if (to <= from) return 0f;
            float t = (v - from) / (to - from);
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        /// <summary>Side and roof dents are strongest in the middle of the car and fade towards the ends.</summary>
        static float MidLength(float z, float minZ, float maxZ)
        {
            float half = (maxZ - minZ) * 0.5f;
            if (half <= 0f) return 0f;
            float t = 1f - Math.Abs(z - (minZ + maxZ) * 0.5f) / half;
            return t < 0f ? 0f : (float)Math.Sqrt(t);
        }

        /// <summary>Deterministic 0..1 noise from a position (same vertex = same dent every time).</summary>
        static float Noise(float x, float y, float z)
        {
            double h = Math.Sin(x * 12.9898 + y * 78.233 + z * 37.719) * 43758.5453;
            return (float)(h - Math.Floor(h));
        }
    }
}
