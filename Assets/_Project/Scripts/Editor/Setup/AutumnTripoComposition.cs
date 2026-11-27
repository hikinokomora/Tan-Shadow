using System;
using System.Collections.Generic;
using UnityEngine;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        sealed class Placement
        {
            internal Prop Asset;
            internal Zone Zone;
            internal Vector3 Position;
            internal Vector3 Surface;
            internal Vector3 Normal;
            internal Quaternion Rotation;
            internal Vector3 Scale;
            internal Footprint Foot;
            internal Vector3 Dimensions;
        }

        static List<Placement> Compose(Transform entrance, Terrain terrain, Prop[] assets)
        {
            Physics.SyncTransforms();
            var stage = new Composition(entrance, terrain, assets);
            stage.Slope();
            stage.Clearing();
            stage.CompleteClearing();
            stage.Trail();
            stage.CompleteTrail();
            stage.Arena();
            return stage.Results;
        }

        sealed partial class Composition
        {
            readonly Random random = new Random(Seed);
            readonly Prop[] assets;
            readonly Dictionary<Kind, Vector3[]> points = new Dictionary<Kind, Vector3[]>();
            readonly Vector3 top;
            readonly Vector3 foot = new Vector3(CenterX, 0f, 0f);
            readonly Vector3 along;
            readonly Vector3 across;
            readonly int groundMask = LayerMask.GetMask("Ground");
            readonly Collider ramp;
            readonly Collider apron;
            internal readonly List<Placement> Results = new List<Placement>();

            internal Composition(Transform entrance, Terrain terrain, Prop[] assets)
            {
                this.assets = assets;
                ramp = entrance.Find("Entrance_Ramp/Entrance_RampWalk").GetComponent<Collider>();
                apron = entrance.Find("Entrance_Apron/Entrance_ApronWalk").GetComponent<Collider>();
                var local = ((MeshCollider)ramp).sharedMesh.vertices;
                var origin = ramp.transform;
                Vector3 first = origin.TransformPoint(local[0]), second = origin.TransformPoint(local[1]);
                top = (first + second) * 0.5f;
                along = Vector3.ProjectOnPlane(foot - top, Vector3.up).normalized;
                across = Vector3.Cross(along, Vector3.up);
                if (!terrain.isActiveAndEnabled || groundMask == 0)
                    throw new InvalidOperationException("The autumn terrain or Ground layer is unavailable.");
                foreach (Prop prop in assets) points.Add(prop.Kind, prop.Meshes[0].vertices);
            }

            internal void Slope()
            {
                float axis = -Mathf.Atan2(along.z, along.x) * Mathf.Rad2Deg;
                QueueSlope(Kind.WallChunk, Zone.Mouth, new[] { .035f, .067f, .095f, .131f, .168f },
                    new[] { -.76f, .77f, -.83f, -.75f, .82f }, axis, 0.70f, 2);
                QueueSlope(Kind.BambooLog, Zone.Mouth, new[] { .024f, .049f, .071f, .107f, .142f, .164f, .194f, .213f },
                    new[] { .56f, -.51f, .83f, .64f, -.72f, .56f, -.57f, .72f }, axis, 0.85f, 0);

                for (int cluster = 0; cluster < 5; cluster++)
                {
                    float t = .045f + cluster * .041f;
                    float sign = cluster % 2 == 0 ? -1f : 1f;
                    for (int rock = 0; rock < 5; rock++)
                        Place(Kind.Boulder, Zone.Mouth, Ramp(t + (rock - 2) * .013f, sign * (.72f + .10f * (rock % 2))),
                            axis, 0.69f, false);
                }

                QueueSlope(Kind.BambooLog, Zone.Scree, new[] { .287f, .365f, .454f, .553f, .681f, .814f },
                    new[] { -.79f, .82f, -.68f, .81f, -.80f, .74f }, axis, 0.92f, 0);
                for (int cluster = 0; cluster < 8; cluster++)
                {
                    float t = .276f + cluster * .078f;
                    float sign = cluster % 2 == 0 ? 1f : -1f;
                    for (int rock = 0; rock < 5; rock++)
                        Place(Kind.Boulder, Zone.Scree, Ramp(t + (rock - 2) * .015f, sign * (.66f + .14f * (rock % 2))),
                            axis, 0.78f, false);
                }

                Place(Kind.BambooStump, Zone.Scree, Ramp(.945f, -.71f), axis + 27f, .92f, false);
                Fill(Kind.Boulder, Zone.Mouth, 25, new[]
                {
                    Ramp(.230f, -.81f), Ramp(.225f, .79f), Ramp(.250f, -.63f), Ramp(.257f, .84f)
                }, axis, .65f);
            }

            internal void Clearing()
            {
                foreach (float angle in new[] { 11f, 131f })
                    Place(Kind.WallChunk, Zone.Clearing, Circle(.55f, 10.1f, angle), -angle + 63f, 0.91f, angle > 100f);
                foreach (float angle in new[] { 38f, 147f })
                    Place(Kind.BambooStump, Zone.Clearing, Circle(.55f, 9.8f, angle), -angle + 18f, 0.85f, false);
                foreach (float angle in new[] { 19f, 52f, 119f, 143f })
                    Place(Kind.BambooLog, Zone.Clearing, Circle(.55f, 8.85f, angle), 90f - angle, 1.02f, false);

                foreach (float angle in new[] { 15f, 59f, 118f, 137f })
                {
                    for (int i = 0; i < 3; i++)
                        Place(Kind.Boulder, Zone.Clearing, Circle(.55f, 9.8f - i * 0.33f, angle + i * 3.6f),
                            -angle, 0.70f, false);
                }

                foreach (float angle in new[] { 17f, 42f, 63f, 105f, 124f, 142f })
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float a = angle + (i - 2) * 4.15f;
                        float radius = i % 2 == 0 ? 10.8f : 9.5f;
                        Place(Kind.DryGrassClump, Zone.Clearing, Circle(.55f, radius, a), -a + 36f, 1.06f, false);
                    }
                }
            }

            internal void CompleteClearing()
            {
                Fill(Kind.BambooLog, Zone.Clearing, 4, new[]
                {
                    Circle(.55f, 8.20f, 108f), Circle(.55f, 8.0f, 61f), Circle(.55f, 10.1f, 120f)
                }, 103f, .79f);
                Fill(Kind.Boulder, Zone.Clearing, 12, new[]
                {
                    Circle(.55f, 8.50f, 132f), Circle(.55f, 9.95f, 128f), Circle(.55f, 9.52f, 24f),
                    Circle(.55f, 8.34f, 55f), Circle(.55f, 8.34f, 49f), Circle(.55f, 9.55f, 105f)
                }, 78f, .77f);
                Fill(Kind.DryGrassClump, Zone.Clearing, 30, new[]
                {
                    Circle(.55f, 9.01f, 28f), Circle(.55f, 7.75f, 42f), Circle(.55f, 8.25f, 58f),
                    Circle(.55f, 8.4f, 108f), Circle(.55f, 8.15f, 119f), Circle(.55f, 9.10f, 134f)
                }, 41f, 1.25f);
            }

            internal void Trail()
            {
                Place(Kind.BambooStump, Zone.Trail, Side(18.25f, -1f, 3.0f), 36f, 0.92f, false);
                Place(Kind.BambooStump, Zone.Trail, Side(37.5f, 1f, 3.05f), 116f, 0.92f, false);
                Place(Kind.BambooLog, Zone.Trail, Side(14.95f, 1f, 2.65f), 92f, 0.83f, false);
                Place(Kind.BambooLog, Zone.Trail, Side(25.5f, -1f, 2.80f), 102f, 0.86f, false);
                Place(Kind.BambooLog, Zone.Trail, Side(44.5f, 1f, 2.71f), 83f, 0.86f, false);

                for (int i = 0; i < 10; i++)
                    Place(Kind.Boulder, Zone.Trail, Side(15.6f + i * 3.72f, i % 2 == 0 ? -1f : 1f, 1.65f),
                        i * 37f, 0.84f, false);

                for (int i = 0; i < 15; i++)
                {
                    float z = 13.45f + i * 2.62f;
                    Place(Kind.DryGrassClump, Zone.Trail, Side(z, -1f, 2.01f), 17f + i * 37f, 0.59f, false);
                    Place(Kind.DryGrassClump, Zone.Trail, Side(z + .42f * Mathf.Sin(i * 1.39f), 1f, 2.04f),
                        104f + i * 29f, 0.59f, false);
                }
            }

            internal void CompleteTrail()
            {
                Fill(Kind.DryGrassClump, Zone.Trail, 30, new[]
                {
                    Side(45.68f, 1f, 2.08f), Side(43.0f, 1f, 2.14f), Side(50.85f, -1f, 1.94f)
                }, 62f, .58f);
            }

            internal void Arena()
            {
                foreach (float angle in new[] { 223f, 294f, 336f })
                    Place(Kind.BambooLog, Zone.ArenaEdge, Circle(188f, 28.7f, angle), 90f - angle, .84f, false);
                Place(Kind.BambooStump, Zone.ArenaEdge, Circle(188f, 28.85f, 244f), 86f, .84f, false);
                foreach (float angle in new[] { 224f, 245f, 298f, 329f })
                {
                    Place(Kind.Boulder, Zone.ArenaEdge, Circle(188f, 28.2f, angle - 1.8f), -angle, .65f, false);
                    Place(Kind.Boulder, Zone.ArenaEdge, Circle(188f, 28.7f, angle + 1.4f), -angle + 17f, .65f, false);
                }

                foreach (float angle in new[] { 224f, 292f, 334f })
                    Place(Kind.DryGrassClump, Zone.ArenaEdge, Circle(188f, 27.9f, angle), -angle, .69f, false);
            }

            void Fill(Kind kind, Zone zone, int goal, Vector3[] seats, float orientation, float search)
            {
                int existing = Results.FindAll(p => p.Zone == zone && p.Asset.Kind == kind).Count;
                foreach (Vector3 position in seats)
                {
                    if (existing >= goal) break;
                    int start = Results.Count;
                    Place(kind, zone, position, orientation, search, false);
                    existing += Results.Count - start;
                }
            }

            void QueueSlope(Kind kind, Zone zone, float[] at, float[] sides, float heading, float search, int laid)
            {
                for (int i = 0; i < at.Length; i++)
                    Place(kind, zone, Ramp(at[i], sides[i]), heading, search, i < laid);
            }

            Vector3 Ramp(float t, float side) =>
                Vector3.Lerp(top, foot, t) + across * (side * Mathf.Lerp(4.5f, 7.75f, t));

            static Vector3 Side(float z, float side, float distance) => new Vector3(CenterX + side * distance, 0f, z);

            static Vector3 Circle(float centerZ, float radius, float degrees)
            {
                float angle = degrees * Mathf.Deg2Rad;
                return new Vector3(CenterX + Mathf.Cos(angle) * radius, 0f, centerZ + Mathf.Sin(angle) * radius);
            }
        }
    }
}
