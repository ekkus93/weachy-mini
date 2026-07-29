#nullable enable

using System;
using UnityEngine;

namespace ReachyMini.Rendering
{
    public static class ReachyCoordinateConverter
    {
        public static Vector3 ToUnityPosition(
            ReachyMujocoBodyPose pose)
        {
            Vector3 position = new Vector3(
                (float)pose.PositionX,
                (float)pose.PositionZ,
                (float)pose.PositionY);
            if (!IsFinite(position.x) ||
                !IsFinite(position.y) ||
                !IsFinite(position.z))
            {
                throw new ArgumentException(
                    "MuJoCo body position cannot be represented as finite Unity floats.",
                    nameof(pose));
            }
            return position;
        }

        public static Quaternion ToUnityRotation(
            ReachyMujocoBodyPose pose)
        {
            double norm = Math.Sqrt(
                pose.QuaternionW * pose.QuaternionW +
                pose.QuaternionX * pose.QuaternionX +
                pose.QuaternionY * pose.QuaternionY +
                pose.QuaternionZ * pose.QuaternionZ);
            if (double.IsNaN(norm) ||
                double.IsInfinity(norm) ||
                norm <= 1.0e-12)
            {
                throw new ArgumentException(
                    "MuJoCo body pose has an invalid quaternion.",
                    nameof(pose));
            }

            double inverseNorm = 1.0 / norm;
            Quaternion rotation = new Quaternion(
                (float)(-pose.QuaternionX * inverseNorm),
                (float)(-pose.QuaternionZ * inverseNorm),
                (float)(-pose.QuaternionY * inverseNorm),
                (float)(pose.QuaternionW * inverseNorm));
            if (!IsFinite(rotation.x) ||
                !IsFinite(rotation.y) ||
                !IsFinite(rotation.z) ||
                !IsFinite(rotation.w))
            {
                throw new ArgumentException(
                    "MuJoCo body rotation cannot be represented as finite Unity floats.",
                    nameof(pose));
            }
            return rotation;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
