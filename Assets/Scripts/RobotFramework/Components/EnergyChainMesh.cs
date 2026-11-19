using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RobotFramework.Components
{
    /// <summary>Rectangular hollow links sampled at equal distances along a cable path.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class EnergyChainMesh : MonoBehaviour
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();
        private Vector3[] path;
        private float[] distances;
        private Mesh mesh;

        public void Clear()
        {
            if (mesh != null) mesh.Clear();
        }

        public void Rebuild(Vector3[] worldPath, Transform frame, Vector3 up,
            float width, float height, float linkLength, float gapFraction, Material material)
        {
            if (worldPath == null || worldPath.Length < 2)
            {
                Clear();
                return;
            }
            if (path == null || path.Length != worldPath.Length)
            {
                path = new Vector3[worldPath.Length];
                distances = new float[worldPath.Length];
            }
            distances[0] = 0f;
            for (int i = 0; i < path.Length; i++)
            {
                path[i] = frame.InverseTransformPoint(worldPath[i]);
                if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            }
            if (mesh == null)
            {
                mesh = new Mesh { name = "Generated Energy Chain", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                GetComponent<MeshFilter>().sharedMesh = mesh;
            }
            GetComponent<MeshRenderer>().sharedMaterial = material;
            vertices.Clear();
            normals.Clear();
            triangles.Clear();
            float total = distances[distances.Length - 1];
            if (total > 0.00001f)
            {
                int links = Mathf.Clamp(Mathf.CeilToInt(total / Mathf.Max(0.002f, linkLength)), 1, 8192);
                float pitch = total / links;
                float length = pitch * (1f - Mathf.Clamp(gapFraction, 0f, 0.35f));
                width = Mathf.Max(0.001f, width);
                height = Mathf.Max(0.001f, height);
                float wall = Mathf.Min(width, height) * 0.18f;
                Matrix4x4 intoMesh = transform.worldToLocalMatrix * frame.localToWorldMatrix;
                int cursor = 1;
                for (int i = 0; i < links; i++)
                {
                    Vector3 a = Sample(i * pitch, ref cursor);
                    Vector3 b = Sample((i + 1) * pitch, ref cursor);
                    Vector3 tangent = (b - a).normalized;
                    if (tangent.sqrMagnitude < 0.5f) continue;
                    Vector3 side = Vector3.Cross(up.normalized, tangent).normalized;
                    if (side.sqrMagnitude < 0.5f)
                        side = Vector3.Cross(Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right, tangent).normalized;
                    Vector3 vertical = Vector3.Cross(tangent, side).normalized;
                    Vector3 center = (a + b) * 0.5f;
                    // Two side plates and upper/lower crossbars form an open rectangular link.
                    Box(center - side * ((width - wall) * 0.5f), side, vertical, tangent, wall, height, length, intoMesh);
                    Box(center + side * ((width - wall) * 0.5f), side, vertical, tangent, wall, height, length, intoMesh);
                    Box(center + vertical * ((height - wall) * 0.5f), side, vertical, tangent, width - 2f * wall, wall, length, intoMesh);
                    Box(center - vertical * ((height - wall) * 0.5f), side, vertical, tangent, width - 2f * wall, wall, length, intoMesh);
                }
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private Vector3 Sample(float distance, ref int cursor)
        {
            while (cursor < distances.Length - 1 && distances[cursor] < distance) cursor++;
            float span = distances[cursor] - distances[cursor - 1];
            return Vector3.Lerp(path[cursor - 1], path[cursor], span > 0.000001f ? (distance - distances[cursor - 1]) / span : 0f);
        }

        private void Box(Vector3 center, Vector3 side, Vector3 up, Vector3 forward,
            float width, float height, float length, Matrix4x4 matrix)
        {
            Vector3 x = side * (width * 0.5f), y = up * (height * 0.5f), z = forward * (length * 0.5f);
            Quad(center + x - y - z, center + x + y - z, center + x + y + z, center + x - y + z, matrix);
            Quad(center - x - y + z, center - x + y + z, center - x + y - z, center - x - y - z, matrix);
            Quad(center - x + y - z, center - x + y + z, center + x + y + z, center + x + y - z, matrix);
            Quad(center - x - y + z, center - x - y - z, center + x - y - z, center + x - y + z, matrix);
            Quad(center - x - y + z, center + x - y + z, center + x + y + z, center - x + y + z, matrix);
            Quad(center + x - y - z, center - x - y - z, center - x + y - z, center + x + y - z, matrix);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Matrix4x4 matrix)
        {
            a = matrix.MultiplyPoint3x4(a); b = matrix.MultiplyPoint3x4(b);
            c = matrix.MultiplyPoint3x4(c); d = matrix.MultiplyPoint3x4(d);
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }

        private void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
