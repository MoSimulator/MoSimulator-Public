using System.Collections.Generic;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Measurements taken off mesh geometry. The centre of mass here is the area-weighted centroid of
    /// the surface — the centre of mass of a uniform shell — which puts a pivot where the geometry
    /// actually is rather than at the middle of its bounding box.
    /// </summary>
    /// <remarks>
    /// Nothing checks <c>Mesh.isReadable</c>: imported models ship with Read/Write disabled and the
    /// editor can still read their geometry. Skipping them would quietly fall back to transform pivots,
    /// which for a model whose parts all sit at the origin is nowhere near the mass.
    /// </remarks>
    public static class MeshMath
    {
        /// <summary>Centre of mass of one mesh, in its own local space.</summary>
        public static bool TrySurfaceCenter(Mesh mesh, out Vector3 center)
        {
            center = Vector3.zero;
            if (mesh == null) return false;

            var accumulator = new Accumulator();
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int t = 0; t + 2 < triangles.Length; t += 3)
                accumulator.Add(vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]);

            return accumulator.TryResolve(out center);
        }

        /// <summary>Combined centre of mass of several mesh objects, in world space.</summary>
        public static bool TrySurfaceCenter(IEnumerable<MeshFilter> filters, out Vector3 center)
        {
            var accumulator = new Accumulator();

            foreach (MeshFilter filter in filters)
            {
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Transform transform = filter.transform;

                for (int t = 0; t + 2 < triangles.Length; t += 3)
                    accumulator.Add(
                        transform.TransformPoint(vertices[triangles[t]]),
                        transform.TransformPoint(vertices[triangles[t + 1]]),
                        transform.TransformPoint(vertices[triangles[t + 2]]));
            }

            return accumulator.TryResolve(out center);
        }

        /// <summary>Centre of mass of loose geometry that is not a Mesh yet.</summary>
        public static bool TrySurfaceCenter(IReadOnlyList<Vector3> vertices,
            IEnumerable<IReadOnlyList<int>> submeshes, out Vector3 center)
        {
            var accumulator = new Accumulator();

            foreach (IReadOnlyList<int> triangles in submeshes)
                for (int t = 0; t + 2 < triangles.Count; t += 3)
                    accumulator.Add(vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]);

            return accumulator.TryResolve(out center);
        }

        /// <summary>Plain mean of a set of points, for the fallbacks when there is no area to weight by.</summary>
        public static Vector3 Average(IEnumerable<Vector3> points)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (Vector3 point in points)
            {
                sum += point;
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        /// <summary>Running area-weighted centroid; resolves to nothing when the geometry has no area.</summary>
        struct Accumulator
        {
            Vector3 _weighted;
            float _total;

            public void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                _weighted += (a + b + c) / 3f * area;
                _total += area;
            }

            public bool TryResolve(out Vector3 center)
            {
                center = _total > 0f ? _weighted / _total : Vector3.zero;
                return _total > 0f;
            }
        }
    }
}
