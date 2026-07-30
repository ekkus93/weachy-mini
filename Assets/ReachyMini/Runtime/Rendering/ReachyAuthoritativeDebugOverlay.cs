using System;
using ReachyMini.Presentation;
using UnityEngine;

namespace ReachyMini.Rendering
{
    [DefaultExecutionOrder(32001)]
    [DisallowMultipleComponent]
    public sealed class ReachyAuthoritativeDebugOverlay : MonoBehaviour
    {
        [SerializeField]
        private ReachyPresentationBody[] bodies =
            Array.Empty<ReachyPresentationBody>();

        [SerializeField]
        private float axisLengthMetres = 0.025f;

        [SerializeField]
        private bool showJointNames = true;

        private Camera presentationCamera;

        public int BodyCount => bodies.Length;

        public void ConfigureBodies(ReachyPresentationBody[] canonicalBodies)
        {
            if (canonicalBodies == null)
            {
                throw new ArgumentNullException(nameof(canonicalBodies));
            }
            if (canonicalBodies.Length == 0)
            {
                throw new ArgumentException(
                    "The debug overlay requires at least one body.",
                    nameof(canonicalBodies));
            }

            ReachyPresentationBody[] copy =
                new ReachyPresentationBody[canonicalBodies.Length];
            for (int index = 0; index < canonicalBodies.Length; ++index)
            {
                ReachyPresentationBody body = canonicalBodies[index];
                if (body == null || body.BodyIndex != index)
                {
                    throw new ArgumentException(
                        $"Debug-overlay body {index} is not the canonical " +
                        "presentation mapping.",
                        nameof(canonicalBodies));
                }
                copy[index] = body;
            }
            bodies = copy;
        }

        private void OnEnable()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            presentationCamera = Camera.main;
#endif
        }

        private void LateUpdate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (int index = 0; index < bodies.Length; ++index)
            {
                Transform bodyTransform = bodies[index].transform;
                Vector3 origin = bodyTransform.position;
                Debug.DrawLine(
                    origin,
                    origin + bodyTransform.right * axisLengthMetres,
                    Color.red,
                    0f,
                    false);
                Debug.DrawLine(
                    origin,
                    origin + bodyTransform.up * axisLengthMetres,
                    Color.green,
                    0f,
                    false);
                Debug.DrawLine(
                    origin,
                    origin + bodyTransform.forward * axisLengthMetres,
                    Color.blue,
                    0f,
                    false);
            }
#endif
        }

        private void OnGUI()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!showJointNames)
            {
                return;
            }
            if (presentationCamera == null)
            {
                presentationCamera = Camera.main;
                if (presentationCamera == null)
                {
                    return;
                }
            }

            for (int index = 0; index < bodies.Length; ++index)
            {
                string label = bodies[index].JointDebugLabel;
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                Vector3 screenPoint = presentationCamera.WorldToScreenPoint(
                    bodies[index].transform.position);
                if (screenPoint.z <= 0f)
                {
                    continue;
                }

                GUI.Label(
                    new Rect(
                        screenPoint.x + 4f,
                        Screen.height - screenPoint.y - 10f,
                        240f,
                        22f),
                    label);
            }
#endif
        }
    }
}
