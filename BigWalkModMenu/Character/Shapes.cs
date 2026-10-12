using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWalk.ModMenu.Character
{
    /// <summary>Simple meshes built in code (no asset bundle needed). All of them are cached.</summary>
    public static class Shapes
    {
        private static readonly Dictionary<string, Mesh> _cache = new Dictionary<string, Mesh>();

        public static Mesh Sphere => Get("sphere", () => BuildSphere(18, 12, 1f));
        public static Mesh Hemisphere => Get("hemisphere", () => BuildSphere(18, 7, 0.5f));

        /// <summary>Axis along +Y, base at y = 0, top at y = 1. A top radius of 0 gives a cone.</summary>
        public static Mesh Cylinder(float bottomRadius, float topRadius)
        {
            return Get($"cyl{bottomRadius:0.###}_{topRadius:0.###}", () => BuildCylinder(bottomRadius, topRadius, 20));
        }

        /// <summary>Ring in the XZ plane (axis +Y), ring radius 1.</summary>
        public static Mesh Torus(float tubeRadius)
        {
            return Get($"torus{tubeRadius:0.###}", () => BuildTorus(tubeRadius, 24, 8));
        }

        /// <summary>A twenty-sided die with flat faces, corners at radius 1. One face looks towards -X (where
        /// the nose is) and the face above it, tilted up like a forehead, is the 20.</summary>
        public static Mesh D20 => Get("d20", () => BuildD20(false));

        /// <summary>The numbers 1 to 20 of <see cref="D20"/>, lying just above its faces.</summary>
        public static Mesh D20Numbers => Get("d20numbers", () => BuildD20(true));

        /// <summary>Like <see cref="Torus"/> with more faces, for a ring that is the whole shape.</summary>
        public static Mesh FineTorus(float tubeRadius)
        {
            return Get($"finetorus{tubeRadius:0.###}", () => BuildTorus(tubeRadius, 40, 14));
        }

        /// <summary>A box with flat faces, corners at (±1, ±1, ±1).</summary>
        public static Mesh Box => Get("box", BuildBox);

        /// <summary>Radius 1 around +Y: a cylinder of the given half height with half a sphere on each end.</summary>
        public static Mesh Capsule(float halfHeight)
        {
            return Get($"capsule{halfHeight:0.###}", () =>
            {
                var profile = new List<float>();
                for (int i = 0; i <= 8; i++)
                {
                    float a = MathF.PI / 2f * i / 8f;
                    profile.Add(MathF.Sin(a)); profile.Add(halfHeight + MathF.Cos(a));
                }
                for (int i = 0; i <= 8; i++)
                {
                    float a = MathF.PI / 2f * i / 8f;
                    profile.Add(MathF.Cos(a)); profile.Add(-halfHeight - MathF.Sin(a));
                }
                return BuildLathe(profile, 24, 0, 0f);
            });
        }

        /// <summary>A cylinder of radius 1 from y = -1 to y = 1 with rounded edges.</summary>
        public static Mesh RoundedCylinder(float corner)
        {
            return Get($"rounded{corner:0.###}", () =>
            {
                var profile = new List<float> { 0f, 1f };
                for (int i = 0; i <= 5; i++)
                {
                    float a = MathF.PI / 2f * i / 5f;
                    profile.Add(1f - corner + corner * MathF.Sin(a)); profile.Add(1f - corner + corner * MathF.Cos(a));
                }
                for (int i = 0; i <= 5; i++)
                {
                    float a = MathF.PI / 2f * i / 5f;
                    profile.Add(1f - corner + corner * MathF.Cos(a)); profile.Add(-(1f - corner) - corner * MathF.Sin(a));
                }
                profile.Add(0f); profile.Add(-1f);
                return BuildLathe(profile, 24, 0, 0f);
            });
        }

        /// <summary>A sphere of radius 1 whose upper half is stretched to the given height.</summary>
        public static Mesh Egg(float top)
        {
            return Get($"egg{top:0.###}", () =>
            {
                var profile = new List<float>();
                for (int i = 0; i <= 14; i++)
                {
                    float a = MathF.PI * i / 14f, y = MathF.Cos(a);
                    profile.Add(MathF.Sin(a)); profile.Add(y > 0f ? y * top : y);
                }
                return BuildLathe(profile, 24, 0, 0f);
            });
        }

        /// <summary>A sphere of radius 1 with grooves from top to bottom, as deep as the given part of the radius.</summary>
        public static Mesh Ribbed(int ribs, float depth)
        {
            return Get($"ribbed{ribs}_{depth:0.###}", () =>
            {
                var profile = new List<float>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = MathF.PI * i / 12f;
                    profile.Add(MathF.Sin(a)); profile.Add(MathF.Cos(a));
                }
                return BuildLathe(profile, ribs * 4, ribs, depth);
            });
        }

        /// <summary>A die with 6, 8 or 12 flat faces, corners at radius 1, one face towards -X (the 20-sided one is <see cref="D20"/>).</summary>
        public static Mesh Die(int sides) => Get("die" + sides, () => BuildDie(sides, false));

        /// <summary>The numbers of <see cref="Die"/>, lying just above its faces.</summary>
        public static Mesh DieNumbers(int sides) => Get("die" + sides + "numbers", () => BuildDie(sides, true));

        /// <summary>Flat pieces lying on a surface, such as the eyes and mouth drawn on a head. Each piece is a
        /// convex outline z0, y0, z1, y1, ... as seen from the front; 'surface' gives the point for (z, y).</summary>
        public static Mesh Decals(string key, Func<float, float, Vector3> surface, List<float[]> pieces)
        {
            return Get("decals_" + key, () =>
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                foreach (float[] piece in pieces)
                {
                    Vector3 first = surface(piece[0], piece[1]);
                    for (int i = 2; i + 3 < piece.Length; i += 2)
                    {
                        Vector3 b = surface(piece[i], piece[i + 1]), c = surface(piece[i + 2], piece[i + 3]);
                        AddFlat(verts, tris, first + b + c, first, b, c, c);
                    }
                }
                return Finish(verts, tris);
            });
        }

        private static Mesh Get(string key, Func<Mesh> build)
        {
            if (_cache.TryGetValue(key, out Mesh m) && Game.Alive(m)) return m;
            m = build();
            m.name = "bwmm_" + key;
            m.hideFlags = HideFlags.HideAndDontSave;
            _cache[key] = m;
            return m;
        }

        private static Mesh Finish(List<Vector3> verts, List<int> tris)
        {
            // Smooth normals from the faces around each vertex.
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                normals[tris[i]] += n;
                normals[tris[i + 1]] += n;
                normals[tris[i + 2]] += n;
            }
            for (int i = 0; i < normals.Length; i++)
            {
                float len = normals[i].magnitude;
                normals[i] = len > 1e-8f ? normals[i] / len : Vector3.up;
            }

            var mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.normals = normals;
            mesh.uv = new Vector2[verts.Count];
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // fraction = 1 for a full sphere, 0.5 for the upper half.
        private static Mesh BuildSphere(int segments, int rings, float fraction)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float polar = MathF.PI * fraction * r / rings; // 0 at the top
                float y = MathF.Cos(polar), ring = MathF.Sin(polar);
                for (int s = 0; s <= segments; s++)
                {
                    float az = 2f * MathF.PI * s / segments;
                    verts.Add(new Vector3(ring * MathF.Cos(az), y, ring * MathF.Sin(az)));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
            if (fraction < 0.99f)
            {
                // Close the open bottom.
                int centre = verts.Count;
                float polar = MathF.PI * fraction;
                float y = MathF.Cos(polar), ring = MathF.Sin(polar);
                verts.Add(new Vector3(0f, y, 0f));
                int first = verts.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float az = 2f * MathF.PI * s / segments;
                    verts.Add(new Vector3(ring * MathF.Cos(az), y, ring * MathF.Sin(az)));
                }
                for (int s = 0; s < segments; s++)
                {
                    tris.Add(centre); tris.Add(first + s); tris.Add(first + s + 1);
                }
            }
            return Finish(verts, tris);
        }

        private static Mesh BuildCylinder(float bottom, float top, int segments)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // Side.
            for (int s = 0; s <= segments; s++)
            {
                float az = 2f * MathF.PI * s / segments;
                float cx = MathF.Cos(az), cz = MathF.Sin(az);
                verts.Add(new Vector3(bottom * cx, 0f, bottom * cz));
                verts.Add(new Vector3(top * cx, 1f, top * cz));
            }
            for (int s = 0; s < segments; s++)
            {
                int a = s * 2, b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            // Caps, with their own vertices so the edge stays sharp.
            AddCap(verts, tris, bottom, 0f, segments, false);
            if (top > 1e-4f) AddCap(verts, tris, top, 1f, segments, true);
            return Finish(verts, tris);
        }

        private static void AddCap(List<Vector3> verts, List<int> tris, float radius, float y, int segments, bool up)
        {
            int centre = verts.Count;
            verts.Add(new Vector3(0f, y, 0f));
            int first = verts.Count;
            for (int s = 0; s <= segments; s++)
            {
                float az = 2f * MathF.PI * s / segments;
                verts.Add(new Vector3(radius * MathF.Cos(az), y, radius * MathF.Sin(az)));
            }
            for (int s = 0; s < segments; s++)
            {
                if (up) { tris.Add(centre); tris.Add(first + s + 1); tris.Add(first + s); }
                else { tris.Add(centre); tris.Add(first + s); tris.Add(first + s + 1); }
            }
        }

        private static Mesh BuildTorus(float tube, int segments, int sides)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s <= segments; s++)
            {
                float az = 2f * MathF.PI * s / segments;
                float cx = MathF.Cos(az), cz = MathF.Sin(az);
                for (int t = 0; t <= sides; t++)
                {
                    float a = 2f * MathF.PI * t / sides;
                    float r = 1f + tube * MathF.Cos(a);
                    verts.Add(new Vector3(r * cx, tube * MathF.Sin(a), r * cz));
                }
            }
            int row = sides + 1;
            for (int s = 0; s < segments; s++)
            {
                for (int t = 0; t < sides; t++)
                {
                    int a = s * row + t, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
            return Finish(verts, tris);
        }

        // Turns an outline around +Y. The outline is radius, y, radius, y, ... from the top down; a point given
        // twice makes a sharp edge. With ribs, the radius goes in and out that many times around.
        private static Mesh BuildLathe(List<float> profile, int segments, int ribs, float ribDepth)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            int rings = profile.Count / 2;
            for (int r = 0; r < rings; r++)
            {
                float radius = profile[r * 2], y = profile[r * 2 + 1];
                for (int s = 0; s <= segments; s++)
                {
                    float az = 2f * MathF.PI * s / segments;
                    float k = ribs > 0 ? 1f - ribDepth * (0.5f + 0.5f * MathF.Cos(ribs * az)) : 1f;
                    verts.Add(new Vector3(radius * k * MathF.Cos(az), y, radius * k * MathF.Sin(az)));
                }
            }
            int row = segments + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s, b = a + 1, c = a + row, d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
            return Finish(verts, tris);
        }

        private static Mesh BuildBox()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var corners = new Vector3[4];
                    for (int i = 0; i < 4; i++)
                    {
                        // Round the face: (-1,-1), (1,-1), (1,1), (-1,1) on the two other axes.
                        float u = i == 0 || i == 3 ? -1f : 1f, v = i < 2 ? -1f : 1f;
                        corners[i] = axis == 0 ? new Vector3(sign, u, v) : axis == 1 ? new Vector3(u, sign, v) : new Vector3(u, v, sign);
                    }
                    Vector3 outward = axis == 0 ? new Vector3(sign, 0f, 0f) : axis == 1 ? new Vector3(0f, sign, 0f) : new Vector3(0f, 0f, sign);
                    AddFlat(verts, tris, outward, corners[0], corners[1], corners[2], corners[3]);
                }
            }
            return Finish(verts, tris);
        }

        // ---- dice with 6, 8 and 12 faces ----

        // The faces (their corners in order round the face) in the order of their numbers: [0] is the 1.
        private static Vector3[][] DieFaces(int sides)
        {
            float phi = (1f + MathF.Sqrt(5f)) / 2f;
            var corners = new List<Vector3>();
            var towards = new List<Vector3>(); // one direction per face
            var cube = new List<Vector3>();
            var axes = new List<Vector3>();
            for (int a = -1; a <= 1; a += 2)
            {
                axes.Add(new Vector3(a, 0f, 0f)); axes.Add(new Vector3(0f, a, 0f)); axes.Add(new Vector3(0f, 0f, a));
                for (int b = -1; b <= 1; b += 2)
                {
                    for (int c = -1; c <= 1; c += 2) cube.Add(new Vector3(a, b, c));
                }
            }
            if (sides == 6) { corners.AddRange(cube); towards.AddRange(axes); }
            else if (sides == 8) { corners.AddRange(axes); towards.AddRange(cube); }
            else
            {
                corners.AddRange(cube);
                for (int a = -1; a <= 1; a += 2)
                {
                    for (int b = -1; b <= 1; b += 2)
                    {
                        corners.Add(new Vector3(0f, a / phi, b * phi));
                        corners.Add(new Vector3(a / phi, b * phi, 0f));
                        corners.Add(new Vector3(a * phi, 0f, b / phi));
                        towards.Add(new Vector3(0f, a * phi, b));
                        towards.Add(new Vector3(b, 0f, a * phi));
                        towards.Add(new Vector3(a * phi, b, 0f));
                    }
                }
            }
            for (int i = 0; i < corners.Count; i++) corners[i] = corners[i].normalized;

            // A face is made of the corners furthest in its direction, taken in order round it.
            var faces = new List<Vector3[]>();
            foreach (Vector3 t in towards)
            {
                Vector3 n = t.normalized;
                float furthest = float.MinValue;
                foreach (Vector3 c in corners) furthest = MathF.Max(furthest, Vector3.Dot(c, n));
                var face = new List<Vector3>();
                foreach (Vector3 c in corners) if (Vector3.Dot(c, n) > furthest - 1e-3f) face.Add(c);
                Vector3 u = (face[0] - n * Vector3.Dot(face[0], n)).normalized, v = Vector3.Cross(n, u);
                face.Sort((p, q) => MathF.Atan2(Vector3.Dot(p, v), Vector3.Dot(p, u)).CompareTo(MathF.Atan2(Vector3.Dot(q, v), Vector3.Dot(q, u))));
                faces.Add(face.ToArray());
            }

            // The cube already has a face towards -X. The others are turned like the 20-sided die: the first
            // face goes to -X, and a face next to it goes straight above it.
            if (sides != 6)
            {
                Vector3 front = Middle(faces[0]).normalized, above = front;
                float nearest = -1f;
                for (int i = 1; i < faces.Count; i++)
                {
                    Vector3 n = Middle(faces[i]).normalized;
                    if (Vector3.Dot(n, front) > nearest + 1e-3f) { nearest = Vector3.Dot(n, front); above = n; }
                }
                Vector3 bx = front * -1f;
                Vector3 by = (above - front * Vector3.Dot(above, front)).normalized;
                Vector3 bz = Vector3.Cross(bx, by);
                foreach (Vector3[] f in faces)
                {
                    for (int i = 0; i < f.Length; i++) f[i] = new Vector3(Vector3.Dot(bx, f[i]), Vector3.Dot(by, f[i]), Vector3.Dot(bz, f[i]));
                }
            }

            // Opposite faces add up to one more than the number of faces. The highest number is on the forehead
            // (on the cube: on the face); the other pairs go round with high and low numbers alternating.
            Vector3[] hero = faces[0];
            float best = float.MinValue;
            foreach (Vector3[] f in faces)
            {
                Vector3 n = Middle(f).normalized;
                float score = sides == 6 ? -n.x : (n.x < -0.1f && n.y > 0.1f ? n.y - MathF.Abs(n.z) : -9f);
                if (score > best) { best = score; hero = f; }
            }
            var numbered = new Vector3[sides][];
            numbered[sides - 1] = hero;
            numbered[0] = OppositeOf(faces, hero);
            var upper = new List<Vector3[]>();
            foreach (Vector3[] f in faces)
            {
                if (f == hero || f == numbered[0]) continue;
                Vector3 n = Middle(f).normalized;
                bool up = n.y > 0.01f || (n.y > -0.01f && (n.x < -0.01f || (n.x < 0.01f && n.z > 0f)));
                if (up) upper.Add(f);
            }
            upper.Sort((a, b) => Around(a).CompareTo(Around(b)));
            for (int i = 0; i < upper.Count; i++)
            {
                int high = sides - 1 - i;
                int number = i % 2 == 0 ? high : sides + 1 - high;
                numbered[number - 1] = upper[i];
                numbered[sides - number] = OppositeOf(faces, upper[i]);
            }
            return numbered;
        }

        private static Vector3 Middle(Vector3[] face)
        {
            Vector3 sum = Vector3.zero;
            foreach (Vector3 c in face) sum += c;
            return sum / face.Length;
        }

        private static float Around(Vector3[] face)
        {
            Vector3 c = Middle(face);
            return MathF.Atan2(c.z, -c.x) + c.y * 0.001f;
        }

        private static Vector3[] OppositeOf(List<Vector3[]> faces, Vector3[] face)
        {
            Vector3 n = Middle(face);
            Vector3[] best = face;
            foreach (Vector3[] f in faces) if (Vector3.Dot(Middle(f), n) < Vector3.Dot(Middle(best), n)) best = f;
            return best;
        }

        private static Mesh BuildDie(int sides, bool numbers)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3[][] faces = DieFaces(sides);
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3[] face = faces[f];
                Vector3 centre = Middle(face), normal = centre.normalized;
                if (!numbers)
                {
                    for (int i = 1; i + 1 < face.Length; i++) AddFlat(verts, tris, normal, face[0], face[i], face[i + 1], face[i + 1]);
                    continue;
                }

                // The number stands as upright as the face allows: its top points at a corner or at the middle of
                // an edge, or away from one. On a face that lies flat, its top points to the back of the head.
                Vector3 up = Vector3.up;
                float best = float.MinValue, room = float.MaxValue;
                for (int i = 0; i < face.Length; i++)
                {
                    Vector3 middle = (face[i] + face[(i + 1) % face.Length]) / 2f - centre;
                    room = MathF.Min(room, middle.magnitude);
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 d = (k < 2 ? face[i] - centre : middle).normalized * (k % 2 == 0 ? 1f : -1f);
                        float score = d.y + d.x * 0.01f;
                        if (score > best + 1e-4f) { best = score; up = d; }
                    }
                }
                Vector3 right = Vector3.Cross(normal, up);
                Vector3 origin = centre + normal * 0.012f; // clear of the face, so the two do not flicker

                // The same proportions as on the 20-sided die, whose faces have 0.3035 of room around their middle.
                float size = room / 0.3035f;
                float h = 0.27f * size, w = 0.15f * size, t = 0.042f * size, gap = 0.065f * size;
                string text = (f + 1).ToString();
                float width = gap * (text.Length - 1);
                foreach (char ch in text) width += ch == '1' ? t : w;
                float x = -width / 2f;
                foreach (char ch in text)
                {
                    float cell = ch == '1' ? t : w;
                    float l = x + t / 2f, r = x + cell - t / 2f;
                    int seg = DigitSegments[ch - '0'];
                    if ((seg & 0x01) != 0) AddBar(verts, tris, origin, right, up, normal, l, h / 2f, r, h / 2f, t);
                    if ((seg & 0x40) != 0) AddBar(verts, tris, origin, right, up, normal, l, 0f, r, 0f, t);
                    if ((seg & 0x08) != 0) AddBar(verts, tris, origin, right, up, normal, l, -h / 2f, r, -h / 2f, t);
                    if ((seg & 0x02) != 0) AddBar(verts, tris, origin, right, up, normal, r, 0f, r, h / 2f, t);
                    if ((seg & 0x04) != 0) AddBar(verts, tris, origin, right, up, normal, r, -h / 2f, r, 0f, t);
                    if ((seg & 0x20) != 0) AddBar(verts, tris, origin, right, up, normal, l, 0f, l, h / 2f, t);
                    if ((seg & 0x10) != 0) AddBar(verts, tris, origin, right, up, normal, l, -h / 2f, l, 0f, t);
                    x += cell + gap;
                }
                // 6 and 9 are underlined where the die has both.
                if (sides >= 9 && (text == "6" || text == "9"))
                    AddBar(verts, tris, origin, right, up, normal, -w / 2f, -h / 2f - 0.075f * size, w / 2f, -h / 2f - 0.075f * size, t * 0.7f);
            }
            return Finish(verts, tris);
        }

        // ---- twenty-sided die ----

        // The 20 faces (three corners each) in the order of their numbers: [0] is the 1, [19] the 20.
        private static Vector3[][] D20Faces()
        {
            float phi = (1f + MathF.Sqrt(5f)) / 2f;
            var corners = new List<Vector3>();
            for (int a = -1; a <= 1; a += 2)
            {
                for (int b = -1; b <= 1; b += 2)
                {
                    corners.Add(new Vector3(0f, a, b * phi).normalized);
                    corners.Add(new Vector3(a, b * phi, 0f).normalized);
                    corners.Add(new Vector3(a * phi, 0f, b).normalized);
                }
            }

            // Three corners that are all one edge apart make a face.
            float edge = 4f / MathF.Sqrt(10f + 2f * MathF.Sqrt(5f)), slack = edge * 1.01f;
            var faces = new List<Vector3[]>();
            for (int i = 0; i < 12; i++)
            {
                for (int j = i + 1; j < 12; j++)
                {
                    if ((corners[i] - corners[j]).magnitude > slack) continue;
                    for (int k = j + 1; k < 12; k++)
                    {
                        if ((corners[i] - corners[k]).magnitude > slack || (corners[j] - corners[k]).magnitude > slack) continue;
                        faces.Add(new[] { corners[i], corners[j], corners[k] });
                    }
                }
            }

            // Turn the die: the first face goes to -X, and a face next to it goes straight above it.
            Vector3 front = Centre(faces[0]).normalized, above = front;
            for (int i = 1; i < faces.Count; i++)
            {
                Vector3 n = Centre(faces[i]).normalized;
                if (Vector3.Dot(n, front) > 0.7f) { above = n; break; } // neighbours are 41.8 degrees apart
            }
            Vector3 bx = front * -1f;
            Vector3 by = (above - front * Vector3.Dot(above, front)).normalized;
            Vector3 bz = Vector3.Cross(bx, by);
            foreach (Vector3[] f in faces)
            {
                for (int i = 0; i < 3; i++) f[i] = new Vector3(Vector3.Dot(bx, f[i]), Vector3.Dot(by, f[i]), Vector3.Dot(bz, f[i]));
            }

            // Opposite faces add up to 21, as on a real die. The forehead is the 20; the other pairs go
            // round the die with high and low numbers alternating between the upper and the lower half.
            var numbered = new Vector3[20][];
            var upper = new List<Vector3[]>();
            foreach (Vector3[] f in faces)
            {
                Vector3 n = Centre(f).normalized;
                if (n.x < -0.7f && n.y > 0.5f) { numbered[19] = f; numbered[0] = Opposite(faces, f); }
                else if (n.y > 0.01f || (n.y > -0.01f && n.x < 0f)) upper.Add(f); // the upper half, and the front face
            }
            upper.Sort((a, b) => Bearing(a).CompareTo(Bearing(b)));
            for (int i = 0; i < upper.Count; i++)
            {
                int high = 19 - i; // 19, 18, ... 11
                int number = i % 2 == 0 ? high : 21 - high;
                numbered[number - 1] = upper[i];
                numbered[20 - number] = Opposite(faces, upper[i]);
            }
            return numbered;
        }

        private static Vector3 Centre(Vector3[] face) => (face[0] + face[1] + face[2]) / 3f;

        private static float Bearing(Vector3[] face)
        {
            Vector3 c = Centre(face);
            return MathF.Atan2(c.z, -c.x) + c.y * 0.001f;
        }

        private static Vector3[] Opposite(List<Vector3[]> faces, Vector3[] face)
        {
            Vector3 n = Centre(face);
            Vector3[] best = face;
            foreach (Vector3[] f in faces) if (Vector3.Dot(Centre(f), n) < Vector3.Dot(Centre(best), n)) best = f;
            return best;
        }

        // Seven-segment digits: bit 0 top, 1 upper right, 2 lower right, 3 bottom, 4 lower left, 5 upper left, 6 middle.
        private static readonly int[] DigitSegments = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };

        private static Mesh BuildD20(bool numbers)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Vector3[][] faces = D20Faces();
            for (int f = 0; f < faces.Length; f++)
            {
                Vector3[] face = faces[f];
                Vector3 centre = Centre(face), normal = centre.normalized;
                if (!numbers)
                {
                    AddFlat(verts, tris, normal, face[0], face[1], face[2], face[2]);
                    continue;
                }

                // The number stands as upright as the face allows: its top points at a corner, or away from one.
                Vector3 up = Vector3.up;
                float best = float.MinValue;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 d = (face[i % 3] - centre).normalized * (i < 3 ? 1f : -1f);
                    if (d.y > best + 1e-4f) { best = d.y; up = d; }
                }
                Vector3 right = Vector3.Cross(normal, up);
                Vector3 origin = centre + normal * 0.012f; // clear of the face, so the two do not flicker

                const float h = 0.27f, w = 0.15f, t = 0.042f, gap = 0.065f;
                string text = (f + 1).ToString();
                float width = gap * (text.Length - 1);
                foreach (char ch in text) width += ch == '1' ? t : w;
                float x = -width / 2f;
                foreach (char ch in text)
                {
                    // A 1 is just the bar, so that it sits in the middle of its own space.
                    float cell = ch == '1' ? t : w;
                    float l = x + t / 2f, r = x + cell - t / 2f;
                    int seg = DigitSegments[ch - '0'];
                    if ((seg & 0x01) != 0) AddBar(verts, tris, origin, right, up, normal, l, h / 2f, r, h / 2f, t);
                    if ((seg & 0x40) != 0) AddBar(verts, tris, origin, right, up, normal, l, 0f, r, 0f, t);
                    if ((seg & 0x08) != 0) AddBar(verts, tris, origin, right, up, normal, l, -h / 2f, r, -h / 2f, t);
                    if ((seg & 0x02) != 0) AddBar(verts, tris, origin, right, up, normal, r, 0f, r, h / 2f, t);
                    if ((seg & 0x04) != 0) AddBar(verts, tris, origin, right, up, normal, r, -h / 2f, r, 0f, t);
                    if ((seg & 0x20) != 0) AddBar(verts, tris, origin, right, up, normal, l, 0f, l, h / 2f, t);
                    if ((seg & 0x10) != 0) AddBar(verts, tris, origin, right, up, normal, l, -h / 2f, l, 0f, t);
                    x += cell + gap;
                }
                // 6 and 9 are underlined, as on a real die, to tell them apart.
                if (text == "6" || text == "9") AddBar(verts, tris, origin, right, up, normal, -w / 2f, -h / 2f - 0.075f, w / 2f, -h / 2f - 0.075f, t * 0.7f);
            }
            return Finish(verts, tris);
        }

        // A straight bar of the given thickness between two points of a face, square ends included.
        private static void AddBar(List<Vector3> verts, List<int> tris, Vector3 origin, Vector3 right, Vector3 up, Vector3 normal,
                                   float x0, float y0, float x1, float y1, float thickness)
        {
            float e = thickness / 2f;
            AddFlat(verts, tris, normal,
                origin + right * (x0 - e) + up * (y0 - e), origin + right * (x0 - e) + up * (y1 + e),
                origin + right * (x1 + e) + up * (y1 + e), origin + right * (x1 + e) + up * (y0 - e));
        }

        // A flat triangle (c == d) or quad with its own corners, wound so that it is seen from where 'outward' points.
        private static void AddFlat(List<Vector3> verts, List<int> tris, Vector3 outward, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            int first = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(first); tris.Add(flip ? first + 2 : first + 1); tris.Add(flip ? first + 1 : first + 2);
            if ((d - c).magnitude < 1e-6f) return;
            verts.Add(d);
            tris.Add(first); tris.Add(flip ? first + 3 : first + 2); tris.Add(flip ? first + 2 : first + 3);
        }
    }

    /// <summary>One piece of a hat or accessory, in "head units": head radius 1, up +Y, face towards -X.</summary>
    public struct ShapePart
    {
        public Mesh Mesh;
        public Vector3 Position, Euler, Scale;
        public bool Secondary; // uses the second colour
        public bool Toned;     // has a colour of its own, whatever the player chose
        public Color Tone;

        public ShapePart(Mesh mesh, Vector3 position, Vector3 scale, Vector3 euler = default, bool secondary = false)
        {
            Mesh = mesh;
            Position = position;
            Scale = scale;
            Euler = euler;
            Secondary = secondary;
            Toned = false;
            Tone = default;
        }

        public ShapePart(Color tone, Mesh mesh, Vector3 position, Vector3 scale, Vector3 euler = default)
            : this(mesh, position, scale, euler)
        {
            Toned = true;
            Tone = tone;
        }
    }

    public static class Wardrobe
    {
        public static readonly string[] HatNames =
        {
            "None", "Top hat", "Party hat", "Wizard hat", "Bowler", "Crown", "Halo", "Antenna", "Cap",
            "Cowboy hat", "Chef hat", "Beanie", "Propeller cap", "Sombrero", "Fez", "Viking helmet", "Santa hat",
            "Traffic cone", "Mushroom",
        };

        /// <summary>The last hat that versions up to 1.5.0 know; they refuse a look with a higher number.</summary>
        public const int LastOldHat = 8;

        public static readonly string[] AccessoryNames = { "Glasses", "Moustache", "Ears", "Horns" };

        /// <summary>The order is the number sent to friends and saved: new shapes go at the end.</summary>
        public static readonly string[] HeadNames =
        {
            "Normal", "D20 die", "D12 die", "D8 die", "D6 die", "Cube", "Creeper", "Minifig", "Ogre", "Donkey",
            "Alien", "Goggle capsule", "Visor capsule", "Gingerbread", "Pumpkin", "Egg", "Light bulb", "Television",
            "Donut", "Ice cream", "Bowling ball",
        };

        // An icosahedron's faces are at 0.795 of its corners' radius: at this size they just clear the head.
        public const float DieRadius = 1.27f;
        public const int Television = 17, Donut = 18; // index in HeadNames

        // With a shape on, hats go up to its top and accessories forward to its front.
        public static readonly float[] HeadHatLift =
        {
            0f, 0.3f, 0.3f, 0.45f, 0.36f, 0.36f, 0.36f, 0.4f, 0.06f, 0.08f,
            0.6f, 0.35f, 0.35f, 0.62f, 0.25f, 0.3f, 1.2f, 0.38f,
            1.12f, 0f, 0.06f,
        };
        public static readonly float[] HeadAccessoryShift =
        {
            0f, 0.08f, 0.08f, 0.12f, 0.12f, 0.12f, 0.12f, 0.1f, 0.07f, 0.1f,
            0.2f, 0.06f, 0.06f, 0.06f, 0.34f, 0.05f, 0.1f, 0f,
            0f, 0.07f, 0.07f,
        };

        // The colour each shape is known by (hue, saturation, value), offered by a button; null where there is none.
        public static readonly float[][] HeadUsualColour =
        {
            null, null, null, null, null, null, new[] { 0.33f, 0.75f, 0.7f }, new[] { 0.14f, 0.9f, 1f }, new[] { 0.24f, 0.7f, 0.72f }, new[] { 0.08f, 0.25f, 0.55f },
            new[] { 0.3f, 0.25f, 0.7f }, new[] { 0.15f, 0.85f, 1f }, new[] { 0f, 0.85f, 0.85f }, new[] { 0.07f, 0.7f, 0.6f }, new[] { 0.07f, 0.95f, 1f }, new[] { 0.1f, 0.12f, 0.97f }, new[] { 0.14f, 0.45f, 1f }, new[] { 0.07f, 0.5f, 0.4f },
            null, new[] { 0.95f, 0.35f, 1f }, new[] { 0.62f, 0.8f, 0.35f },
        };

        private static readonly Color White = new Color(0.96f, 0.95f, 0.9f), Black = new Color(0.07f, 0.07f, 0.09f);
        private static readonly Vector3 Forward = new Vector3(0f, 0f, 90f); // turns a part's +Y towards the face (-X)

        /// <summary>The shape put over the head. It has the head's colour; "secondary" parts are light or dark,
        /// whichever shows on it, and some parts have a colour of their own.</summary>
        public static List<ShapePart> Head(int index)
        {
            var p = new List<ShapePart>();
            switch (index)
            {
                case 1:
                    p.Add(new ShapePart(Shapes.D20, V(0f, 0f, 0f), U(DieRadius)));
                    p.Add(new ShapePart(Shapes.D20Numbers, V(0f, 0f, 0f), U(DieRadius), default, true));
                    break;
                case 2: // a dodecahedron's faces are at 0.795 of its corners' radius too
                    p.Add(new ShapePart(Shapes.Die(12), V(0f, 0f, 0f), U(DieRadius)));
                    p.Add(new ShapePart(Shapes.DieNumbers(12), V(0f, 0f, 0f), U(DieRadius), default, true));
                    break;
                case 3: // octahedron and cube: faces at 0.577
                    p.Add(new ShapePart(Shapes.Die(8), V(0f, 0f, 0f), U(1.76f)));
                    p.Add(new ShapePart(Shapes.DieNumbers(8), V(0f, 0f, 0f), U(1.76f), default, true));
                    break;
                case 4:
                    p.Add(new ShapePart(Shapes.Die(6), V(0f, 0f, 0f), U(1.767f)));
                    p.Add(new ShapePart(Shapes.DieNumbers(6), V(0f, 0f, 0f), U(1.767f), default, true));
                    break;
                case 5: // cube
                    p.Add(new ShapePart(Shapes.Box, V(0f, 0f, 0f), U(1.02f)));
                    break;
                case 6: // creeper: the face is drawn on a grid of 8 by 8 squares
                {
                    p.Add(new ShapePart(Shapes.Box, V(0f, 0f, 0f), U(1.02f)));
                    var face = new List<float[]>
                    {
                        Squares(1, 2, 2, 3), Squares(5, 2, 6, 3), Squares(3, 4, 4, 4), Squares(2, 5, 5, 6), Squares(2, 7, 2, 7), Squares(5, 7, 5, 7),
                    };
                    p.Add(new ShapePart(Black, Shapes.Decals("creeper", OnPlane(1.035f), face), V(0f, 0f, 0f), U(1f)));
                    break;
                }
                case 7: // minifig: a rounded cylinder with a stud on top, two eyes and a smile
                {
                    p.Add(new ShapePart(Shapes.RoundedCylinder(0.24f), V(0f, 0f, 0f), U(1.05f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 1.04f, 0f), V(0.55f, 0.3f, 0.55f)));
                    var face = new List<float[]> { Dot(0.34f, 0.26f, 0.11f), Dot(-0.34f, 0.26f, 0.11f) };
                    Stroke(face, 0f, 0.12f, 0.52f, 215f, 325f, 0.075f, 8);
                    p.Add(new ShapePart(Black, Shapes.Decals("minifig", OnCylinder(1.065f), face), V(0f, 0f, 0f), U(1f)));
                    break;
                }
                case 8: // ogre: a wide head with two trumpet ears
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0f, 0f), V(1.06f, 1.03f, 1.18f)));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        p.Add(new ShapePart(Shapes.Cylinder(0.42f, 1f), V(0f, 0.72f, side * 0.86f), V(0.21f, 0.52f, 0.21f), V(side * 52f, 0f, 0f)));
                        // The open end of the trumpet.
                        p.Add(new ShapePart(Shapes.Cylinder(1f, 0.6f), V(0f, 1.04f, side * 1.27f), V(0.16f, 0.02f, 0.16f), V(side * 52f, 0f, 0f), true));
                    }
                    break;
                case 9: // donkey: a long muzzle and two long ears
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0f, 0f), U(1.05f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.75f), V(-0.5f, -0.25f, 0f), V(0.74f, 1.15f, 0.74f), Forward));
                    p.Add(new ShapePart(Shapes.Sphere, V(-1.65f, -0.25f, 0f), V(0.3f, 0.555f, 0.555f)));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        p.Add(new ShapePart(Black, Shapes.Sphere, V(-1.9f, -0.14f, side * 0.2f), V(0.05f, 0.09f, 0.07f)));
                        p.Add(new ShapePart(Shapes.Sphere, V(0.1f, 1.5f, side * 0.5f), V(0.13f, 0.8f, 0.27f), V(side * 14f, 0f, 0f)));
                        p.Add(new ShapePart(Shapes.Sphere, V(0.03f, 1.5f, side * 0.5f), V(0.09f, 0.62f, 0.17f), V(side * 14f, 0f, 0f), true));
                    }
                    break;
                case 10: // alien: a big skull, a narrow chin and two slanted black eyes
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0.3f, 0f), V(1.25f, 1.2f, 1.3f)));
                    p.Add(new ShapePart(Shapes.Cylinder(0.5f, 1f), V(0f, -1.15f, 0f), V(1.18f, 1.15f, 1.18f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, -1.15f, 0f), V(0.59f, 0.25f, 0.59f)));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        p.Add(new ShapePart(Black, Shapes.Sphere, V(-1.1f, 0.2f, side * 0.45f), V(0.12f, 0.2f, 0.36f), V(side * 22f, side * 20f, 0f)));
                    }
                    break;
                case 11: // goggle capsule: a band round the head and one big eye in a ring
                    p.Add(new ShapePart(Shapes.Capsule(0.25f), V(0f, 0f, 0f), U(1.05f)));
                    p.Add(new ShapePart(Black, Shapes.Cylinder(1f, 1f), V(0f, 0.38f, 0f), V(1.065f, 0.24f, 1.065f)));
                    p.Add(new ShapePart(new Color(0.6f, 0.62f, 0.65f), Shapes.Torus(0.2f), V(-1.06f, 0.5f, 0f), U(0.4f), Forward));
                    p.Add(new ShapePart(White, Shapes.Sphere, V(-1.0f, 0.5f, 0f), V(0.12f, 0.38f, 0.38f)));
                    p.Add(new ShapePart(Black, Shapes.Sphere, V(-1.1f, 0.5f, 0f), V(0.05f, 0.15f, 0.15f)));
                    break;
                case 12: // visor capsule: a wide visor and a pack on the back
                    p.Add(new ShapePart(Shapes.Capsule(0.25f), V(0f, 0f, 0f), U(1.05f)));
                    p.Add(new ShapePart(new Color(0.62f, 0.82f, 0.9f), Shapes.Sphere, V(-0.72f, 0.45f, 0f), V(0.5f, 0.36f, 0.72f)));
                    p.Add(new ShapePart(Shapes.Box, V(1.0f, -0.15f, 0f), V(0.28f, 0.55f, 0.6f)));
                    break;
                case 13: // gingerbread: a thick biscuit seen from the front, with icing
                {
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(1.04f, 0f, 0f), V(1.3f, 2.08f, 1.3f), Forward));
                    p.Add(new ShapePart(White, Shapes.Torus(0.045f), V(-1.04f, 0f, 0f), U(1.14f), Forward));
                    var face = new List<float[]> { Dot(0.42f, 0.42f, 0.13f), Dot(-0.42f, 0.42f, 0.13f), Dot(0f, -0.95f, 0.1f) };
                    Stroke(face, 0f, 0.15f, 0.68f, 215f, 325f, 0.09f, 8);
                    p.Add(new ShapePart(White, Shapes.Decals("gingerbread", OnPlane(1.055f), face), V(0f, 0f, 0f), U(1f)));
                    break;
                }
                case 14: // pumpkin: grooves, a stem and a carved face
                {
                    p.Add(new ShapePart(Shapes.Ribbed(10, 0.07f), V(0f, 0f, 0f), V(1.34f, 1.08f, 1.34f)));
                    p.Add(new ShapePart(new Color(0.3f, 0.42f, 0.15f), Shapes.Cylinder(1f, 0.7f), V(0f, 0.98f, 0f), V(0.16f, 0.4f, 0.16f), V(0f, 0f, -12f)));
                    var face = new List<float[]>
                    {
                        new[] { 0.2f, 0.28f, 0.64f, 0.28f, 0.42f, 0.62f }, new[] { -0.2f, 0.28f, -0.42f, 0.62f, -0.64f, 0.28f },
                    };
                    // The grin: pointed at both ends, in strips so that it follows the round surface.
                    for (int i = 0; i < 8; i++)
                    {
                        float z0 = -0.78f + 1.56f * i / 8f, z1 = -0.78f + 1.56f * (i + 1) / 8f;
                        float a = z0 / 0.78f, b = z1 / 0.78f;
                        face.Add(new[] { z0, -0.74f + 0.5f * a * a, z1, -0.74f + 0.5f * b * b, z1, -0.44f + 0.2f * b * b, z0, -0.44f + 0.2f * a * a });
                    }
                    p.Add(new ShapePart(Black, Shapes.Decals("pumpkin", OnEllipsoid(1.34f, 1.08f, 1.34f, 0f, 1.025f), face), V(0f, 0f, 0f), U(1f)));
                    break;
                }
                case 15: // egg
                    p.Add(new ShapePart(Shapes.Egg(1.35f), V(0f, 0f, 0f), U(1.04f)));
                    break;
                case 16: // light bulb, hanging: the glass round the head and the screw base on top
                {
                    var metal = new Color(0.62f, 0.62f, 0.6f);
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0f, 0f), U(1.08f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.6f), V(0f, 0.72f, 0f), V(0.8f, 0.55f, 0.8f)));
                    p.Add(new ShapePart(metal, Shapes.Cylinder(1f, 1f), V(0f, 1.27f, 0f), V(0.48f, 0.5f, 0.48f)));
                    for (int i = 0; i < 3; i++) p.Add(new ShapePart(metal, Shapes.Torus(0.1f), V(0f, 1.37f + 0.15f * i, 0f), U(0.48f)));
                    p.Add(new ShapePart(Black, Shapes.Cylinder(1f, 0.5f), V(0f, 1.77f, 0f), V(0.26f, 0.12f, 0.26f)));
                    break;
                }
                case 17: // television: the head is the screen, bulging out of the front like an old tube
                {
                    var dark = new Color(0.12f, 0.12f, 0.14f);
                    var metal = new Color(0.62f, 0.62f, 0.6f);
                    p.Add(new ShapePart(Shapes.Box, V(0.25f, 0f, 0f), V(0.85f, 1.05f, 1.2f)));
                    // The frame round the screen.
                    p.Add(new ShapePart(dark, Shapes.Box, V(-0.64f, 0.92f, 0f), V(0.07f, 0.09f, 1.01f)));
                    p.Add(new ShapePart(dark, Shapes.Box, V(-0.64f, -0.92f, 0f), V(0.07f, 0.09f, 1.01f)));
                    p.Add(new ShapePart(dark, Shapes.Box, V(-0.64f, 0f, 0.92f), V(0.07f, 0.83f, 0.09f)));
                    p.Add(new ShapePart(dark, Shapes.Box, V(-0.64f, 0f, -0.92f), V(0.07f, 0.83f, 0.09f)));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        p.Add(new ShapePart(metal, Shapes.Cylinder(1f, 1f), V(0.25f, 1.03f, 0f), V(0.03f, 1.0f, 0.03f), V(side * 32f, 0f, 0f)));
                        p.Add(new ShapePart(metal, Shapes.Sphere, V(0.25f, 1.03f + 0.848f, side * 0.53f), U(0.07f)));
                    }
                    break;
                }
                case 18: // donut: the head looks out of the hole
                {
                    p.Add(new ShapePart(new Color(0.8f, 0.58f, 0.3f), Shapes.FineTorus(0.44f), V(0f, 0f, 0f), U(1.25f), Forward));
                    p.Add(new ShapePart(new Color(0.95f, 0.5f, 0.68f), Shapes.FineTorus(0.464f), V(-0.1f, 0f, 0f), U(1.25f), Forward));
                    Color[] sprinkles = { White, new Color(1f, 0.85f, 0.2f), new Color(0.3f, 0.6f, 0.95f), new Color(0.9f, 0.2f, 0.2f) };
                    for (int i = 0; i < 12; i++)
                    {
                        // On the front of the icing, at uneven distances from the middle of the ring.
                        float a = 2f * MathF.PI * i / 12f, r = 1.25f + (i % 3 - 1) * 0.2f;
                        float x = -0.1f - MathF.Sqrt(0.58f * 0.58f - (r - 1.25f) * (r - 1.25f));
                        p.Add(new ShapePart(sprinkles[i % 4], Shapes.Sphere, V(x, r * MathF.Cos(a), r * MathF.Sin(a)), V(0.035f, 0.045f, 0.13f), V(i * 47f, 0f, 0f)));
                    }
                    break;
                }
                case 19: // ice cream: the head is the scoop, with the cone upside down on it
                {
                    var wafer = new Color(0.85f, 0.62f, 0.3f);
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0f, 0f), U(1.06f)));
                    p.Add(new ShapePart(Shapes.Torus(0.2f), V(0f, 0.52f, 0f), V(0.9f, 0.7f, 0.9f)));
                    p.Add(new ShapePart(wafer, Shapes.Cylinder(1f, 0f), V(0.1f, 0.56f, 0f), V(0.86f, 1.7f, 0.86f), V(0f, 0f, -14f)));
                    break;
                }
                case 20: // bowling ball: three finger holes
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0f, 0f), U(1.06f)));
                    p.Add(new ShapePart(Black, Shapes.Sphere, V(-0.55f, 0.83f, 0.17f), U(0.12f)));
                    p.Add(new ShapePart(Black, Shapes.Sphere, V(-0.55f, 0.83f, -0.17f), U(0.12f)));
                    p.Add(new ShapePart(Black, Shapes.Sphere, V(-0.24f, 0.97f, 0f), U(0.12f)));
                    break;
            }
            return p;
        }

        // ---- drawing on a head: where a point (z, y) seen from the front lands on the shape ----

        private static Func<float, float, Vector3> OnPlane(float distance)
        {
            return (z, y) => new Vector3(-distance, y, z);
        }

        // A cylinder around +Y: z is measured along its surface.
        private static Func<float, float, Vector3> OnCylinder(float radius)
        {
            return (z, y) => new Vector3(-radius * MathF.Cos(z / radius), y, radius * MathF.Sin(z / radius));
        }

        // 'lift' raises the drawing off the surface (1.02 = by 2 percent of the radius).
        private static Func<float, float, Vector3> OnEllipsoid(float a, float b, float c, float centreY, float lift)
        {
            return (z, y) =>
            {
                float v = (y - centreY) / b, w = z / c;
                float x = -a * MathF.Sqrt(MathF.Max(0f, 1f - v * v - w * w));
                return new Vector3(x * lift, centreY + (y - centreY) * lift, z * lift);
            };
        }

        private static float[] Rect(float z0, float y0, float z1, float y1) => new[] { z0, y0, z1, y0, z1, y1, z0, y1 };

        // Squares of an 8 by 8 grid over a face 2.04 wide: columns from left to right, rows from the top down.
        private static float[] Squares(int column0, int row0, int column1, int row1)
        {
            const float g = 2.04f / 8f;
            return Rect(-1.02f + column0 * g, 1.02f - (row1 + 1) * g, -1.02f + (column1 + 1) * g, 1.02f - row0 * g);
        }

        private static float[] Dot(float z, float y, float radius)
        {
            var o = new float[20];
            for (int i = 0; i < 10; i++)
            {
                float a = 2f * MathF.PI * i / 10f;
                o[i * 2] = z + radius * MathF.Cos(a);
                o[i * 2 + 1] = y + radius * MathF.Sin(a);
            }
            return o;
        }

        // Part of a circle drawn as a line of the given thickness; angles in degrees, 270 is the bottom.
        private static void Stroke(List<float[]> pieces, float z, float y, float radius, float from, float to, float thickness, int steps)
        {
            float inner = radius - thickness / 2f, outer = radius + thickness / 2f;
            for (int i = 0; i < steps; i++)
            {
                float a = (from + (to - from) * i / steps) * MathF.PI / 180f, b = (from + (to - from) * (i + 1) / steps) * MathF.PI / 180f;
                pieces.Add(new[]
                {
                    z + inner * MathF.Cos(a), y + inner * MathF.Sin(a), z + outer * MathF.Cos(a), y + outer * MathF.Sin(a),
                    z + outer * MathF.Cos(b), y + outer * MathF.Sin(b), z + inner * MathF.Cos(b), y + inner * MathF.Sin(b),
                });
            }
        }

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        private static Vector3 U(float s) => new Vector3(s, s, s);

        public static List<ShapePart> Hat(int index)
        {
            var p = new List<ShapePart>();
            switch (index)
            {
                case 1: // top hat
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.70f, 0f), V(1.08f, 0.06f, 1.08f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1.06f), V(0f, 0.72f, 0f), V(0.70f, 0.95f, 0.70f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.76f, 0f), V(0.715f, 0.14f, 0.715f), default, true));
                    break;
                case 2: // party cone
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.80f, 0f), V(0.58f, 1.35f, 0.58f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 2.15f, 0f), U(0.15f), default, true));
                    break;
                case 3: // wizard
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.64f, 0f), V(1.3f, 0.05f, 1.3f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.66f, 0f), V(0.76f, 1.9f, 0.76f), V(0f, 0f, -6f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.69f, 0f), V(0.77f, 0.12f, 0.77f), default, true));
                    break;
                case 4: // bowler
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.68f, 0f), V(1.05f, 0.05f, 1.05f)));
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.70f, 0f), V(0.74f, 0.62f, 0.74f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.71f, 0f), V(0.75f, 0.10f, 0.75f), default, true));
                    break;
                case 5: // crown
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1.08f), V(0f, 0.72f, 0f), V(0.70f, 0.28f, 0.70f)));
                    for (int i = 0; i < 6; i++)
                    {
                        float a = 2f * MathF.PI * i / 6f;
                        p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0.68f * MathF.Cos(a), 0.98f, 0.68f * MathF.Sin(a)), V(0.14f, 0.32f, 0.14f)));
                        p.Add(new ShapePart(Shapes.Sphere, V(0.70f * MathF.Cos(a), 1.32f, 0.70f * MathF.Sin(a)), U(0.055f), default, true));
                    }
                    break;
                case 6: // halo
                    p.Add(new ShapePart(Shapes.Torus(0.09f), V(0f, 1.5f, 0f), U(0.62f)));
                    break;
                case 7: // antenna
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.97f, 0f), V(0.04f, 0.95f, 0.04f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 2.0f, 0f), U(0.17f), default, true));
                    break;
                case 8: // cap with a visor towards the face (-X)
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.22f, 0f), V(1.04f, 0.84f, 1.04f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(-0.95f, 0.26f, 0f), V(0.75f, 0.05f, 0.62f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.06f, 0f), U(0.08f), default, true));
                    break;
                case 9: // cowboy: brim wider at the sides
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.66f, 0f), V(1.3f, 0.05f, 1.55f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.82f), V(0f, 0.68f, 0f), V(0.73f, 0.62f, 0.73f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.71f, 0f), V(0.745f, 0.10f, 0.745f), default, true));
                    break;
                case 10: // chef: a band and a puff on top
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.70f, 0f), V(0.72f, 0.55f, 0.72f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.38f, 0f), V(0.94f, 0.42f, 0.94f)));
                    break;
                case 11: // beanie with a pom-pom
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.30f, 0f), V(1.03f, 0.80f, 1.03f)));
                    p.Add(new ShapePart(Shapes.Torus(0.1f), V(0f, 0.33f, 0f), U(0.98f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.2f, 0f), U(0.2f), default, true));
                    break;
                case 12: // propeller cap
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.30f, 0f), V(1.03f, 0.80f, 1.03f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 1.08f, 0f), V(0.035f, 0.24f, 0.035f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.33f, 0.28f), V(0.11f, 0.025f, 0.28f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.33f, -0.28f), V(0.11f, 0.025f, 0.28f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.33f, 0f), U(0.06f), default, true));
                    break;
                case 13: // sombrero
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.62f, 0f), V(1.75f, 0.05f, 1.75f)));
                    p.Add(new ShapePart(Shapes.Torus(0.05f), V(0f, 0.66f, 0f), U(1.75f), default, true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.4f), V(0f, 0.64f, 0f), V(0.80f, 0.95f, 0.80f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.67f, 0f), V(0.81f, 0.10f, 0.81f), default, true));
                    break;
                case 14: // fez with a tassel hanging on one side
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.8f), V(0f, 0.72f, 0f), V(0.71f, 0.7f, 0.71f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.43f, 0f), U(0.05f), default, true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 1.44f, 0f), V(0.02f, 0.62f, 0.02f), V(90f, 0f, 0f), true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 1.06f, 0.62f), V(0.02f, 0.38f, 0.02f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.0f, 0.62f), V(0.06f, 0.13f, 0.06f), default, true));
                    break;
                case 15: // viking helmet: the horns point out and up
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.30f, 0f), V(1.04f, 0.80f, 1.04f)));
                    p.Add(new ShapePart(Shapes.Torus(0.07f), V(0f, 0.33f, 0f), U(1.0f), default, true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.75f, 0.78f), V(0.2f, 0.8f, 0.2f), V(40f, 0f, 0f), true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.75f, -0.78f), V(0.2f, 0.8f, 0.2f), V(-40f, 0f, 0f), true));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.1f, 0f), U(0.08f), default, true));
                    break;
                case 16: // santa: the cone leans back, with a ball on its tip
                    p.Add(new ShapePart(Shapes.Torus(0.16f), V(0f, 0.62f, 0f), U(0.80f), default, true));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.64f, 0f), V(0.80f, 1.5f, 0.80f), V(0f, 0f, -10f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0.26f, 2.12f, 0f), U(0.17f), default, true));
                    break;
                case 17: // traffic cone with a stripe
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(0f, 0.66f, 0f), V(1.0f, 0.07f, 1.0f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.18f), V(0f, 0.70f, 0f), V(0.74f, 1.5f, 0.74f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0.757f), V(0f, 1.30f, 0f), V(0.507f, 0.3f, 0.507f), default, true));
                    break;
                case 18: // mushroom cap with spots
                    p.Add(new ShapePart(Shapes.Hemisphere, V(0f, 0.35f, 0f), V(1.45f, 0.95f, 1.45f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 1.28f, 0f), V(0.2f, 0.06f, 0.2f), default, true));
                    for (int i = 0; i < 5; i++)
                    {
                        // On the cap's surface, 50 degrees down from its top.
                        float a = 2f * MathF.PI * i / 5f;
                        p.Add(new ShapePart(Shapes.Sphere, V(1.11f * MathF.Cos(a), 0.96f, 1.11f * MathF.Sin(a)), U(0.16f), default, true));
                    }
                    break;
            }
            return p;
        }

        /// <summary>Where the moustache sits; its halves are placed around this point.</summary>
        public static readonly Vector3 MoustacheCentre = new Vector3(-0.97f, -0.24f, 0f);
        /// <summary>The usual droop of the moustache's ends, in degrees.</summary>
        public const float MoustacheTilt = 18f;
        public const int Moustache = 1; // index in AccessoryNames

        /// <summary>One half of the moustache (side +1 or -1), relative to <see cref="MoustacheCentre"/>.
        /// A positive tilt lowers the ends, a negative one curls them up.</summary>
        public static ShapePart MoustacheHalf(int side, float width, float tilt)
        {
            return new ShapePart(Shapes.Sphere, V(0f, 0f, side * 0.19f * width), V(0.07f, 0.08f, 0.22f * width), V(side * tilt, 0f, 0f));
        }

        public static void AddAccessory(List<ShapePart> p, int index)
        {
            switch (index)
            {
                case 0: // glasses: two rings facing forward and a bridge
                    p.Add(new ShapePart(Shapes.Torus(0.14f), V(-0.95f, 0.16f, 0.30f), U(0.24f), V(0f, 0f, 90f)));
                    p.Add(new ShapePart(Shapes.Torus(0.14f), V(-0.95f, 0.16f, -0.30f), U(0.24f), V(0f, 0f, 90f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(-1.0f, 0.18f, -0.08f), V(0.028f, 0.16f, 0.028f), V(90f, 0f, 0f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(-0.88f, 0.18f, 0.52f), V(0.025f, 0.7f, 0.025f), V(0f, 0f, -90f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 1f), V(-0.88f, 0.18f, -0.52f), V(0.025f, 0.7f, 0.025f), V(0f, 0f, -90f)));
                    break;
                case 1: // moustache: its two halves, around MoustacheCentre
                    p.Add(MoustacheHalf(1, 1f, MoustacheTilt));
                    p.Add(MoustacheHalf(-1, 1f, MoustacheTilt));
                    break;
                case 2: // round ears
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0.80f, 0.70f), V(0.16f, 0.36f, 0.36f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(0f, 0.80f, -0.70f), V(0.16f, 0.36f, 0.36f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(-0.07f, 0.80f, 0.70f), V(0.12f, 0.22f, 0.22f), default, true));
                    p.Add(new ShapePart(Shapes.Sphere, V(-0.07f, 0.80f, -0.70f), V(0.12f, 0.22f, 0.22f), default, true));
                    break;
                case 3: // horns
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.74f, 0.52f), V(0.2f, 0.75f, 0.2f), V(28f, 0f, 0f)));
                    p.Add(new ShapePart(Shapes.Cylinder(1f, 0f), V(0f, 0.74f, -0.52f), V(0.2f, 0.75f, 0.2f), V(-28f, 0f, 0f)));
                    break;
            }
        }
    }
}
