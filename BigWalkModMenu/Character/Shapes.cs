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

        public ShapePart(Mesh mesh, Vector3 position, Vector3 scale, Vector3 euler = default, bool secondary = false)
        {
            Mesh = mesh;
            Position = position;
            Scale = scale;
            Euler = euler;
            Secondary = secondary;
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

        public static readonly string[] HeadNames = { "Normal", "D20 die" };

        // An icosahedron's faces are at 0.795 of its corners' radius: at this size they just clear the head.
        public const float DieRadius = 1.27f;
        // With the die on, hats go up to its top and accessories forward to its front face.
        public const float DieHatLift = 0.3f, DieAccessoryShift = 0.08f;

        /// <summary>The shape put over the head; the numbers use the second material.</summary>
        public static List<ShapePart> Head(int index)
        {
            var p = new List<ShapePart>();
            if (index == 1)
            {
                p.Add(new ShapePart(Shapes.D20, V(0f, 0f, 0f), U(DieRadius)));
                p.Add(new ShapePart(Shapes.D20Numbers, V(0f, 0f, 0f), U(DieRadius), default, true));
            }
            return p;
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
