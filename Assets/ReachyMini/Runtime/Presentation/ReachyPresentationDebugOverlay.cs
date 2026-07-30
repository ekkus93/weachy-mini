using System;
using UnityEngine;

namespace ReachyMini.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ReachyPresentationDebugOverlay : MonoBehaviour
    {
        [SerializeField]
        private ReachyPresentationBody[] bodies =
            Array.Empty<ReachyPresentationBody>();

        [SerializeField]
        private string[] jointNames = Array.Empty<string>();

        [SerializeField]
        private ReachyPresentationBody[] jointBodies =
            Array.Empty<ReachyPresentationBody>();

        [SerializeField]
        private float axisLengthMetres = 0.025f;

        public int BodyCount => bodies.Length;

        public int JointCount => jointNames.Length;

        public bool IsVisible => enabled;

        public void ConfigureGeneratedOverlay(
            ReachyPresentationBody[] generatedBodies,
            string[] generatedJointNames,
            ReachyPresentationBody[] generatedJointBodies)
        {
            if (generatedBodies == null)
            {
                throw new ArgumentNullException(nameof(generatedBodies));
            }
            if (generatedJointNames == null)
            {
                throw new ArgumentNullException(nameof(generatedJointNames));
            }
            if (generatedJointBodies == null)
            {
                throw new ArgumentNullException(nameof(generatedJointBodies));
            }
            if (generatedBodies.Length == 0)
            {
                throw new ArgumentException(
                    "Debug overlay requires at least one body.",
                    nameof(generatedBodies));
            }
            if (generatedJointNames.Length != generatedJointBodies.Length)
            {
                throw new ArgumentException(
                    "Joint names and body bindings must have equal lengths.",
                    nameof(generatedJointBodies));
            }

            ReachyPresentationBody[] bodyCopy =
                new ReachyPresentationBody[generatedBodies.Length];
            Array.Copy(generatedBodies, bodyCopy, generatedBodies.Length);
            string[] nameCopy = new string[generatedJointNames.Length];
            Array.Copy(generatedJointNames, nameCopy, generatedJointNames.Length);
            ReachyPresentationBody[] jointBodyCopy =
                new ReachyPresentationBody[generatedJointBodies.Length];
            Array.Copy(
                generatedJointBodies,
                jointBodyCopy,
                generatedJointBodies.Length);

            for (int index = 0; index < bodyCopy.Length; ++index)
            {
                if (bodyCopy[index] == null)
                {
                    throw new ArgumentException(
                        $"Debug overlay body {index} is missing.",
                        nameof(generatedBodies));
                }
            }
            for (int index = 0; index < nameCopy.Length; ++index)
            {
                if (string.IsNullOrWhiteSpace(nameCopy[index]) ||
                    jointBodyCopy[index] == null)
                {
                    throw new ArgumentException(
                        $"Debug overlay joint {index} is malformed.",
                        nameof(generatedJointNames));
                }
            }

            bodies = bodyCopy;
            jointNames = nameCopy;
            jointBodies = jointBodyCopy;
            enabled = false;
        }

        public void SetVisible(bool visible)
        {
            enabled = visible;
        }

        private void LateUpdate()
        {
            float length = axisLengthMetres;
            for (int index = 0; index < bodies.Length; ++index)
            {
                Transform body = bodies[index].transform;
                Vector3 origin = body.position;
                Debug.DrawLine(
                    origin,
                    origin + body.right * length,
                    Color.red,
                    0f,
                    false);
                Debug.DrawLine(
                    origin,
                    origin + body.up * length,
                    Color.green,
                    0f,
                    false);
                Debug.DrawLine(
                    origin,
                    origin + body.forward * length,
                    Color.blue,
                    0f,
                    false);
            }
        }

        private void OnGUI()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            for (int index = 0; index < jointNames.Length; ++index)
            {
                Vector3 screen = camera.WorldToScreenPoint(
                    jointBodies[index].transform.position);
                if (screen.z <= 0f)
                {
                    continue;
                }
                Rect labelRect = new Rect(
                    screen.x + 4f,
                    Screen.height - screen.y - 10f,
                    180f,
                    20f);
                GUI.Label(labelRect, jointNames[index]);
            }
        }
    }
}
