using System;
using System.Collections.Generic;
using System.IO;
using ReachyMini.Presentation;
using UnityEditor;
using UnityEngine;

namespace ReachyMini.Editor
{
    [InitializeOnLoad]
    public static class ReachyAuthoritativeDebugOverlay
    {
        private const string MenuPath =
            "Reachy Mini/Debug/Show Authoritative Body Axes and Joint Names";
        private const string SessionKey =
            "ReachyMini.AuthoritativeDebugOverlay.Enabled";
        private const string AuditRelativePath =
            "models/reachy-mini/model-parameter-audit.json";
        private const float AxisLengthMetres = 0.025f;

        private static bool enabled;
        private static string jointLabel = string.Empty;
        private static string loadFault = string.Empty;

        static ReachyAuthoritativeDebugOverlay()
        {
            enabled = SessionState.GetBool(SessionKey, false);
            Menu.SetChecked(MenuPath, enabled);
            if (enabled)
            {
                ReloadJointLabel();
            }
        }

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            enabled = !enabled;
            SessionState.SetBool(SessionKey, enabled);
            Menu.SetChecked(MenuPath, enabled);
            if (enabled)
            {
                ReloadJointLabel();
            }
            SceneView.RepaintAll();
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawAuthoritativeBody(
            ReachyPresentationBody body,
            GizmoType gizmoType)
        {
            _ = gizmoType;
            if (!enabled || body == null)
            {
                return;
            }

            Transform bodyTransform = body.transform;
            Vector3 origin = bodyTransform.position;
            Handles.color = Color.red;
            Handles.DrawLine(
                origin,
                origin + bodyTransform.right * AxisLengthMetres);
            Handles.color = Color.green;
            Handles.DrawLine(
                origin,
                origin + bodyTransform.up * AxisLengthMetres);
            Handles.color = Color.blue;
            Handles.DrawLine(
                origin,
                origin + bodyTransform.forward * AxisLengthMetres);

            string label = string.IsNullOrEmpty(body.BodyName)
                ? body.BodyPath
                : body.BodyName;
            Handles.Label(
                origin + bodyTransform.up * (AxisLengthMetres * 1.2f),
                label);
        }

        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawAuthoritativeRoot(
            ReachyPresentationRoot root,
            GizmoType gizmoType)
        {
            _ = gizmoType;
            if (!enabled || root == null)
            {
                return;
            }

            string text = string.IsNullOrEmpty(loadFault)
                ? jointLabel
                : $"Joint-name overlay unavailable: {loadFault}";
            Handles.Label(
                root.transform.position + Vector3.up * 0.34f,
                text);
        }

        private static void ReloadJointLabel()
        {
            jointLabel = string.Empty;
            loadFault = string.Empty;
            string auditPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                AuditRelativePath);
            try
            {
                string json = File.ReadAllText(auditPath);
                JointAudit audit = JsonUtility.FromJson<JointAudit>(json);
                if (audit == null || audit.joints == null ||
                    audit.joints.Length == 0)
                {
                    throw new InvalidDataException(
                        "The mechanical parameter audit contains no joints.");
                }

                HashSet<string> distinctNames =
                    new HashSet<string>(StringComparer.Ordinal);
                List<string> names = new List<string>(audit.joints.Length);
                for (int index = 0; index < audit.joints.Length; ++index)
                {
                    JointEntry entry = audit.joints[index];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.name))
                    {
                        throw new InvalidDataException(
                            $"Joint audit entry {index} has no name.");
                    }
                    if (!distinctNames.Add(entry.name))
                    {
                        throw new InvalidDataException(
                            $"Joint audit contains duplicate name {entry.name}.");
                    }
                    names.Add(entry.name);
                }

                jointLabel = BuildJointLabel(names);
            }
            catch (InvalidDataException exception)
            {
                RecordLoadFault(exception);
            }
            catch (IOException exception)
            {
                RecordLoadFault(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                RecordLoadFault(exception);
            }
            catch (ArgumentException exception)
            {
                RecordLoadFault(exception);
            }
        }

        private static void RecordLoadFault(Exception exception)
        {
            loadFault = exception.Message;
            Debug.LogError(
                $"Reachy authoritative debug overlay failed to load " +
                $"{AuditRelativePath}: {exception.Message}");
        }

        private static string BuildJointLabel(IReadOnlyList<string> names)
        {
            const int NamesPerLine = 4;
            List<string> lines = new List<string>();
            for (int start = 0; start < names.Count; start += NamesPerLine)
            {
                int count = Math.Min(NamesPerLine, names.Count - start);
                string[] lineNames = new string[count];
                for (int offset = 0; offset < count; ++offset)
                {
                    lineNames[offset] = names[start + offset];
                }
                lines.Add(string.Join(", ", lineNames));
            }
            return "Authoritative joints:\n" + string.Join("\n", lines);
        }

        [Serializable]
        private sealed class JointAudit
        {
            public JointEntry[] joints = Array.Empty<JointEntry>();
        }

        [Serializable]
        private sealed class JointEntry
        {
            public string name = string.Empty;
        }
    }
}
