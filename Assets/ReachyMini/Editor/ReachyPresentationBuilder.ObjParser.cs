using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
        private static Mesh ParseGeneratedObj(
            string path,
            string meshName,
            int expectedTriangleCount)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<ObjFace> faces = new List<ObjFace>();
            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(path))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' ||
                    line.StartsWith("o ", StringComparison.Ordinal))
                {
                    continue;
                }
                if (line.StartsWith("v ", StringComparison.Ordinal))
                {
                    vertices.Add(ParseVector3(line.Substring(2), path, lineNumber));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    normals.Add(ParseVector3(line.Substring(3), path, lineNumber));
                }
                else if (line.StartsWith("f ", StringComparison.Ordinal))
                {
                    faces.Add(ParseFace(line.Substring(2), path, lineNumber));
                }
                else
                {
                    throw new InvalidDataException(
                        $"Unsupported generated OBJ syntax at {path}:{lineNumber}: {line}");
                }
            }

            if (faces.Count != expectedTriangleCount ||
                vertices.Count != expectedTriangleCount * 3 ||
                normals.Count != expectedTriangleCount)
            {
                throw new InvalidDataException(
                    $"Generated OBJ counts do not match manifest for {meshName}.");
            }

            Vector3[] vertexArray = vertices.ToArray();
            Vector3[] normalArray = new Vector3[vertexArray.Length];
            int[] triangles = new int[faces.Count * 3];
            for (int index = 0; index < faces.Count; index++)
            {
                ObjFace face = faces[index];
                ValidateObjIndex(face.VertexA, vertexArray.Length, path);
                ValidateObjIndex(face.VertexB, vertexArray.Length, path);
                ValidateObjIndex(face.VertexC, vertexArray.Length, path);
                ValidateObjIndex(face.Normal, normals.Count, path);
                int a = face.VertexA - 1;
                int b = face.VertexB - 1;
                int c = face.VertexC - 1;
                Vector3 normal = normals[face.Normal - 1];
                normalArray[a] = normal;
                normalArray[b] = normal;
                normalArray[c] = normal;
                triangles[index * 3] = a;
                triangles[index * 3 + 1] = b;
                triangles[index * 3 + 2] = c;
            }

            Mesh mesh = new Mesh
            {
                name = meshName,
                indexFormat = vertexArray.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
            };
            mesh.vertices = vertexArray;
            mesh.normals = normalArray;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Vector3 ParseVector3(
            string value,
            string path,
            int lineNumber)
        {
            string[] fields = value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3)
            {
                throw new InvalidDataException(
                    $"Generated OBJ vector must contain three values at " +
                    $"{path}:{lineNumber}.");
            }
            return new Vector3(
                ParseFiniteSingle(fields[0], path, lineNumber),
                ParseFiniteSingle(fields[1], path, lineNumber),
                ParseFiniteSingle(fields[2], path, lineNumber));
        }

        private static float ParseFiniteSingle(
            string value,
            string path,
            int lineNumber)
        {
            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out float result) ||
                float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new InvalidDataException(
                    $"Generated OBJ contains a non-finite number at " +
                    $"{path}:{lineNumber}.");
            }
            return result;
        }

        private static ObjFace ParseFace(
            string value,
            string path,
            int lineNumber)
        {
            string[] fields = value.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3)
            {
                throw new InvalidDataException(
                    $"Generated OBJ face must contain three vertices at " +
                    $"{path}:{lineNumber}.");
            }

            int[] vertices = new int[3];
            int normal = -1;
            for (int index = 0; index < fields.Length; index++)
            {
                string[] indices = fields[index].Split(
                    new[] { "//" },
                    StringSplitOptions.None);
                if (indices.Length != 2 ||
                    !int.TryParse(
                        indices[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out vertices[index]) ||
                    !int.TryParse(
                        indices[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int currentNormal))
                {
                    throw new InvalidDataException(
                        $"Generated OBJ face index is invalid at " +
                        $"{path}:{lineNumber}.");
                }
                if (normal < 0)
                {
                    normal = currentNormal;
                }
                else if (normal != currentNormal)
                {
                    throw new InvalidDataException(
                        $"Generated OBJ face uses inconsistent normals at " +
                        $"{path}:{lineNumber}.");
                }
            }
            return new ObjFace(
                vertices[0],
                vertices[1],
                vertices[2],
                normal);
        }

        private static void ValidateObjIndex(
            int value,
            int maximum,
            string path)
        {
            if (value <= 0 || value > maximum)
            {
                throw new InvalidDataException(
                    $"Generated OBJ index {value} is outside 1..{maximum}: {path}");
            }
        }

        private readonly struct ObjFace
        {
            public ObjFace(
                int vertexA,
                int vertexB,
                int vertexC,
                int normal)
            {
                VertexA = vertexA;
                VertexB = vertexB;
                VertexC = vertexC;
                Normal = normal;
            }

            public int VertexA { get; }

            public int VertexB { get; }

            public int VertexC { get; }

            public int Normal { get; }
        }
    }
}
