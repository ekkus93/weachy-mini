#nullable enable

using ReachyMini.AppState;
using UnityEngine;

namespace ReachyMini.Rendering
{
    public static class ReachyCameraCalibrationUnityAdapter
    {
        public static Matrix4x4 ToUnityMatrix(ReachyMatrix3x3 matrix)
        {
            Matrix4x4 result = Matrix4x4.identity;
            result.m00 = (float)matrix.M00;
            result.m01 = (float)matrix.M01;
            result.m02 = (float)matrix.M02;
            result.m10 = (float)matrix.M10;
            result.m11 = (float)matrix.M11;
            result.m12 = (float)matrix.M12;
            result.m20 = (float)matrix.M20;
            result.m21 = (float)matrix.M21;
            result.m22 = (float)matrix.M22;
            return result;
        }

        public static Quaternion ToUnityQuaternion(ReachyQuaternionD quaternion)
        {
            return new Quaternion(
                (float)quaternion.X,
                (float)quaternion.Y,
                (float)quaternion.Z,
                (float)quaternion.W);
        }

        public static ReachyQuaternionD ToCoreQuaternion(Quaternion quaternion)
        {
            return new ReachyQuaternionD(
                quaternion.x,
                quaternion.y,
                quaternion.z,
                quaternion.w);
        }
    }
}
