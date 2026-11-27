using System.Collections.Generic;
using UnityEngine;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        sealed partial class Composition
        {
            void Place(Kind kind, Zone zone, Vector3 anchor, float heading, float search, bool laid)
            {
                Prop asset = assets[(int)kind];
                Vector3 meters = StandardSize(kind) * Range(random, 0.85f, 1.15f);
                if (kind == Kind.DryGrassClump)
                    meters = Vector3.one * Mathf.Clamp(meters.x, 1.4f, 1.7f);
                if (zone == Zone.Trail && kind == Kind.BambooLog)
                    meters = new Vector3(Mathf.Max(1.55f, meters.x * 0.85f), meters.y * 0.90f, meters.z * 0.76f);

                Vector3 scale = ScaleFor(asset, meters);
                float yaw = heading + Range(random, kind == Kind.Boulder ? -75f : -18f, kind == Kind.Boulder ? 75f : 18f);
                float phase = Range(random, 0f, 6.283185f);
                float embed = kind == Kind.WallChunk ? Range(random, 0.26f, 0.39f) : Range(random, 0.055f, 0.095f);
                float lean = kind == Kind.DryGrassClump ? Range(random, 7f, 14f) : 0f;
                float layAngle = laid ? Range(random, 77f, 92f) : 0f;
                const int attempts = 42;
                for (int i = 0; i < attempts; i++)
                {
                    float spin = i * 2.399963f + phase;
                    float radius = search * Mathf.Sqrt(i / (float)(attempts - 1));
                    Vector3 candidate = anchor + new Vector3(Mathf.Cos(spin), 0f, Mathf.Sin(spin)) * radius;
                    if (!Probe(candidate, zone, out RaycastHit hit)) continue;

                    var yawRotation = Quaternion.Euler(0f, yaw, 0f);
                    Quaternion slopeTilt = Quaternion.RotateTowards(Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, hit.normal), 15f);
                    Quaternion rotation = slopeTilt * yawRotation * Quaternion.Euler(0f, 0f, layAngle);
                    if (lean > 0f)
                    {
                        Vector3 toward = new Vector3(CenterX - candidate.x, 0f, 0f).normalized;
                        rotation = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, toward)) * rotation;
                    }

                    Bounds model = asset.Meshes[0].bounds;
                    float height = TransformedHeight(model, rotation, scale);
                    float min = MinSupport(points[kind], rotation, scale, hit.normal);
                    Vector3 position = hit.point + hit.normal * (-min - height * embed);
                    Footprint full = Footprint.From(model, position, rotation, scale);
                    if (!Allowed(full, zone)) continue;
                    Footprint physical = full.Shrink(kind == Kind.DryGrassClump ? 0.66f : 0.88f);
                    if (Overlaps(physical, position.y, height)) continue;

                    Results.Add(new Placement
                    {
                        Asset = asset, Zone = zone, Position = position, Surface = hit.point, Normal = hit.normal,
                        Rotation = rotation, Scale = scale, Foot = physical,
                        Dimensions = new Vector3(meters.x, height, meters.z)
                    });
                    return;
                }

                Debug.Log($"ShadowTan / Tripo skipped {kind} in {zone} at ({anchor.x:F2}, {anchor.z:F2}): no clear support.");
            }

            bool Probe(Vector3 sample, Zone zone, out RaycastHit hit)
            {
                Vector3 origin = new Vector3(sample.x, 65f, sample.z);
                if (!Physics.Raycast(origin, Vector3.down, out hit, 68f, groundMask, QueryTriggerInteraction.Ignore))
                    return false;

                if (zone == Zone.Mouth || zone == Zone.Scree)
                    return hit.collider == ramp || (zone == Zone.Mouth && hit.collider == apron);

                return hit.collider is TerrainCollider && hit.point.y < 0.75f;
            }

            bool Allowed(Footprint footprint, Zone zone)
            {
                var center = footprint.Center;
                if (zone == Zone.Mouth || zone == Zone.Scree)
                {
                    Vector3 offset = new Vector3(center.x - top.x, 0f, center.y - top.z);
                    var axis = new Vector2(across.x, across.z);
                    return Mathf.Abs(Vector3.Dot(offset, across)) - footprint.Extent(axis) >= 1.48f;
                }

                if (center.y + footprint.Extent(Vector2.up) >= 1.25f && center.y < EndZ &&
                    Mathf.Abs(center.x - CenterX) - footprint.Extent(Vector2.right) < ClearHalfWidth + 0.03f)
                    return false;

                if (zone == Zone.Clearing)
                {
                    float distance = new Vector2(center.x - CenterX, center.y - 0.55f).magnitude;
                    return center.y >= 0.85f && distance >= 7.1f && distance <= 11.2f;
                }

                if (zone == Zone.Trail)
                    return center.y >= StartZ && center.y <= 52f && Mathf.Abs(center.x - CenterX) <= 3.9f;

                Vector2 radial = center - new Vector2(CenterX, 188f);
                return radial.magnitude - footprint.Extent(radial.normalized) >= 26.25f && radial.magnitude <= 31.2f;
            }

            bool Overlaps(Footprint footprint, float y, float height)
            {
                foreach (Placement placed in Results)
                {
                    if (Mathf.Abs(placed.Position.y - y) > Mathf.Max(1.2f, placed.Dimensions.y + height)) continue;
                    if (placed.Foot.Overlaps(footprint)) return true;
                }
                return false;
            }

            static float MinSupport(Vector3[] vertices, Quaternion rotation, Vector3 scale, Vector3 normal)
            {
                Vector3 localNormal = Vector3.Scale(Quaternion.Inverse(rotation) * normal, scale);
                float min = float.PositiveInfinity;
                for (int i = 0; i < vertices.Length; i++)
                    min = Mathf.Min(min, Vector3.Dot(localNormal, vertices[i]));
                return min;
            }

            static float TransformedHeight(Bounds bounds, Quaternion rotation, Vector3 scale)
            {
                Vector3 axis = Quaternion.Inverse(rotation) * Vector3.up;
                Vector3 dimensions = Vector3.Scale(bounds.size, scale);
                return Mathf.Abs(axis.x) * dimensions.x + Mathf.Abs(axis.y) * dimensions.y + Mathf.Abs(axis.z) * dimensions.z;
            }
        }

        readonly struct Footprint
        {
            internal readonly Vector2 Center;
            internal readonly Vector2 X;
            internal readonly Vector2 Z;
            internal readonly Vector2 Half;

            Footprint(Vector2 center, Vector2 x, Vector2 z, Vector2 half)
            {
                Center = center;
                X = x;
                Z = z;
                Half = half;
            }

            internal static Footprint From(Bounds bounds, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Vector3 right = rotation * Vector3.right;
                var x = new Vector2(right.x, right.z).normalized;
                if (x.sqrMagnitude < 0.01f) x = Vector2.right;
                var z = new Vector2(-x.y, x.x);
                Vector3 worldCenter = position + rotation * Vector3.Scale(bounds.center, scale);
                var center = new Vector2(worldCenter.x, worldCenter.z);
                Vector3 e = Vector3.Scale(bounds.extents, scale);
                var rx = rotation * (Vector3.right * e.x);
                var ry = rotation * (Vector3.up * e.y);
                var rz = rotation * (Vector3.forward * e.z);
                return new Footprint(center, x, z,
                    new Vector2(Extent(x), Extent(z)));

                float Extent(Vector2 axis) => Project(rx, axis) + Project(ry, axis) + Project(rz, axis);
                static float Project(Vector3 v, Vector2 a) => Mathf.Abs(v.x * a.x + v.z * a.y);
            }

            internal float Extent(Vector2 axis) =>
                Mathf.Abs(Vector2.Dot(axis, X)) * Half.x + Mathf.Abs(Vector2.Dot(axis, Z)) * Half.y;

            internal Footprint Shrink(float ratio) => new Footprint(Center, X, Z, Half * ratio);

            internal bool Overlaps(Footprint other)
            {
                Vector2 delta = other.Center - Center;
                if (delta.sqrMagnitude > 25f) return false;
                Footprint current = this;
                return !Separated(X) && !Separated(Z) && !Separated(other.X) && !Separated(other.Z);

                bool Separated(Vector2 axis) =>
                    Mathf.Abs(Vector2.Dot(delta, axis)) > current.Extent(axis) + other.Extent(axis) + 0.035f;
            }
        }
    }
}
