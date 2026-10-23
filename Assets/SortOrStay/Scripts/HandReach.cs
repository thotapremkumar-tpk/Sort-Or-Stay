using UnityEngine;

namespace SortOrStay
{
    /// Procedural right-arm reach for the Rocketbox avatars (generic rig, so no built-in humanoid IK).
    /// After the Animator has posed the body, a two-bone solve bends upper arm and forearm so the hand
    /// lands on a target, blended in and out so the arm eases off the idle pose and back.
    /// Puzzles call Hold() to grab something (the hand follows it while it moves) and Release() to let go.
    [DefaultExecutionOrder(100)]          // after PlayerController.LateUpdate has placed the hips
    public class HandReach : MonoBehaviour
    {
        public float blendTime = 0.28f;   // seconds to reach out / pull back

        PlayerController pc;
        Transform[] upper, lower, hand;
        Transform target;
        Vector3 targetOffset;
        float weight;
        bool holding;

        public bool IsHolding => holding;
        /// 0 = animated pose, 1 = hand on the target.
        public float Weight => weight;

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            int n = pc != null && pc.avatars != null ? pc.avatars.Length : 0;
            upper = new Transform[n];
            lower = new Transform[n];
            hand = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                if (pc.avatars[i] == null) continue;
                upper[i] = FindDeep(pc.avatars[i], "Bip01 R UpperArm");
                lower[i] = FindDeep(pc.avatars[i], "Bip01 R Forearm");
                hand[i] = FindDeep(pc.avatars[i], "Bip01 R Hand");
            }
        }

        /// Reach for 't' and keep the hand on it (plus a world-space offset) until Release().
        public void Hold(Transform t, Vector3 offset)
        {
            target = t;
            targetOffset = offset;
            holding = t != null;
        }

        public void Release() { holding = false; }

        void LateUpdate()
        {
            weight = Mathf.MoveTowards(weight, holding ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, blendTime));
            if (weight <= 0f) { target = null; return; }
            if (target == null || pc == null) { holding = false; return; }

            int a = pc.AvatarIndex;
            if (a >= upper.Length || upper[a] == null || lower[a] == null || hand[a] == null) return;

            Vector3 goal = target.position + targetOffset;
            if (holding && !pc.firstPerson && pc.Speed < 0.1f) pc.FaceTowards(goal);
            Solve(upper[a], lower[a], hand[a], goal, Mathf.SmoothStep(0f, 1f, weight));
        }

        /// Two-bone solve: place the elbow with the law of cosines (bending down and out from the body),
        /// aim the forearm at the goal, then blend from the animated rotations by w.
        void Solve(Transform up, Transform low, Transform h, Vector3 goal, float w)
        {
            Quaternion upAnim = up.localRotation, lowAnim = low.localRotation;

            Vector3 a = up.position;
            float l1 = Vector3.Distance(a, low.position);
            float l2 = Vector3.Distance(low.position, h.position);
            Vector3 to = goal - a;
            float dist = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.995f);
            Vector3 dir = to.sqrMagnitude > 1e-6f ? to.normalized : pc.Body.forward;

            Transform body = pc.Body;
            Vector3 pole = -body.up + body.right * 0.6f - body.forward * 0.2f;   // elbow hangs down and out to the right
            Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
            bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : -body.up;

            float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float side = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 elbow = a + dir * along + bend * side;

            up.rotation = Quaternion.FromToRotation(low.position - a, elbow - a) * up.rotation;
            low.rotation = Quaternion.FromToRotation(h.position - low.position, a + dir * dist - low.position) * low.rotation;

            up.localRotation = Quaternion.Slerp(upAnim, up.localRotation, w);
            low.localRotation = Quaternion.Slerp(lowAnim, low.localRotation, w);
        }

        static Transform FindDeep(Transform root, string name)
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
