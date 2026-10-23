using UnityEngine;

namespace SortOrStay
{
    /// Jump poses for the avatars (there is no jump clip): the hips sink while the feet stay planted for the
    /// crouch before take-off and the landing; in the air the feet tuck up, the arms come up and out, and a
    /// leap taken at a run splits the legs and arms front/back with the chest leaning into it.
    [DefaultExecutionOrder(90)]           // after PlayerController.LateUpdate, before HandReach
    public class LegPose : MonoBehaviour
    {
        public float crouchDepth = 0.13f; // metres the hips drop at a full crouch
        public float tuckLift = 0.2f;     // metres the feet rise at a full tuck
        public float strideReach = 0.28f; // metres the leading foot reaches forward in a running leap
        public float leanDegrees = 14f;   // chest lean into a running leap / a hard landing

        static readonly string[] Prefix = { "Bip01 R ", "Bip01 L " };

        PlayerController pc;
        Transform[,] thigh, calf, foot, upperArm, forearm, hand;   // [avatar, side]
        Transform[] spine;

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            int n = pc != null && pc.avatars != null ? pc.avatars.Length : 0;
            thigh = new Transform[n, 2];
            calf = new Transform[n, 2];
            foot = new Transform[n, 2];
            upperArm = new Transform[n, 2];
            forearm = new Transform[n, 2];
            hand = new Transform[n, 2];
            spine = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                if (pc.avatars[i] == null) continue;
                spine[i] = TwoBoneIK.FindDeep(pc.avatars[i], "Bip01 Spine1") ?? TwoBoneIK.FindDeep(pc.avatars[i], "Bip01 Spine");
                for (int s = 0; s < 2; s++)
                {
                    thigh[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Thigh");
                    calf[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Calf");
                    foot[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Foot");
                    upperArm[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "UpperArm");
                    forearm[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Forearm");
                    hand[i, s] = TwoBoneIK.FindDeep(pc.avatars[i], Prefix[s] + "Hand");
                }
            }
        }

        void LateUpdate()
        {
            if (pc == null) return;
            float crouch = pc.JumpCrouch, tuck = pc.JumpTuck, stride = pc.JumpStride, arms = pc.JumpArms;
            if (crouch <= 0.001f && tuck <= 0.001f && arms <= 0.001f) return;

            int a = pc.AvatarIndex;
            if (a >= thigh.GetLength(0) || pc.rootBones == null || a >= pc.rootBones.Length || pc.rootBones[a] == null) return;
            Transform body = pc.Body;
            Vector3 fwd = body.forward, right = body.right, up = Vector3.up;

            // where the animation put the feet, before the hips move
            var planted = new Vector3[2];
            for (int s = 0; s < 2; s++) if (foot[a, s] != null) planted[s] = foot[a, s].position;

            pc.rootBones[a].position += Vector3.down * (crouchDepth * crouch);

            // chest leans into a running leap and folds forward a little on a hard landing
            if (spine[a] != null)
            {
                float lean = leanDegrees * Mathf.Max(stride, crouch * 0.6f);
                if (lean > 0.01f) spine[a].rotation = Quaternion.AngleAxis(lean, right) * spine[a].rotation;
            }

            // legs: tucked in the air; in a leap the right leg reaches forward, the left trails behind
            for (int s = 0; s < 2; s++)
            {
                if (thigh[a, s] == null || calf[a, s] == null || foot[a, s] == null) continue;
                float split = s == HandReach.Right ? strideReach : -strideReach * 0.8f;
                float lift = tuckLift * tuck * (s == HandReach.Right ? 1f : 1.25f * stride + (1f - stride));
                Vector3 goal = planted[s] + up * lift + fwd * (split * stride);
                TwoBoneIK.Solve(thigh[a, s], calf[a, s], foot[a, s], goal, fwd, 1f);   // knees bend forward
            }

            // arms: up and out for balance; in a leap they swing opposite to the legs. A hand holding something is left alone.
            if (arms <= 0.001f) return;
            var holding = pc.Hand;
            for (int s = 0; s < 2; s++)
            {
                if (upperArm[a, s] == null || forearm[a, s] == null || hand[a, s] == null) continue;
                if (holding != null && holding.Weight(s) > 0.01f) continue;
                float outward = s == HandReach.Right ? 1f : -1f;
                float swingFwd = s == HandReach.Right ? -1f : 1f;       // opposite arm to the leading leg goes forward
                Vector3 shoulder = upperArm[a, s].position;
                Vector3 open = shoulder + right * (0.32f * outward) + up * 0.02f + fwd * 0.12f;              // standing hop
                Vector3 leap = shoulder + fwd * (0.34f * swingFwd) + up * (swingFwd > 0f ? 0.12f : -0.22f) + right * (0.16f * outward);
                Vector3 goal = Vector3.Lerp(open, leap, stride);
                TwoBoneIK.Solve(upperArm[a, s], forearm[a, s], hand[a, s], goal, -up + right * (0.6f * outward) - fwd * 0.3f, arms);
            }
        }
    }
}
