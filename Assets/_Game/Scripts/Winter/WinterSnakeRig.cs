using System;
using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Winter
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Tan Shadow/Winter/Snake Rig")]
    public sealed class WinterSnakeRig : MonoBehaviour
    {
        [SerializeField] private Transform rigRoot;
        [SerializeField] private Transform head;
        [SerializeField] private Transform jaw;
        [SerializeField] private Transform rattleAnchor;
        [SerializeField] private Transform rattleTip;
        [SerializeField] private Transform[] bodyJoints;

        [SerializeField, HideInInspector] private Quaternion headBindRotation;
        [SerializeField, HideInInspector] private Quaternion jawBindRotation;
        [SerializeField, HideInInspector] private Vector3 jawClosingAxis;
        [SerializeField, HideInInspector] private Quaternion[] jointBindRotations;

        public Transform Root => rigRoot;
        public Transform Head => head;
        public Transform Jaw => jaw;
        public Transform RattleAnchor => rattleAnchor;
        public Transform RattleTip => rattleTip;

        /// <summary>Ordered from the first spine joint to the last tail joint.</summary>
        public IReadOnlyList<Transform> BodyJoints => bodyJoints;

        /// <summary>Sets the head rotation offset from its imported local bind pose.</summary>
        public void SetHeadRotation(Quaternion localOffset)
        {
            head.localRotation = headBindRotation * localOffset;
        }

        /// <summary>Positive degrees close the mouth. Zero restores the imported opening.</summary>
        public void SetJawAngle(float degrees)
        {
            float angle = Mathf.Clamp(degrees, -18f, 32f);
            jaw.localRotation = jawBindRotation * Quaternion.AngleAxis(angle, jawClosingAxis);
        }

        /// <summary>Sets the local rotation offset of one spine or tail joint.</summary>
        public void SetJointRotation(int index, Quaternion localOffset)
        {
            if ((uint)index >= (uint)bodyJoints.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            bodyJoints[index].localRotation = jointBindRotations[index] * localOffset;
        }

        /// <summary>Resets joint rotations while preserving the placement of the rig.</summary>
        public void ResetPose()
        {
            SetHeadRotation(Quaternion.identity);
            SetJawAngle(0f);
            for (int i = 0; i < bodyJoints.Length; i++)
                SetJointRotation(i, Quaternion.identity);
        }
    }
}
