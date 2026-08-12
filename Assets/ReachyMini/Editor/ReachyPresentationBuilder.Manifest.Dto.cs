using System;

namespace ReachyMini.Editor
{
    public static partial class ReachyPresentationBuilder
    {
        [Serializable]
        private sealed class RenderManifest
        {
            public int schema_version;
            public SourceEntry source = new SourceEntry();
            public MeshEntry[] meshes = Array.Empty<MeshEntry>();
            public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
            public BodyEntry[] bodies = Array.Empty<BodyEntry>();
            public VisualGeomEntry[] visual_geoms = Array.Empty<VisualGeomEntry>();
            public SourceCameraEntry[] source_cameras =
                Array.Empty<SourceCameraEntry>();
            public PresentationEntry presentation = new PresentationEntry();
        }

        [Serializable]
        private sealed class SourceEntry
        {
            public string model_sha256 = string.Empty;
        }

        [Serializable]
        private sealed class MeshEntry
        {
            public string name = string.Empty;
            public string source_path = string.Empty;
            public string source_sha256 = string.Empty;
            public double[] source_scale = Array.Empty<double>();
            public bool scale_baked_into_vertices;
            public string output_path = string.Empty;
            public string output_sha256 = string.Empty;
            public int triangle_count;
        }

        [Serializable]
        private sealed class MaterialEntry
        {
            public string name = string.Empty;
            public double[] rgba = Array.Empty<double>();
        }

        [Serializable]
        private sealed class BodyEntry
        {
            public int index;
            public string name = string.Empty;
            public string path = string.Empty;
            public string parent_path = string.Empty;
            public PoseEntry local_pose_unity = new PoseEntry();
        }

        [Serializable]
        private sealed class VisualGeomEntry
        {
            public int index;
            public string path = string.Empty;
            public string body_path = string.Empty;
            public string mesh = string.Empty;
            public string mesh_output_path = string.Empty;
            public string material = string.Empty;
            public PoseEntry local_pose_unity = new PoseEntry();
        }

        [Serializable]
        private sealed class PoseEntry
        {
            public double[] position_metres = Array.Empty<double>();
            public double[] quaternion_wxyz = Array.Empty<double>();
        }

        [Serializable]
        private sealed class SourceCameraEntry
        {
            public string name = string.Empty;
            public bool included_in_presentation;
        }

        [Serializable]
        private sealed class PresentationEntry
        {
            public bool source_cameras_included;
            public string authoritative_transform_source = string.Empty;
        }
    }
}
