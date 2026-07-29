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
            return new Vector3(
                checked((float)pose.PositionX),
                checked((float)pose.PositionZ),
                checked((float)pose.PositionY));
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
            return new Quaternion(
                checked((float)(-pose.QuaternionX * inverseNorm)),
                checked((float)(-pose.QuaternionZ * inverseNorm)),
                checked((float)(-pose.QuaternionY * inverseNorm)),
                checked((float)(pose.QuaternionW * inverseNorm)));
        }
    }
}
