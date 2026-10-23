using UnityEngine;

namespace SortOrStay
{
    /// Procedural two-arm reach for the Rocketbox avatars (generic rig, so no built-in humanoid IK).
    /// After the Animator has posed the body, a two-bone solve bends upper arm and forearm so each hand
    /// lands on its target, blended in and out so the arms ease off the idle pose and back.
    /// Puzzles call Hold() to grab something with one hand (the hand follows it while it moves) and Release() to let go.
    [DefaultExecutionOrder(100)]          // after PlayerController.LateUpdate has placed the hips
    public class HandReach : MonoBehaviour
    {
        public const int Right = 0, Left = 1;

        public float blendTime = 0.28f;   // seconds to reach out / pull back

        static readonly string[] Prefix = { "Bip01 R ", "Bip01 L " };

        PlayerController pc;
        Transform[,] upper, lower, hand;  // [avatar, side]
        readonly Transform[] target = new Transform[2];
        readonly Vector3[] offset = new Vector3[2];
        readonly float[] weight = new float[2];
        readonly bool[] holding = new bool[2];

        public bool IsHolding => holding[Right] || holding[Left];
        /// 0 = animated pose, 1 = hand on the target.
        public float Weight(int side) => weight[side];

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            int n = pc != null && pc.avatars != null ? pc.avatars.Length : 0;
            upper = new Transform[n, 2];
            lower = new Transform[n, 2];
            hand = new Transform[n, 2];
            for (int i = 0; i < n; i++)
            {
                if (pc.avatars[i] == null) continue;
                for (int s = 0; s < 2; s++)
                {
                    upper[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "UpperArm");
                    lower[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Forearm");
                    hand[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Hand");
                }
            }
        }

        /// The current avatar's hand bone on that side (things carried in the hand follow it).
        public Transform HandBone(int side)
        {
            int a = pc != null ? pc.AvatarIndex : 0;
            return a < hand.GetLength(0) ? hand[a, side] : null;
        }

        /// The hand on the same side of the body as this point (right hand for things on the avatar's right).
        public int SideFor(Vector3 point)
        {
            if (pc == null) return Right;
            Transform body = pc.Body;
            return Vector3.Dot(point - body.position, body.right) >= 0f ? Right : Left;
        }

        /// Reach for 't' with one hand and keep it there (plus a world-space offset) until Release().
        public void Hold(int side, Transform t, Vector3 worldOffset)
        {
            target[side] = t;
            offset[side] = worldOffset;
            holding[side] = t != null;
        }

        public void Release(int side) { holding[side] = false; }

        public void Release()
        {
            holding[Right] = false;
            holding[Left] = false;
        }

        void LateUpdate()
        {
            if (pc == null) return;
            int a = pc.AvatarIndex;
            Vector3 faceSum = Vector3.zero;
            int faceCount = 0;

            for (int s = 0; s < 2; s++)
            {
                weight[s] = Mathf.MoveTowards(weight[s], holding[s] ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, blendTime));
                if (weight[s] <= 0f) { target[s] = null; continue; }
                if (target[s] == null) { holding[s] = false; continue; }
                if (a >= upper.GetLength(0) || upper[a, s] == null || lower[a, s] == null || hand[a, s] == null) continue;

                Vector3 goal = target[s].position + offset[s];
                if (holding[s]) { faceSum += goal; faceCount++; }
                Solve(s, upper[a, s], lower[a, s], hand[a, s], goal, Mathf.SmoothStep(0f, 1f, weight[s]));
            }

            if (faceCount > 0 && !pc.firstPerson && pc.Speed < 0.1f) pc.FaceTowards(faceSum / faceCount);
        }

        /// Each elbow hangs down and out to its own side.
        void Solve(int side, Transform up, Transform low, Transform h, Vector3 goal, float w)
        {
            Transform body = pc.Body;
            float outward = side == Right ? 0.6f : -0.6f;
            TwoBoneIK.Solve(up, low, h, goal, -body.up + body.right * outward - body.forward * 0.2f, w);
        }
    }
}
