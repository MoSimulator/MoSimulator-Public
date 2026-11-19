using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Reads a mesh as the built-in primitive that best matches its silhouette, fitted to an oriented
    /// box. Three tools stand on this — the shape replacer, the collider guesser and the roller
    /// guesser — and they differ only in what they emit, so they all agree on what shape a part is.
    /// </summary>
    public static class ShapeFit
    {
        /// <summary>Roughly how many surface samples are scored against the candidate shapes.</summary>
        const int TargetSamples = 3000;

        /// <summary>How many of those samples the orientation search runs on; it visits many angles.</summary>
        const int SearchSamples = 700;

        /// <summary>The winning shape and the oriented box it was fitted to, in the mesh's local space.</summary>
        public struct Fit
        {
            public PrimitiveType Primitive;

            /// <summary>Index of the fitted axis a cylinder or capsule runs along: 0 = X, 1 = Y, 2 = Z.</summary>
            public int Axis;

            public Quaternion Rotation;
            public Vector3 Center;
            public Vector3 Extents;

            /// <summary>The top few candidates and how well each scored, for the tool's log line.</summary>
            public string Report;
        }

        /// <summary>The mesh reduced to an oriented box: its own axes, centre and half sizes.</summary>
        struct Frame
        {
            public Quaternion Rotation;
            public Vector3 Center;
            public Vector3 Extents;
        }

        struct Candidate
        {
            public PrimitiveType Primitive;
            public int Axis;
            public float Error;
        }

        /// <summary>Fits the primitive that best matches a mesh.</summary>
        public static bool TryFit(Mesh mesh, out Fit fit)
        {
            fit = default;
            if (mesh == null) return false;

            Vector3[] samples = SampleSurface(mesh, out Vector3[] normals);
            if (samples.Length == 0) return false;

            Frame frame = FitFrame(samples, normals);
            Candidate best = Classify(mesh, samples, frame, out string report);

            fit = new Fit
            {
                Primitive = best.Primitive,
                Axis = best.Axis,
                Rotation = frame.Rotation,
                Center = frame.Center,
                Extents = frame.Extents,
                Report = report
            };

            return true;
        }

        /// <summary>
        /// Size of the fitted box in world units, measured along its own (rotated) axes. A non-uniform
        /// scale shears a rotated box rather than resizing it, so this is the closest box-shaped answer.
        /// </summary>
        public static Vector3 WorldSize(Fit fit, Vector3 lossyScale)
        {
            Vector3 size = fit.Extents * 2f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = fit.Rotation * Unit(i);
                size[i] *= Vector3.Scale(lossyScale, axis).magnitude;
            }

            return size;
        }

        /// <summary>The unit vector along one of the three axes, by index.</summary>
        public static Vector3 Unit(int axis)
        {
            return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        }

        /// <summary>True for the shapes that have a single axis they are symmetric about.</summary>
        public static bool IsAxial(PrimitiveType primitive)
        {
            return primitive == PrimitiveType.Cylinder || primitive == PrimitiveType.Capsule;
        }

        static string Describe(Candidate candidate)
        {
            string axis = IsAxial(candidate.Primitive) ? " along fitted axis " + "XYZ"[candidate.Axis] : string.Empty;
            return $"{candidate.Primitive}{axis} ({candidate.Error:P1} deviation)";
        }

        /// <summary>
        /// Points spread over the mesh surface in proportion to triangle area. Raw vertices would be
        /// scored instead, but tessellation is uneven — a lathe-turned face carries far more vertices
        /// than a flat one — and that bias is what makes round parts read as boxes.
        /// </summary>
        static Vector3[] SampleSurface(Mesh mesh, out Vector3[] faceNormals)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            faceNormals = new Vector3[0];
            if (vertices.Length == 0) return vertices;
            if (triangles.Length < 3) return vertices;

            float total = 0f;
            var areas = new float[triangles.Length / 3];
            for (int t = 0; t < areas.Length; t++)
            {
                Vector3 a = vertices[triangles[t * 3]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                areas[t] = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                total += areas[t];
            }

            if (total <= 0f) return vertices;

            // Fixed barycentric offsets keep the result deterministic across runs.
            var weights = new[]
            {
                new Vector3(1f / 3f, 1f / 3f, 1f / 3f),
                new Vector3(0.6f, 0.2f, 0.2f),
                new Vector3(0.2f, 0.6f, 0.2f),
                new Vector3(0.2f, 0.2f, 0.6f)
            };

            var samples = new List<Vector3>(TargetSamples + areas.Length);
            var normals = new List<Vector3>(TargetSamples + areas.Length);
            for (int t = 0; t < areas.Length; t++)
            {
                Vector3 a = vertices[triangles[t * 3]];
                Vector3 b = vertices[triangles[t * 3 + 1]];
                Vector3 c = vertices[triangles[t * 3 + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                int count = Mathf.Clamp(Mathf.RoundToInt(areas[t] / total * TargetSamples), 1, 64);
                for (int s = 0; s < count; s++)
                {
                    Vector3 w = weights[s % weights.Length];
                    float spread = 1f + s / weights.Length;
                    w = Vector3.Lerp(w, new Vector3(1f / 3f, 1f / 3f, 1f / 3f), 1f - 1f / spread);
                    samples.Add(a * w.x + b * w.y + c * w.z);
                    normals.Add(normal);
                }
            }

            faceNormals = normals.ToArray();
            return samples.ToArray();
        }

        /// <summary>
        /// Smallest oriented box that contains the samples, searched over candidate orientations.
        /// </summary>
        /// <remarks>
        /// Principal component analysis alone is not enough. Its axes are only well defined when the
        /// three eigenvalues differ, so a square or cubic part — where two or three of them are equal —
        /// comes out at an arbitrary angle, and the box that results is the diagonal one: far larger
        /// than the part and useless as a collider. The minimum-volume box of a flat-faced solid always
        /// lies flush with one of its faces, so the dominant face normals are searched as well, and the
        /// box is spun around each candidate axis to find the tightest cross section.
        /// </remarks>
        static Frame FitFrame(Vector3[] samples, Vector3[] normals)
        {
            // Everything below runs on points normalised to roughly unit size. A part authored in
            // millimetres and blown up by a 100x parent scale carries local coordinates in the
            // thousandths, where fixed tolerances would swallow the whole shape; against a unit-sized
            // copy every threshold means the same thing whatever the model's scale.
            float radius = Normalize(samples, out Vector3[] unit);

            // The search visits thousands of orientations, so it runs on a thinned set of points; the
            // winning box is then measured against every sample so nothing pokes out of it.
            Vector3[] search = Decimate(unit, SearchSamples);

            Quaternion best = Quaternion.identity;
            float bestVolume = float.MaxValue;

            foreach (Vector3 axis in CandidateAxes(unit, normals))
            {
                Quaternion aligned = Quaternion.FromToRotation(Vector3.up, axis);
                Quaternion refined = RefineSpin(search, aligned, out float volume);

                if (volume >= bestVolume) continue;
                bestVolume = volume;
                best = refined;
            }

            Bounds bounds = Measure(samples, best);
            return new Frame
            {
                Rotation = best,
                Center = bounds.center,
                Extents = Vector3.Max(bounds.extents, Vector3.one * radius * 1e-6f)
            };
        }

        /// <summary>
        /// Copies the samples centred on their mean and scaled to unit radius, returning that radius so
        /// results can be read back in the mesh's own units.
        /// </summary>
        static float Normalize(Vector3[] samples, out Vector3[] unit)
        {
            Vector3 mean = Vector3.zero;
            foreach (Vector3 s in samples) mean += s;
            mean /= samples.Length;

            float radius = 0f;
            foreach (Vector3 s in samples) radius = Mathf.Max(radius, (s - mean).magnitude);
            radius = Mathf.Max(radius, 1e-12f);

            unit = new Vector3[samples.Length];
            for (int i = 0; i < samples.Length; i++) unit[i] = (samples[i] - mean) / radius;
            return radius;
        }

        /// <summary>
        /// Orientations worth testing: the principal axes, the mesh's own axes, and the normals of the
        /// largest flat faces, which is where a box-shaped part's true alignment shows up.
        /// </summary>
        static List<Vector3> CandidateAxes(Vector3[] samples, Vector3[] normals)
        {
            var axes = new List<Vector3> { Vector3.right, Vector3.up, Vector3.forward };

            Vector3 mean = Vector3.zero;
            foreach (Vector3 s in samples) mean += s;
            mean /= samples.Length;

            var covariance = new float[3, 3];
            foreach (Vector3 s in samples)
            {
                Vector3 d = s - mean;
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    covariance[i, j] += d[i] * d[j];
            }

            axes.AddRange(Eigenvectors(covariance));

            // Normals are already area weighted by the sampler, so counting them ranks faces by area.
            var buckets = new Dictionary<Vector3Int, (Vector3 Direction, int Count)>();
            foreach (Vector3 n in normals)
            {
                if (n.sqrMagnitude < 1e-8f) continue;

                // Opposite faces of a box describe the same orientation, so fold the hemisphere over.
                Vector3 d = n.normalized;
                if (d.x + d.y + d.z < 0f || (Mathf.Approximately(d.x + d.y + d.z, 0f) && d.x < 0f)) d = -d;

                var key = new Vector3Int(
                    Mathf.RoundToInt(d.x * 24f),
                    Mathf.RoundToInt(d.y * 24f),
                    Mathf.RoundToInt(d.z * 24f));

                buckets[key] = buckets.TryGetValue(key, out var bucket)
                    ? (bucket.Direction + d, bucket.Count + 1)
                    : (d, 1);
            }

            axes.AddRange(buckets.Values
                .OrderByDescending(b => b.Count)
                .Take(8)
                .Select(b => b.Direction.normalized));

            return axes;
        }

        /// <summary>
        /// Spins the box around its own up axis to the angle that gives the smallest cross section,
        /// coarsely over the 90 degrees of symmetry and then finely around the winner.
        /// </summary>
        static Quaternion RefineSpin(Vector3[] samples, Quaternion aligned, out float volume)
        {
            float bestAngle = 0f;
            volume = float.MaxValue;

            for (float angle = 0f; angle < 90f; angle += 1.5f)
                Consider(angle, ref bestAngle, ref volume);

            float coarse = bestAngle;
            for (float angle = coarse - 1.5f; angle <= coarse + 1.5f; angle += 0.1f)
                Consider(angle, ref bestAngle, ref volume);

            return aligned * Quaternion.Euler(0f, bestAngle, 0f);

            void Consider(float angle, ref float winner, ref float smallest)
            {
                Bounds bounds = Measure(samples, aligned * Quaternion.Euler(0f, angle, 0f));
                Vector3 size = bounds.size;
                float candidate = Mathf.Max(size.x, 1e-6f) * Mathf.Max(size.y, 1e-6f) * Mathf.Max(size.z, 1e-6f);

                if (candidate >= smallest) return;
                smallest = candidate;
                winner = angle;
            }
        }

        /// <summary>Evenly thinned copy of the samples, or the samples themselves when already small.</summary>
        static Vector3[] Decimate(Vector3[] samples, int limit)
        {
            if (samples.Length <= limit) return samples;

            int stride = Mathf.CeilToInt(samples.Length / (float)limit);
            var thinned = new List<Vector3>(limit + 1);
            for (int i = 0; i < samples.Length; i += stride) thinned.Add(samples[i]);
            return thinned.ToArray();
        }

        /// <summary>Bounds of the samples as seen from the given orientation.</summary>
        static Bounds Measure(Vector3[] samples, Quaternion rotation)
        {
            Quaternion inverse = Quaternion.Inverse(rotation);
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            foreach (Vector3 s in samples)
            {
                Vector3 local = inverse * s;
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>Eigenvectors of a symmetric 3x3 matrix by cyclic Jacobi rotation, largest spread first.</summary>
        static Vector3[] Eigenvectors(float[,] m)
        {
            var v = new float[3, 3] { { 1f, 0f, 0f }, { 0f, 1f, 0f }, { 0f, 0f, 1f } };

            for (int sweep = 0; sweep < 24; sweep++)
            {
                float off = Mathf.Abs(m[0, 1]) + Mathf.Abs(m[0, 2]) + Mathf.Abs(m[1, 2]);
                if (off < 1e-9f) break;

                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Mathf.Abs(m[p, q]) < 1e-12f) continue;

                    float theta = (m[q, q] - m[p, p]) / (2f * m[p, q]);
                    float t = Mathf.Sign(theta) / (Mathf.Abs(theta) + Mathf.Sqrt(theta * theta + 1f));
                    float c = 1f / Mathf.Sqrt(t * t + 1f);
                    float s = t * c;

                    for (int k = 0; k < 3; k++)
                    {
                        float mkp = m[k, p];
                        float mkq = m[k, q];
                        m[k, p] = c * mkp - s * mkq;
                        m[k, q] = s * mkp + c * mkq;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        float mpk = m[p, k];
                        float mqk = m[q, k];
                        m[p, k] = c * mpk - s * mqk;
                        m[q, k] = s * mpk + c * mqk;

                        float vkp = v[k, p];
                        float vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }

            var ordered = new[] { 0, 1, 2 }
                .OrderByDescending(i => m[i, i])
                .Select(i => new Vector3(v[0, i], v[1, i], v[2, i]).normalized)
                .ToArray();

            // Re-orthogonalise into a right-handed frame; Jacobi output can drift slightly.
            Vector3 forward = ordered[0];
            Vector3 up = Vector3.Cross(forward, ordered[1]).sqrMagnitude > 1e-8f ? ordered[1] : Vector3.up;
            up = (up - Vector3.Project(up, forward)).normalized;
            return new[] { Vector3.Cross(up, forward), up, forward };
        }

        /// <summary>
        /// Scores every candidate primitive fitted to the frame by the average distance from the surface
        /// samples to that primitive. A circular outline with broad flat faces identifies cylinders
        /// even when teeth, bores or raised hubs make their surface distances favour an ellipsoid.
        /// </summary>
        static Candidate Classify(Mesh mesh, Vector3[] samples, Frame frame, out string report)
        {
            Quaternion inverse = Quaternion.Inverse(frame.Rotation);

            // Scored against a unit-sized copy of the fit, so the tolerances inside the distance
            // functions stay meaningful for a part whose local units are tiny under a large parent scale.
            float scale = Mathf.Max(frame.Extents.magnitude, 1e-12f);
            Vector3 extents = frame.Extents / scale;

            var candidates = new[]
            {
                new Candidate { Primitive = PrimitiveType.Cube, Axis = 1 },
                new Candidate { Primitive = PrimitiveType.Sphere, Axis = 1 },
                new Candidate { Primitive = PrimitiveType.Cylinder, Axis = 0 },
                new Candidate { Primitive = PrimitiveType.Cylinder, Axis = 1 },
                new Candidate { Primitive = PrimitiveType.Cylinder, Axis = 2 },
                new Candidate { Primitive = PrimitiveType.Capsule, Axis = 0 },
                new Candidate { Primitive = PrimitiveType.Capsule, Axis = 1 },
                new Candidate { Primitive = PrimitiveType.Capsule, Axis = 2 }
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                float total = 0f;
                foreach (Vector3 s in samples)
                    total += Mathf.Abs(Distance(candidates[i], (inverse * s - frame.Center) / scale, extents));

                candidates[i].Error = total / samples.Length;
            }

            Candidate[] ranked = candidates.OrderBy(c => c.Error).ToArray();
            report = string.Join(", ", ranked.Take(3).Select(Describe));
            foreach (Candidate candidate in ranked)
            {
                if (candidate.Primitive != PrimitiveType.Cylinder) continue;
                if (!HasCylinderProfile(mesh, frame, candidate.Axis)) continue;
                report = $"{Describe(candidate)} (circular outline and flat faces); distance ranking: {report}";
                return candidate;
            }
            return ranked[0];
        }

        /// <summary>
        /// Tests the outer outline rather than all surface radii, so a bore and gear teeth do not
        /// obscure the round body. Flat faces on both sides distinguish it from a sphere or capsule.
        /// </summary>
        static bool HasCylinderProfile(Mesh mesh, Frame frame, int axis)
        {
            const int sectors = 16;
            Quaternion inverse = Quaternion.Inverse(frame.Rotation);
            float scale = Mathf.Max(frame.Extents.magnitude, 1e-12f);
            Vector3[] vertices = mesh.vertices.Select(v => (inverse * v - frame.Center) / scale).ToArray();
            Bounds bounds = Measure(vertices, Quaternion.identity);
            Vector3 extents = bounds.extents;
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            if (Mathf.Min(extents[a], extents[b]) <= 1e-6f || extents[axis] <= 1e-6f) return false;
            if (Mathf.Min(extents[a], extents[b]) / Mathf.Max(extents[a], extents[b]) < .85f) return false;

            var outline = new float[sectors];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] -= bounds.center;
                float x = vertices[i][a] / extents[a], y = vertices[i][b] / extents[b];
                float radius = Mathf.Sqrt(x * x + y * y);
                // Square corners extend beyond the circular outline, including on subdivided boxes.
                if (radius > 1.1f) return false;
                int sector = Mathf.Min(sectors - 1,
                    (int)((Mathf.Atan2(y, x) + Mathf.PI) / (2f * Mathf.PI) * sectors));
                outline[sector] = Mathf.Max(outline[sector], radius);
            }
            // Require a rim all the way around, rather than a few protruding points.
            if (outline.Any(radius => radius < .85f)) return false;

            float totalArea = 0f, positiveArea = 0f, negativeArea = 0f;
            Vector3 inverseExtents = new Vector3(1f / extents.x, 1f / extents.y, 1f / extents.z);
            int[] triangles = mesh.triangles;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Vector3 p = vertices[triangles[t]], q = vertices[triangles[t + 1]], r = vertices[triangles[t + 2]];
                Vector3 normal = Vector3.Cross(q - p, r - p);
                float area = normal.magnitude;
                totalArea += area;
                // Judge flatness after removing the aspect ratio: the sloping surface of a squashed
                // sphere can otherwise have almost axial normals across most of its surface.
                Vector3 shapeNormal = Vector3.Cross(Vector3.Scale(q - p, inverseExtents),
                    Vector3.Scale(r - p, inverseExtents));
                if (area <= 0f || Mathf.Abs(shapeNormal[axis]) < .995f * shapeNormal.magnitude) continue;
                float along = (p[axis] + q[axis] + r[axis]) / 3f;
                // Faces can be inset from the ends when a gear has a raised central hub.
                if (along > extents[axis] * .1f) positiveArea += area;
                if (along < -extents[axis] * .1f) negativeArea += area;
            }
            // Use triangle area, not vertex counts: tiny teeth can be much more densely tessellated
            // than the broad faces, and must not drown out their evidence.
            return totalArea > 0f && Mathf.Min(positiveArea, negativeArea) >= totalArea * .08f;
        }

        /// <summary>Signed distance from a point to the candidate primitive fitted to the given extents.</summary>
        static float Distance(Candidate candidate, Vector3 point, Vector3 extents)
        {
            switch (candidate.Primitive)
            {
                case PrimitiveType.Sphere:
                    return EllipsoidDistance(point, extents);
                case PrimitiveType.Cylinder:
                    return CylinderDistance(point, extents, candidate.Axis);
                case PrimitiveType.Capsule:
                    return CapsuleDistance(point, extents, candidate.Axis);
                default:
                    return BoxDistance(point, extents);
            }
        }

        static float BoxDistance(Vector3 point, Vector3 extents)
        {
            Vector3 q = new Vector3(
                Mathf.Abs(point.x) - extents.x,
                Mathf.Abs(point.y) - extents.y,
                Mathf.Abs(point.z) - extents.z);

            float outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
            return outside + inside;
        }

        /// <summary>
        /// Approximate distance to an ellipsoid. The gradient-scaled form keeps elongated ellipsoids from
        /// scoring artificially well, which a plain radial ratio does.
        /// </summary>
        static float EllipsoidDistance(Vector3 point, Vector3 extents)
        {
            Vector3 e = Max(extents, 1e-6f);
            Vector3 n = new Vector3(point.x / e.x, point.y / e.y, point.z / e.z);
            Vector3 g = new Vector3(n.x / e.x, n.y / e.y, n.z / e.z);

            float k0 = n.magnitude;
            float k1 = Mathf.Max(g.magnitude, 1e-9f);
            return k0 * (k0 - 1f) / k1;
        }

        static float CylinderDistance(Vector3 point, Vector3 extents, int axis)
        {
            Split(point, extents, axis, out float along, out float radial, out float halfHeight, out float radius);

            float dr = radial - radius;
            float dh = Mathf.Abs(along) - halfHeight;
            float outside = new Vector2(Mathf.Max(dr, 0f), Mathf.Max(dh, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(dr, dh), 0f);
        }

        static float CapsuleDistance(Vector3 point, Vector3 extents, int axis)
        {
            Split(point, extents, axis, out float along, out float radial, out float halfHeight, out float radius);

            float spine = Mathf.Max(0f, halfHeight - radius);
            float offAxis = Mathf.Max(0f, Mathf.Abs(along) - spine);
            return new Vector2(radial, offAxis).magnitude - radius;
        }

        /// <summary>
        /// Splits a point into its distance along the chosen axis and its distance from that axis, with
        /// the fitted radius taken from the mean of the two remaining extents so an oval cross section
        /// is not judged against its widest span alone.
        /// </summary>
        static void Split(Vector3 point, Vector3 extents, int axis,
            out float along, out float radial, out float halfHeight, out float radius)
        {
            int a = (axis + 1) % 3;
            int b = (axis + 2) % 3;

            along = point[axis];
            radial = new Vector2(point[a], point[b]).magnitude;
            halfHeight = Mathf.Max(extents[axis], 1e-6f);
            radius = Mathf.Max((extents[a] + extents[b]) * 0.5f, 1e-6f);
        }

        static Vector3 Max(Vector3 value, float floor)
        {
            return new Vector3(Mathf.Max(value.x, floor), Mathf.Max(value.y, floor), Mathf.Max(value.z, floor));
        }
    }
}
