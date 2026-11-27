using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnEntranceCave
    {
        static void BuildWall(Transform root, Materials materials, HashSet<string> meshes, HashSet<string> objects, Geometry structure)
        {
            Transform wall = Section(root, "Wall", objects);
            const float min = -105f, max = 105f, course = 1.75f, block = 3.65f;
            float left = RampTop.x - 2.5f, right = RampTop.x + 2.5f;
            AddPart("Left", min, left, 0f, 60f);
            AddPart("Right", right, max, 0f, 60f);
            AddPart("Sill", left, right, 0f, EntranceY);
            AddPart("Lintel", left, right, EntranceY + 5f, 60f);
            RemoveChildren(wall, Prefix + "WallCore");

            var facing = new Geometry(Vector3.zero, 1);
            var random = new Random(Seed);
            for (int row = 0; row * course < 60f; row++)
            {
                float y0 = row * course + 0.025f;
                float y1 = Mathf.Min(59.99f, (row + 1) * course - 0.022f);
                for (float x = min - block + row % 2 * (block * 0.54f); x < max; x += block)
                {
                    float x0 = Mathf.Max(min, x + 0.025f);
                    float x1 = Mathf.Min(max, x + block - 0.025f);
                    if (x0 >= x1) continue;
                    if (x1 <= left || x0 >= right || y1 <= EntranceY || y0 >= EntranceY + 5f)
                    {
                        Tile(x0, x1, y0, y1);
                    }
                    else
                    {
                        Tile(x0, Mathf.Min(x1, left), y0, y1);
                        Tile(Mathf.Max(x0, right), x1, y0, y1);
                        Tile(Mathf.Max(x0, left), Mathf.Min(x1, right), y0, Mathf.Min(y1, EntranceY));
                        Tile(Mathf.Max(x0, left), Mathf.Min(x1, right), Mathf.Max(y0, EntranceY + 5f), y1);
                    }
                }
            }

            Bake(wall, "WallFacing", facing, materials.Wall, ShadowCastingMode.On, meshes, SolidFlags);

            void AddPart(string name, float x0, float x1, float y0, float y1)
            {
                var center = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, MouthZ - 2f);
                var size = new Vector3(x1 - x0, y1 - y0, 4f);
                Box(structure, center, size, 5f);
                ColliderBox(wall, Prefix + "Wall_" + name, center, size, "Wall");
            }

            void Tile(float x0, float x1, float y0, float y1)
            {
                if (x1 - x0 < 0.004f || y1 - y0 < 0.004f) return;
                const float z = MouthZ + 0.022f;
                var uv = new Rect(Range(random, 0f, 1f), Range(random, 0f, 1f), (x1 - x0) / 4.8f, (y1 - y0) / 4.8f);
                facing.Quad(0, new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), uv);
            }
        }

        static Transform BuildTunnel(Transform root, Materials materials, HashSet<string> meshes, HashSet<string> objects, Geometry structure)
        {
            Transform tunnel = Section(root, "Tunnel", objects);
            const float thickness = 0.36f;
            float centerZ = MouthZ - TunnelLength * 0.5f;
            float x = RampTop.x;
            Part("Floor", new Vector3(x, EntranceY - thickness * 0.5f, centerZ),
                new Vector3(5f, thickness, TunnelLength), "Ground");
            Part("Ceiling", new Vector3(x, EntranceY + 5f + thickness * 0.5f, centerZ),
                new Vector3(5f, thickness, TunnelLength), "Wall");
            Part("Left", new Vector3(x - 2.5f - thickness * 0.5f, EntranceY + 2.5f, centerZ),
                new Vector3(thickness, 5f, TunnelLength), "Wall");
            Part("Right", new Vector3(x + 2.5f + thickness * 0.5f, EntranceY + 2.5f, centerZ),
                new Vector3(thickness, 5f, TunnelLength), "Wall");
            Part("ApproachFloor", new Vector3(x, EntranceY - thickness * 0.5f, MouthZ - TunnelLength - 1.15f),
                new Vector3(5f, thickness, 2.3f), "Ground");
            Part("ApproachLeft", new Vector3(x - 2.5f - thickness * 0.5f, EntranceY + 2.5f, MouthZ - TunnelLength - 1.15f),
                new Vector3(thickness, 5f, 2.3f), "Wall");
            Part("ApproachRight", new Vector3(x + 2.5f + thickness * 0.5f, EntranceY + 2.5f, MouthZ - TunnelLength - 1.15f),
                new Vector3(thickness, 5f, 2.3f), "Wall");
            Part("ReturnStop", new Vector3(x, EntranceY + 2.5f, MouthZ - TunnelLength - 2.45f),
                new Vector3(5f, 5f, 0.3f), "Wall");
            Part("ApproachCeiling", new Vector3(x, EntranceY + 5f + thickness * 0.5f, MouthZ - TunnelLength - 1.15f),
                new Vector3(5f, thickness, 2.3f), "Wall");
            Part("SlabPocketFront", new Vector3(x, EntranceY + 8.08f, MouthZ - TunnelLength + 0.75f),
                new Vector3(5.88f, 5.5f, 0.3f), "Wall");
            RemoveChildren(tunnel, Prefix + "TunnelShell");

            var lining = new Geometry(Vector3.zero, 1);
            for (int i = 0; i < 4; i++)
            {
                float front = MouthZ - i * 1.75f - 0.012f, back = front - 1.725f;
                for (int j = 0; j < 2; j++)
                {
                    float l = x - 2.50f + j * 2.5f + 0.012f, r = l + 2.475f;
                    float y = EntranceY + 0.012f;
                    lining.Quad(0, new Vector3(l, y, front), new Vector3(r, y, front),
                        new Vector3(r, y, back), new Vector3(l, y, back), new Rect(0f, 0f, 0.78f, 0.55f));
                    y = EntranceY + 4.983f;
                    lining.Quad(0, new Vector3(l, y, back), new Vector3(r, y, back),
                        new Vector3(r, y, front), new Vector3(l, y, front), new Rect(0.2f, 0.4f, 0.78f, 0.55f));
                }

                for (int row = 0; row < 3; row++)
                {
                    float bottom = EntranceY + row * 5f / 3f + 0.018f;
                    float top = bottom + 1.63f;
                    float xl = x - 2.48f, xr = x + 2.48f;
                    var uv = new Rect(i * 0.18f, row * 0.24f, 0.47f, 0.45f);
                    lining.Quad(0, new Vector3(xl, bottom, front), new Vector3(xl, bottom, back),
                        new Vector3(xl, top, back), new Vector3(xl, top, front), uv);
                    lining.Quad(0, new Vector3(xr, bottom, back), new Vector3(xr, bottom, front),
                        new Vector3(xr, top, front), new Vector3(xr, top, back), uv);
                }
            }

            Bake(tunnel, "TunnelLining", lining, materials.Tunnel, ShadowCastingMode.On, meshes, SolidFlags);
            var closed = new Vector3(x, EntranceY + 2.67f, MouthZ - TunnelLength + 0.3f);
            Transform slab = Node(tunnel, Prefix + "DoorSlab", closed);
            var doorMesh = new Geometry(Vector3.zero, 1);
            Vector3 size = new Vector3(5.2f, 5.4f, 0.6f);
            Box(doorMesh, Vector3.zero, size, 3.5f);
            Mesh mesh = SaveMesh($"{Folder}/{Prefix}DoorSlab.asset", Finish(doorMesh), meshes);
            Render(slab, mesh, new[] { materials.Wall }, ShadowCastingMode.On, 0);
            slab.gameObject.layer = LayerMask.NameToLayer("Wall");
            Component<BoxCollider>(slab.gameObject).size = size;
            var body = Component<Rigidbody>(slab.gameObject);
            body.isKinematic = true;
            body.useGravity = false;
            return slab;

            void Part(string name, Vector3 position, Vector3 partSize, string layer)
            {
                Box(structure, position, partSize, 3.5f);
                ColliderBox(tunnel, Prefix + "Tunnel_" + name, position, partSize, layer);
            }
        }

        static void BuildApron(Transform root, HashSet<string> meshes, HashSet<string> objects, Geometry bed)
        {
            Transform apron = Section(root, "Apron", objects);
            float x = RampTop.x;
            Vector3 left = new Vector3(x - 2.5f, EntranceY, MouthZ);
            Vector3 right = new Vector3(x + 2.5f, EntranceY, MouthZ);
            Vector3 corner = new Vector3(x - 3.25f, EntranceY, -5.85f);
            Vector3 lower = RampPoint(0f, -1f), upper = RampPoint(0f, 1f);
            var geometry = new Geometry(Vector3.zero, 1);
            Surface(geometry, left, corner, upper, lower, 3.2f);
            Surface(geometry, left, lower, right, right, 3.2f);
            Surface(bed, left, corner, upper, lower, 3.2f);
            Surface(bed, left, lower, right, right, 3.2f);
            RemoveChildren(apron, Prefix + "ApronFloor");
            Mesh floor = SaveMesh($"{Folder}/{Prefix}ApronWalk.asset", geometry.MakeMesh(), meshes);
            MeshCollision(apron, Prefix + "ApronWalk", floor, "Ground");
            Guard(apron, Prefix + "ApronLeft", left, corner, 2f);
            Guard(apron, Prefix + "ApronFront", corner, upper, 2f);
            Guard(apron, Prefix + "ApronRight", lower, right, 2f);
        }
    }
}
