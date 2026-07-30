using UnityEngine;

namespace ReachyMini.Presentation
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ReachyPresentationCamera : MonoBehaviour
    {
        [SerializeField]
        private string framing = "fixed_front_three_quarter";

        [SerializeField]
        private bool acceptsUserNavigation;

        public string Framing => framing;

        public bool AcceptsUserNavigation => acceptsUserNavigation;

        public void ConfigureFixedPresentationCamera()
        {
            framing = "fixed_front_three_quarter";
            acceptsUserNavigation = false;
        }
    }
}
