using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Points every object drawing a duplicate of the same geometry back at a single shared mesh. The
    /// fast Blender pipeline applies its decimate and normals modifiers per object, and those are baked
    /// per object on FBX export, so parts that were one part in CAD arrive as "Bearing", "Bearing.001",
    /// "Bearing.002" and so on. Collapsing them is what makes the instance-aware tools work again.
    /// </summary>
    /// <remarks>
    /// Nothing is written to the model asset. The duplicate meshes stay inside the FBX and only stop
    /// being referenced, which is enough for a build to leave them out and for undo to put every
    /// reference back. Dropping them from the asset itself would take an import-time pass, and would
    /// strand any prefab that still names one directly.
    /// </remarks>
    public sealed class ReInstanceMeshes : AuthoringTool
    {
        /// <summary>
        /// How far two positions may sit apart and still be the same point. Copies measured off these
        /// exports differ by around 3e-8m of FBX transform noise, so this leaves three orders of room
        /// while still telling apart parts that genuinely differ by a hundredth of a millimetre.
        /// </summary>
        const float PositionTolerance = 1e-5f;

        const float NormalTolerance = 1e-4f;
        const float UvTolerance = 1e-5f;

        /// <summary>Bounds round to this before they are used to bucket meshes for comparison.</summary>
        const float BoundsQuantum = 1e-4f;

        /// <summary>
        /// The cheap part of a mesh's identity. Meshes that disagree here cannot be copies, so this
        /// sorts them into buckets and the full comparison only ever runs inside one.
        /// </summary>
        readonly struct Key : IEquatable<Key>
        {
            readonly int _vertices, _indices, _submeshes;
            readonly Vector3Int _center, _size;

            public Key(Mesh mesh)
            {
                _vertices = mesh.vertexCount;
                _submeshes = mesh.subMeshCount;

                int indices = 0;
                for (int i = 0; i < mesh.subMeshCount; i++) indices += (int)mesh.GetIndexCount(i);
                _indices = indices;

                Bounds bounds = mesh.bounds;
                _center = Quantize(bounds.center);
                _size = Quantize(bounds.size);
            }

            static Vector3Int Quantize(Vector3 value)
            {
                return new Vector3Int(
                    Mathf.RoundToInt(value.x / BoundsQuantum),
                    Mathf.RoundToInt(value.y / BoundsQuantum),
                    Mathf.RoundToInt(value.z / BoundsQuantum));
            }

            public bool Equals(Key other)
            {
                return _vertices == other._vertices && _indices == other._indices &&
                       _submeshes == other._submeshes && _center == other._center && _size == other._size;
            }

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _vertices;
                    hash = hash * 397 ^ _indices;
                    hash = hash * 397 ^ _submeshes;
                    hash = hash * 397 ^ _center.GetHashCode();
                    hash = hash * 397 ^ _size.GetHashCode();
                    return hash;
                }
            }
        }

        /// <summary>A mesh's geometry, read once and held for the length of the run.</summary>
        sealed class Geometry
        {
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly Vector2[] Uv;
            public readonly int[][] Submeshes;
            public readonly MeshTopology[] Topologies;

            public Geometry(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Normals = mesh.normals;
                Uv = mesh.uv;

                Submeshes = new int[mesh.subMeshCount][];
                Topologies = new MeshTopology[mesh.subMeshCount];

                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    Submeshes[i] = mesh.GetIndices(i);
                    Topologies[i] = mesh.GetTopology(i);
                }
            }
        }

        /// <summary>One shape, and every distinct mesh asset found holding it.</summary>
        sealed class Group
        {
            public Geometry Sample;
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        public override ToolInfo Info => new ToolInfo("Mesh", "Re-Instance Meshes", 13,
            "Select the model root(s) holding the duplicated meshes.");

        [MenuItem("GameObject/Re-Instance Meshes", false, 45)]
        static void Menu() => Run<ReInstanceMeshes>();

        [MenuItem("GameObject/Re-Instance Meshes", true)]
        static bool Validate() => CanRun<ReInstanceMeshes>();

        public override bool IsAvailable() => Has<MeshFilter>(TargetScope.Roots, f => f.sharedMesh != null);

        protected internal override void Execute(ToolRun run)
        {
            Dictionary<Mesh, List<MeshFilter>> users = ReadSelection();
            if (users.Count == 0)
            {
                run.Fail(Info.EmptyMessage);
                return;
            }

            run.Considered = users.Count;

            int collapsed = 0;
            var geometry = new Dictionary<Mesh, Geometry>();

            foreach (Group group in Match(users.Keys, geometry))
            {
                if (group.Meshes.Count < 2) continue;

                Mesh canonical = Canonical(group.Meshes);

                foreach (Mesh duplicate in group.Meshes)
                {
                    if (duplicate == canonical) continue;

                    foreach (MeshFilter filter in users[duplicate])
                    {
                        Undo.RecordObject(filter, run.Name);
                        filter.sharedMesh = canonical;

                        // A filter still linked to its prefab needs the override recorded, or the change
                        // lives only in memory and is gone the next time the instance reloads.
                        if (PrefabUtility.IsPartOfPrefabInstance(filter))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);

                        run.Applied++;
                    }

                    collapsed++;
                }
            }

            run.Report(collapsed == 0
                ? $"the {users.Count} mesh(es) in the selection are already distinct; nothing to collapse."
                : $"repointed {run.Applied} filter(s); {users.Count} mesh(es) collapsed to " +
                  $"{users.Count - collapsed} unique.");
        }

        /// <summary>Every mesh the selection draws, with the filters drawing it.</summary>
        static Dictionary<Mesh, List<MeshFilter>> ReadSelection()
        {
            var users = new Dictionary<Mesh, List<MeshFilter>>();

            foreach (MeshFilter filter in SelectionSet.Targets<MeshFilter>(TargetScope.Roots))
            {
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || mesh.vertexCount == 0) continue;

                if (!users.TryGetValue(mesh, out List<MeshFilter> filters))
                    users[mesh] = filters = new List<MeshFilter>();

                filters.Add(filter);
            }

            return users;
        }

        /// <summary>Sorts the meshes into groups that hold the same geometry.</summary>
        static IEnumerable<Group> Match(IEnumerable<Mesh> meshes, Dictionary<Mesh, Geometry> geometry)
        {
            var buckets = new Dictionary<Key, List<Group>>();

            // Walked in name order rather than hierarchy order, so two runs over the same model group
            // the meshes the same way.
            foreach (Mesh mesh in meshes.OrderBy(m => m.name, StringComparer.Ordinal))
            {
                var key = new Key(mesh);
                if (!buckets.TryGetValue(key, out List<Group> groups))
                    buckets[key] = groups = new List<Group>();

                Geometry candidate = Read(mesh, geometry);
                Group match = groups.FirstOrDefault(g => Matches(g.Sample, candidate));

                if (match == null)
                {
                    match = new Group { Sample = candidate };
                    groups.Add(match);
                }

                match.Meshes.Add(mesh);
            }

            return buckets.Values.SelectMany(groups => groups);
        }

        static Geometry Read(Mesh mesh, Dictionary<Mesh, Geometry> cache)
        {
            if (!cache.TryGetValue(mesh, out Geometry read))
                cache[mesh] = read = new Geometry(mesh);

            return read;
        }

        /// <summary>Whether two meshes hold the same geometry, vertex for vertex and index for index.</summary>
        static bool Matches(Geometry a, Geometry b)
        {
            if (a.Submeshes.Length != b.Submeshes.Length) return false;

            for (int i = 0; i < a.Submeshes.Length; i++)
            {
                if (a.Topologies[i] != b.Topologies[i]) return false;
                if (a.Submeshes[i].Length != b.Submeshes[i].Length) return false;

                for (int j = 0; j < a.Submeshes[i].Length; j++)
                    if (a.Submeshes[i][j] != b.Submeshes[i][j]) return false;
            }

            if (!Near(a.Vertices, b.Vertices, PositionTolerance)) return false;
            if (!Near(a.Normals, b.Normals, NormalTolerance)) return false;
            if (a.Uv.Length != b.Uv.Length) return false;

            for (int i = 0; i < a.Uv.Length; i++)
                if (Mathf.Abs(a.Uv[i].x - b.Uv[i].x) > UvTolerance ||
                    Mathf.Abs(a.Uv[i].y - b.Uv[i].y) > UvTolerance) return false;

            return true;
        }

        static bool Near(Vector3[] a, Vector3[] b, float tolerance)
        {
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].x - b[i].x) > tolerance ||
                    Mathf.Abs(a[i].y - b[i].y) > tolerance ||
                    Mathf.Abs(a[i].z - b[i].z) > tolerance) return false;

            return true;
        }

        /// <summary>
        /// The mesh of a group that the others are pointed at. Blender numbers its copies "Bearing.001",
        /// so the unsuffixed name is the original and the one worth keeping; the rest of the ordering is
        /// only there to make the choice the same on every run.
        /// </summary>
        static Mesh Canonical(IEnumerable<Mesh> meshes)
        {
            return meshes
                .OrderBy(m => IsCopy(m.name, out int _) ? 1 : 0)
                .ThenBy(m =>
                {
                    IsCopy(m.name, out int number);
                    return number;
                })
                .ThenBy(m => m.name, StringComparer.Ordinal)
                .First();
        }

        /// <summary>Reads Blender's ".001" copy suffix off a name.</summary>
        static bool IsCopy(string name, out int number)
        {
            number = 0;
            if (name == null || name.Length < 4 || name[name.Length - 4] != '.') return false;

            return int.TryParse(name.Substring(name.Length - 3), out number);
        }
    }
}
