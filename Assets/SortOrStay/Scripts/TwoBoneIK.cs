using UnityEngine;

namespace SortOrStay
{
    /// Analytic two-bone IK for the generic Rocketbox rigs (arms and legs), applied on top of the animated pose.
    public static class TwoBoneIK
    {
        /// Place the middle joint with the law of cosines (bending toward 'pole'), aim the lower bone at the goal,
        /// then blend from the animated rotations by w.
        public static void Solve(Transform up, Transform low, Transform end, Vector3 goal, Vector3 pole, float w)
        {
            if (w <= 0f) return;
            Quaternion upAnim = up.localRotation, lowAnim = low.localRotation;

            Vector3 a = up.position;
            float l1 = Vector3.Distance(a, low.position);
            float l2 = Vector3.Distance(low.position, end.position);
            Vector3 to = goal - a;
            float dist = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.995f);
            Vector3 dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.down;

            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.ProjectOnPlane(Vector3.forward, dir).normalized;

            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float side = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 mid = a + dir * along + bend * side;

            up.rotation = Quaternion.FromToRotation(low.position - a, mid - a) * up.rotation;
            low.rotation = Quaternion.FromToRotation(end.position - low.position, a + dir * dist - low.position) * low.rotation;

            up.localRotation = Quaternion.Slerp(upAnim, up.localRotation, w);
            low.localRotation = Quaternion.Slerp(lowAnim, low.localRotation, w);
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
