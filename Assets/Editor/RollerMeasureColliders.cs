using RobotFramework.Components;
using UnityEngine;

namespace Editor
{
    public static partial class RollerMeasureTool
    {
        static void InvalidateMeasurement() { measuredA = measuredB = null; UnityEditor.SceneView.RepaintAll(); }

        static int GeometryHash(Component target)
        {
            unchecked
            {
                int hash = target.transform.localToWorldMatrix.GetHashCode() * 397 ^ UnityEditor.EditorUtility.GetDirtyCount(target);
                if (target is MeshCollider mesh)
                    return hash * 397 ^ mesh.convex.GetHashCode() ^ (mesh.sharedMesh == null ? 0 : mesh.sharedMesh.GetInstanceID() ^
                        UnityEditor.EditorUtility.GetDirtyCount(mesh.sharedMesh) ^ mesh.sharedMesh.bounds.GetHashCode());
                Core core = TargetCore(target);
                return hash * 397 ^ core.Center.GetHashCode() ^ core.Half.GetHashCode() ^ core.Radius.GetHashCode() ^ core.Start.GetHashCode() ^ core.End.GetHashCode();
            }
        }

        static float MeasuredGap(Component a, Component b, out Vector3 pa, out Vector3 pb)
        {
            int hash = unchecked(GeometryHash(a) * 397 ^ GeometryHash(b));
            if (measuredA != a || measuredB != b || measuredHash != hash)
            {
                measuredA = a; measuredB = b; measuredHash = hash;
                measurementError = null;
                try
                {
                    measuredGap = TargetGap(a, b, out measuredPointA, out measuredPointB);
                    if (float.IsNaN(measuredGap) || float.IsInfinity(measuredGap))
                        measurementError = "Collider has no measurable geometry.";
                }
                catch (System.Exception)
                {
                    measurementError = "Mesh geometry unavailable. Enable Read/Write on its model.";
                }
            }
            pa = measuredPointA; pb = measuredPointB;
            return measuredGap;
        }

        static bool Supported(Collider collider) => collider is BoxCollider || collider is SphereCollider ||
            collider is CapsuleCollider || (collider is MeshCollider mesh && mesh.sharedMesh != null);

        internal static float TargetGap(Component a, Component b, out Vector3 pa, out Vector3 pb)
        {
            if (a is MeshCollider nonconvexA && !nonconvexA.convex) return MeshGap(nonconvexA, b, out pa, out pb);
            if (b is MeshCollider nonconvexB && !nonconvexB.convex) return MeshGap(nonconvexB, a, out pb, out pa);
            if (a is MeshCollider ma && ma.convex || b is MeshCollider mb && mb.convex)
                return ConvexGap(a, b, out pa, out pb);
            return ShapeGap(TargetCore(a), TargetCore(b), out pa, out pb);
        }

        static Core TargetCore(Component target) => target is InteractionRoller roller ? new Core(roller) : new Core((Collider)target);

        static Vector3 TargetClosest(Component target, Vector3 point)
        {
            if (target is MeshCollider mesh) return mesh.ClosestPoint(point);
            return ShapeClosest(TargetCore(target), point);
        }

