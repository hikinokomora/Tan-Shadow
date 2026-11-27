using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class QinglongFoliage : MonoBehaviour
{
    [Serializable]
    public sealed class Species
    {
        public string name;
        public GameObject[] variants;
    }

    [SerializeField] private MeshCollider paintSurface;
    [SerializeField] private Transform instances;
    [SerializeField] private Species[] palette;

    public MeshCollider PaintSurface => paintSurface;
    public Transform Instances => instances;
    public Species[] Palette => palette;
}
