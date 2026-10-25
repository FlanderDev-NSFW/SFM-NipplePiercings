using System;
using System.Numerics;

namespace NipplePiercings.Logic
{
    /// <summary>A shape the hanging pieces stay out of: a sphere (a breast), a capsule (an arm) or a half space (the floor).</summary>
    public readonly struct Collider
    {
        public enum ShapeKind : byte { Sphere, Capsule, Plane }

        public readonly ShapeKind Shape;
        public readonly Vector3 A, B;         // sphere: A = centre; capsule: the axis A - B; plane: A = a point on it, B = its normal
        public readonly float Radius;

        private Collider(ShapeKind shape, Vector3 a, Vector3 b, float radius) { Shape = shape; A = a; B = b; Radius = radius; }

        public static Collider Sphere(Vector3 centre, float radius) => new Collider(ShapeKind.Sphere, centre, centre, radius);
        public static Collider Capsule(Vector3 a, Vector3 b, float radius) => new Collider(ShapeKind.Capsule, a, b, radius);
        /// <summary>Everything below the plane through <paramref name="point"/> (away from <paramref name="up"/>) is solid.</summary>
        public static Collider Plane(Vector3 point, Vector3 up) => new Collider(ShapeKind.Plane, point, Vector3.Normalize(up), 0f);

        /// <summary>How deep <paramref name="p"/> (with <paramref name="margin"/> around it) is inside, and the way out
        /// (<paramref name="normal"/>, unit); 0 or less = outside.</summary>
        public float Depth(Vector3 p, float margin, out Vector3 normal)
        {
            switch (Shape)
            {
                case ShapeKind.Plane:
                    normal = B;
                    return margin - Vector3.Dot(p - A, B);
                case ShapeKind.Capsule:
                {
                    Vector3 ab = B - A;
                    float len2 = ab.LengthSquared();
                    float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(p - A, ab) / len2, 0f, 1f);
                    return Round(p, A + ab * t, margin, out normal);
                }
                default:
                    return Round(p, A, margin, out normal);
            }
        }

        private float Round(Vector3 p, Vector3 c, float margin, out Vector3 normal)
        {
            Vector3 d = p - c;
            float dist = d.Length();
            normal = dist < 1e-9f ? Vector3.UnitY : d / dist;
            return Radius + margin - dist;
        }
    }
}