        static Vector3 ShapeClosest(Core shape, Vector3 point)
        {
            if (shape.Box) return shape.Closest(point);
            Vector3 spine = shape.End - shape.Start;
            float t = spine.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - shape.Start, spine) / spine.sqrMagnitude) : 0;
            Vector3 center = shape.Start + t * spine;
            Vector3 offset = point - center;
            return offset.sqrMagnitude <= shape.Radius * shape.Radius ? point : center + offset.normalized * shape.Radius;
        }

        static float ConvexGap(Component a, Component b, out Vector3 pa, out Vector3 pb)
        {
            // Unity's cooked convex hull can differ from sharedMesh. Project onto the
            // actual collider hull, not the source mesh or its axis-aligned bounds.
            return ProjectionGap(p => TargetClosest(a, p), p => TargetClosest(b, p),
                b.transform.position, out pa, out pb);
        }

        static float ProjectionGap(System.Func<Vector3, Vector3> closestA, System.Func<Vector3, Vector3> closestB,
            Vector3 seed, out Vector3 pa, out Vector3 pb)
        {
            pa = closestA(seed);
            pb = closestB(pa);
            for (int i = 0; i < 4096; i++)
            {
                Vector3 next = closestA(pb);
                Vector3 other = closestB(next);
                float change = (next - pa).sqrMagnitude + (other - pb).sqrMagnitude;
                pa = next; pb = other;
                if (change < 1e-14f) break;
            }
            return Vector3.Distance(pa, pb);
        }

        static float MeshGap(MeshCollider mesh, Component other, out Vector3 pa, out Vector3 pb)
        {
            Mesh source = mesh.sharedMesh;
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            float best = float.PositiveInfinity;
            pa = pb = mesh.transform.position;
            Core target = other is MeshCollider ? default : TargetCore(other);
            bool convex = other is MeshCollider convexMesh && convexMesh.convex;
            Vector3[] otherVertices = other is MeshCollider om && !convex ? om.sharedMesh.vertices : null;
            int[] otherTriangles = other is MeshCollider ot && !convex ? ot.sharedMesh.triangles : null;
            var matrix = mesh.transform.localToWorldMatrix;
            var otherMatrix = other.transform.localToWorldMatrix;
            Bounds targetBounds = other is Collider collider ? collider.bounds : ShapeBounds(target);
            Core[] otherShapes = otherTriangles == null ? null : new Core[otherTriangles.Length / 3];
            Bounds[] otherBounds = otherShapes == null ? null : new Bounds[otherShapes.Length];
            if (otherShapes != null)
                for (int j = 0; j < otherShapes.Length; j++)
                {
                    int k = j * 3;
                    otherShapes[j] = new Core(otherMatrix.MultiplyPoint3x4(otherVertices[otherTriangles[k]]),
                        otherMatrix.MultiplyPoint3x4(otherVertices[otherTriangles[k + 1]]),
                        otherMatrix.MultiplyPoint3x4(otherVertices[otherTriangles[k + 2]]));
                    otherBounds[j] = ShapeBounds(otherShapes[j]);
                }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Core triangle = new Core(matrix.MultiplyPoint3x4(vertices[triangles[i]]),
                    matrix.MultiplyPoint3x4(vertices[triangles[i + 1]]), matrix.MultiplyPoint3x4(vertices[triangles[i + 2]]));
                Bounds triangleBounds = ShapeBounds(triangle);
                if (BoundsDistanceSquared(triangleBounds, targetBounds) > best * best) continue;
                if (otherTriangles == null)
                {
                    Vector3 p, q;
                    float gap = convex
                        ? ProjectionGap(point => triangle.Closest(point), point => TargetClosest(other, point), triangle.Center, out p, out q)
                        : ShapeGap(triangle, target, out p, out q);
                    if (gap >= best) continue;
                    best = gap; pa = p; pb = q;
                }
                else
                {
                    for (int j = 0; j < otherShapes.Length; j++)
                    {
                        if (BoundsDistanceSquared(triangleBounds, otherBounds[j]) > best * best) continue;
                        float gap = ShapeGap(triangle, otherShapes[j], out var p, out var q);
                        if (gap >= best) continue;
                        best = gap; pa = p; pb = q;
                        if (best < 1e-7f) return 0;
                    }
                }
                if (best < 1e-7f) return 0;
            }
            return best;
        }

        static Bounds ShapeBounds(Core shape)
        {
            var bounds = new Bounds(shape.Center, Vector3.zero);
            if (shape.Box)
                for (int edge = 0; edge < shape.EdgeCount; edge++)
                {
                    shape.Edge(edge, out var a, out var b);
                    bounds.Encapsulate(a); bounds.Encapsulate(b);
                }
            else
            {
                bounds.Encapsulate(shape.Start); bounds.Encapsulate(shape.End);
                bounds.Expand(shape.Radius * 2);
            }
            return bounds;
        }

        static float BoundsDistanceSquared(Bounds a, Bounds b)
        {
            float sum = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                float gap = Mathf.Max(0, Mathf.Max(a.min[axis] - b.max[axis], b.min[axis] - a.max[axis]));
                sum += gap * gap;
            }
            return sum;
        }

        static Vector3 TriangleClosest(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            // Degenerate imported triangles reduce to their edges.
            if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-20f)
            {
                Segments(p, p, a, b, out _, out var x);
                Segments(p, p, b, c, out _, out var y);
                Segments(p, p, c, a, out _, out var z);
                return (p - x).sqrMagnitude <= (p - y).sqrMagnitude
                    ? ((p - x).sqrMagnitude <= (p - z).sqrMagnitude ? x : z)
                    : ((p - y).sqrMagnitude <= (p - z).sqrMagnitude ? y : z);
            }
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float inv = 1 / (va + vb + vc);
            return a + ab * (vb * inv) + ac * (vc * inv);
        }
    }
}
