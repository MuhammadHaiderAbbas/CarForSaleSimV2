using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarSales.Sim;
using UnityEngine;

namespace CarSales
{
    /// <summary>
    /// Shows body damage as dents on a car model (parked, listed or driven), worked out from the body panels'
    /// health by <see cref="DentModel"/>. Meshes are copied before they're changed (the shared model is never
    /// touched) and restored when the car is repaired. Wheels, tyres and glass are skipped.
    /// </summary>
    public class CarDamageVisual : MonoBehaviour
    {
        static readonly Regex Skip = new Regex("(wheel|tyre|tire|hub|rim|glass|window|light|lamp)", RegexOptions.IgnoreCase);

        class Part
        {
            public MeshFilter filter;
            public Mesh original, dented;
            public Vector3[] baseVertices;
        }

        readonly List<Part> parts = new List<Part>();
        int signature = int.MinValue;
        bool collected;

        /// <summary>Adds (if needed) and updates the dents on a car model.</summary>
        public static void Apply(GameObject model, CarInstance car)
        {
            if (!model || car == null) return;
            CarParts.EnsureParts(car);
            var depths = DentModel.ZoneDepths(car.components);
            var visual = model.GetComponent<CarDamageVisual>();
            if (!visual)
            {
                if (!DentModel.Any(depths)) return;   // nothing to show: don't copy any meshes
                visual = model.AddComponent<CarDamageVisual>();
            }
            visual.Set(depths);
        }

        void Collect()
        {
            collected = true;
            foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            {
                if (!f.sharedMesh || Skip.IsMatch(f.name) || !f.sharedMesh.isReadable) continue;
                parts.Add(new Part { filter = f, original = f.sharedMesh, baseVertices = f.sharedMesh.vertices });
            }
        }

        void Set(float[] depths)
        {
            int sig = DentModel.Signature(depths);
            if (sig == signature) return;
            signature = sig;
            if (!collected) Collect();
            bool any = DentModel.Any(depths);

            // Body bounds in the model's own space.
            var bounds = new Bounds();
            bool first = true;
            foreach (var p in parts)
            {
                var m = p.filter.transform;
                foreach (var corner in Corners(p.original.bounds))
                {
                    var c = transform.InverseTransformPoint(m.TransformPoint(corner));
                    if (first) { bounds = new Bounds(c, Vector3.zero); first = false; } else bounds.Encapsulate(c);
                }
            }

            foreach (var p in parts)
            {
                if (!any)
                {
                    p.filter.sharedMesh = p.original;
                    continue;
                }
                if (!p.dented) { p.dented = Instantiate(p.original); p.dented.name = p.original.name + " (dented)"; }
                var t = p.filter.transform;
                var verts = new Vector3[p.baseVertices.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    var local = transform.InverseTransformPoint(t.TransformPoint(p.baseVertices[i]));
                    DentModel.Displace(local.x, local.y, local.z, bounds.min.x, bounds.max.x, bounds.max.y, bounds.min.z, bounds.max.z,
                                       depths, out float dx, out float dy, out float dz);
                    var moved = transform.TransformPoint(local + new Vector3(dx, dy, dz));
                    verts[i] = t.InverseTransformPoint(moved);
                }
                p.dented.vertices = verts;
                p.dented.RecalculateNormals();
                p.dented.RecalculateBounds();
                p.filter.sharedMesh = p.dented;
            }
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++)
                yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }

        void OnDestroy()
        {
            foreach (var p in parts)
            {
                if (p.filter) p.filter.sharedMesh = p.original;
                if (p.dented) Destroy(p.dented);
            }
        }
    }
}
