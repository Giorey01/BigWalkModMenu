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
        };

        public static readonly string[] AccessoryNames = { "Glasses", "Moustache", "Ears", "Horns" };

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
            }
            return p;
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
                case 1: // moustache
                    p.Add(new ShapePart(Shapes.Sphere, V(-0.97f, -0.24f, 0.19f), V(0.07f, 0.08f, 0.22f), V(18f, 0f, 0f)));
                    p.Add(new ShapePart(Shapes.Sphere, V(-0.97f, -0.24f, -0.19f), V(0.07f, 0.08f, 0.22f), V(-18f, 0f, 0f)));
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
