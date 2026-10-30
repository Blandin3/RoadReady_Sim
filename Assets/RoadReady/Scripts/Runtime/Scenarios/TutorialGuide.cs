using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RoadReady.Traffic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// Tutorial guidance: a circle on the road ahead of the learner and, floating in the air right in front of them
    /// (not drawn on the road), the direction animation (the scrolling arrows of the Animation_Textures pack) pointing
    /// to it. Driving up to the circle buzzes the controllers and makes it disappear, and it reappears further along
    /// the lane at another distance in view. Walking learners get a smaller circle on the spot they have to reach.
    /// The artwork is the "RoadReadyGuide" prefab in a Resources folder (built by the editor menu RoadReady >
    /// Build Tutorial Guide Prefab) with children Circle, Straight, TurnLeft and TurnRight, each lying flat with
    /// its forward (+Z) along the way to go.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        public const string ResourceName = "RoadReadyGuide";

        static readonly float[] k_Distances = { 32f, 44f, 26f }; // metres ahead of the learner, cycling
        const float k_ReachRadius = 3.5f;
        const float k_WalkReachRadius = 1.2f;
        const float k_TurnAngle = 12f;
        const float k_FloatDistance = 4f;     // metres in front of the eyes
        const float k_FloatDrop = 0.15f;      // and a little below the line of sight
        const float k_FloatScale = 0.17f;     // the prefab's arrows are 7.5 m long
        const float k_BendOfTurnArrows = 35f; // as built into the TurnLeft / TurnRight art
        const float k_RingDiameter = 7f;      // the circle in the prefab (TutorialGuideKit.k_CircleDiameter)
        const float k_LookDownSin = 0.574f;   // sin(35 degrees)

        GameObject m_Root, m_Circle, m_Straight, m_TurnLeft, m_TurnRight;
        Func<Vector3> m_Position, m_Forward;
        Transform m_FixedTarget;
        readonly List<Vector3> m_Lane = new List<Vector3>();
        float[] m_Cumulative;
        float m_Length;
        bool m_Loop;
        int m_NearestIndex;
        float m_TargetS;
        Vector3 m_TargetPosition;
        float m_Scale = 1f;
        int m_Cycle;
        bool m_Running, m_Waiting;
        float m_ReachRadius;
        GameObject m_Arrows;
        Transform m_Viewer;

        /// <summary>How many circles the learner has reached so far.</summary>
        public int ReachedCount { get; private set; }
        public bool IsRunning => m_Running;

        /// <summary>Driving: circles follow the lane the car is in.</summary>
        public bool BeginDriving(Func<Vector3> position, Func<Vector3> forward)
        {
            m_Position = position;
            m_Forward = forward;
            m_Scale = 1f;
            m_ReachRadius = k_ReachRadius;
            if (!Create() || !BuildLane(position(), forward()))
            {
                End();
                return false;
            }

            m_ReachRadius = k_ReachRadius * m_Scale;

            m_Running = true;
            PlaceNext(FollowerS());
            return true;
        }

        /// <summary>Walking: one smaller circle on the target, with the arrows pointing the way.</summary>
        public bool BeginWalking(Func<Vector3> position, Transform target)
        {
            m_Position = position;
            m_FixedTarget = target;
            m_Scale = 0.3f;
            m_ReachRadius = k_WalkReachRadius;
            if (target == null || !Create())
            {
                End();
                return false;
            }

            m_Running = true;
            ShowCircle(GroundPoint(target.position));
            return true;
        }

        public void End()
        {
            m_Running = false;
            if (m_Root != null)
                Destroy(m_Root);
            m_Root = null;
        }

        void OnDestroy() => End();

        bool Create()
        {
            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"[RoadReady] Tutorial guide prefab '{ResourceName}' not found in a Resources folder: run RoadReady > Build Tutorial Guide Prefab.");
                return false;
            }

            m_Root = Instantiate(prefab);
            m_Root.name = "Tutorial Guide";
            m_Circle = Child("Circle");
            m_Straight = Child("Straight");
            m_TurnLeft = Child("TurnLeft");
            m_TurnRight = Child("TurnRight");
            foreach (var part in new[] { m_Circle, m_Straight, m_TurnLeft, m_TurnRight })
                if (part != null)
                    part.SetActive(false);
            return m_Circle != null;
        }

        GameObject Child(string name)
        {
            var t = m_Root.transform.Find(name);
            return t != null ? t.gameObject : null;
        }

        // ------------------------------------------------------------------ lane

        /// <summary>The lane the learner is in, as a line along the carriageway zones.</summary>
        bool BuildLane(Vector3 position, Vector3 forward)
        {
            var zones = RoadZone.All.Where(z => z.Type == RoadZoneType.Carriageway).ToList();
            if (zones.Count < 3)
                return false;
            var nearest = zones.OrderBy(z => (z.transform.position - position).sqrMagnitude).First();
            var chain = zones.Where(z => z.transform.parent == nearest.transform.parent).OrderBy(z => z.transform.GetSiblingIndex()).ToList();
            if (chain.Count < 3)
                return false;

            // Down the middle of the road (the car starts on one side of it, which put the circle half on the kerb),
            // with the ring made to fit inside the carriageway.
            var points = chain.Select(z => z.transform.position).ToList();
            m_Scale = Mathf.Clamp((nearest.Size.x - 1.5f) / k_RingDiameter, 0.45f, 1f);
            var at = chain.IndexOf(nearest);
            if (Vector3.Dot(points[(at + 1) % points.Count] - points[at], forward) < 0f)
                points.Reverse();

            m_Lane.Clear();
            m_Lane.AddRange(points);
            var closing = Vector3.Distance(points[0], points[points.Count - 1]);
            var step = 0f;
            for (var i = 1; i < points.Count; i++)
                step += Vector3.Distance(points[i - 1], points[i]);
            step /= points.Count - 1;
            m_Loop = closing < step * 2.5f;

            m_Cumulative = new float[m_Lane.Count + 1];
            for (var i = 1; i <= m_Lane.Count; i++)
            {
                var a = m_Lane[i - 1];
                var b = m_Lane[i % m_Lane.Count];
                m_Cumulative[i] = m_Cumulative[i - 1] + Vector3.Distance(a, b);
            }

            m_Length = m_Cumulative[m_Lane.Count];
            m_NearestIndex = NearestIndex(position, -1);
            return true;
        }

        int NearestIndex(Vector3 position, int around)
        {
            var best = 0;
            var bestDistance = float.MaxValue;
            var n = m_Lane.Count;
            var from = around < 0 ? 0 : around - 40;
            var to = around < 0 ? n - 1 : around + 40;
            for (var k = from; k <= to; k++)
            {
                var i = ((k % n) + n) % n;
                var d = (m_Lane[i] - position).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }

            return best;
        }

        float FollowerS()
        {
            m_NearestIndex = NearestIndex(m_Position(), m_NearestIndex);
            return m_Cumulative[m_NearestIndex];
        }

        /// <summary>Lane point and travel direction at a distance along the lane (wrapping on a loop).</summary>
        bool Sample(float s, out Vector3 point, out Vector3 direction)
        {
            point = Vector3.zero;
            direction = Vector3.forward;
            if (m_Loop)
                s = Mathf.Repeat(s, m_Length);
            else if (s < 0f || s > m_Length - 0.5f)
                return false;

            var lo = 0;
            var hi = m_Lane.Count - 1;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (m_Cumulative[mid] <= s) lo = mid;
                else hi = mid - 1;
            }

            var a = m_Lane[lo];
            var b = m_Lane[(lo + 1) % m_Lane.Count];
            var segment = Mathf.Max(0.001f, m_Cumulative[lo + 1] - m_Cumulative[lo]);
            point = Vector3.Lerp(a, b, (s - m_Cumulative[lo]) / segment);
            direction = b - a;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.forward;
            return true;
        }

        // ------------------------------------------------------------------ placing

        void PlaceNext(float followerS)
        {
            // Another distance each time, always a viewable stretch ahead.
            var distance = k_Distances[m_Cycle++ % k_Distances.Length];
            var targetS = followerS + distance;
            if (!Sample(targetS, out var point, out var direction))
            {
                // Out of road (a road that does not loop): use the farthest point still ahead.
                targetS = m_Length - 1f;
                if (targetS - followerS < 8f || !Sample(targetS, out point, out direction))
                {
                    HideAll();
                    return;
                }
            }

            m_TargetS = targetS;
            ShowCircle(GroundPoint(point));

            // Arrows floating in front of the driver, bending the way the road does beyond the circle.
            if (!Sample(targetS + 24f, out _, out var beyond))
                beyond = direction;
            var turn = Vector3.SignedAngle(direction, beyond, Vector3.up);
            var arrows = Mathf.Abs(turn) < k_TurnAngle ? m_Straight : turn > 0f ? m_TurnRight : m_TurnLeft;
            ShowArrows(arrows);
        }

        void ShowCircle(Vector3 position)
        {
            m_TargetPosition = position;
            m_Circle.transform.position = position + Vector3.up * 0.03f;
            m_Circle.transform.rotation = Quaternion.identity;
            m_Circle.transform.localScale = Vector3.one * m_Scale;
            m_Circle.SetActive(true);
            m_Waiting = false;
            StartCoroutine(Pop(m_Circle.transform, 0f, m_Scale, 0.3f, false));
        }

        void ShowArrows(GameObject arrows)
        {
            m_Arrows = arrows;
            foreach (var part in new[] { m_Straight, m_TurnLeft, m_TurnRight })
                if (part != null)
                    part.SetActive(part == arrows);
            PoseFloatingArrows();
        }

        Transform Viewer
        {
            get
            {
                if (m_Viewer == null && Camera.main != null)
                    m_Viewer = Camera.main.transform;
                return m_Viewer;
            }
        }

        /// <summary>
        /// Holds the arrows in the air in front of the learner, facing them, pointing towards the circle. Their yaw
        /// follows the car (or the walking direction), not the head, so looking around does not drag them along.
        /// </summary>
        void PoseFloatingArrows()
        {
            var viewer = Viewer;
            if (m_Arrows == null || !m_Arrows.activeSelf || viewer == null || m_Position == null)
                return;

            var heading = m_Forward != null ? m_Forward() : viewer.forward;
            heading.y = 0f;
            heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : Vector3.forward;
            var position = viewer.position + heading * k_FloatDistance - Vector3.up * k_FloatDrop;

            var toTarget = m_TargetPosition - m_Position();
            toTarget.y = 0f;
            var bearing = toTarget.sqrMagnitude > 0.01f ? Vector3.SignedAngle(heading, toTarget, Vector3.up) : 0f;
            bearing = Mathf.Clamp(bearing, -80f, 80f);
            var built = m_Arrows == m_TurnLeft ? -k_BendOfTurnArrows : m_Arrows == m_TurnRight ? k_BendOfTurnArrows : 0f;

            // A panel facing the eyes: screen-up is "straight on", turned by the bearing (less the bend already in the art).
            var normal = (viewer.position - position).normalized;
            var up = Vector3.ProjectOnPlane(Vector3.up, normal).normalized;
            var right = Vector3.Cross(up, -normal);
            // The arrows are a floating plate seen almost edge-on to the ground, so a bearing on the road reads far
            // steeper on screen than it is (a circle 10 degrees off looks well to the side). Point them the way the
            // circle looks, as if they lay on the ground seen from about 35 degrees above it.
            var onScreen = Mathf.Atan(Mathf.Tan(bearing * Mathf.Deg2Rad) / k_LookDownSin) * Mathf.Rad2Deg;
            var angle = (onScreen - built) * Mathf.Deg2Rad;
            var way = up * Mathf.Cos(angle) + right * Mathf.Sin(angle);
            m_Arrows.transform.SetPositionAndRotation(position, Quaternion.LookRotation(way, normal));
            m_Arrows.transform.localScale = Vector3.one * (k_FloatScale * (m_FixedTarget != null ? 0.75f : 1f));
        }

        /// <summary>A short buzz in both controllers.</summary>
        static void Buzz(float amplitude, float duration)
        {
            var players = FindObjectsByType<HapticImpulsePlayer>(FindObjectsSortMode.None);
            foreach (var player in players)
                player.SendHapticImpulse(amplitude, duration);
            if (players.Length > 0)
                return;
            foreach (var interactor in FindObjectsByType<XRBaseInputInteractor>(FindObjectsSortMode.None))
                interactor.SendHapticImpulse(amplitude, duration);
        }

        void HideAll()
        {
            foreach (var part in new[] { m_Circle, m_Straight, m_TurnLeft, m_TurnRight })
                if (part != null)
                    part.SetActive(false);
        }

        /// <summary>Lays a point on the ground (the lane line is only as accurate as the road zones).</summary>
        static Vector3 GroundPoint(Vector3 point)
        {
            var best = point.y;
            var found = false;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * 4f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<IRoadAgent>() != null)
                    continue;
                if (!found || hit.point.y > best)
                {
                    best = hit.point.y;
                    found = true;
                }
            }

            return new Vector3(point.x, best, point.z);
        }

        // ------------------------------------------------------------------ running

        void LateUpdate()
        {
            if (m_Running)
                PoseFloatingArrows();
        }

        void Update()
        {
            if (!m_Running || m_Waiting || m_Position == null)
                return;

            var position = m_Position();
            var flat = position - m_TargetPosition;
            flat.y = 0f;

            if (m_FixedTarget != null)
            {
                UpdateWalkingArrows(position);
                if (flat.magnitude < m_ReachRadius)
                    StartCoroutine(Reached(false));
                return;
            }

            var followerS = FollowerS();
            if (flat.magnitude < m_ReachRadius)
            {
                StartCoroutine(Reached(true));
                return;
            }

            // Drove past without touching it (a wide road): bring the circle back in front of the learner.
            var passed = Mathf.Repeat(followerS - m_TargetS + m_Length * 0.5f, m_Length) - m_Length * 0.5f;
            if (m_Loop ? passed > 8f : followerS > m_TargetS + 8f)
                PlaceNext(followerS);
        }

        IEnumerator Reached(bool again)
        {
            m_Waiting = true;
            ReachedCount++;
            // You got there: a firm buzz, then a lighter echo.
            Buzz(0.9f, 0.25f);
            StartCoroutine(Echo());
            yield return Pop(m_Circle.transform, m_Scale, 0f, 0.25f, true);
            foreach (var part in new[] { m_Straight, m_TurnLeft, m_TurnRight })
                if (part != null)
                    part.SetActive(false);
            if (!again || !m_Running)
                yield break;
            yield return new WaitForSeconds(0.2f);
            m_Waiting = false;
            PlaceNext(FollowerS());
        }

        static IEnumerator Echo()
        {
            yield return new WaitForSecondsRealtime(0.32f);
            Buzz(0.5f, 0.15f);
        }

        void UpdateWalkingArrows(Vector3 position)
        {
            var to = m_TargetPosition - position;
            to.y = 0f;
            if (to.magnitude < 2.2f)
            {
                foreach (var part in new[] { m_Straight, m_TurnLeft, m_TurnRight })
                    if (part != null)
                        part.SetActive(false);
                return;
            }

            ShowArrows(m_Straight);
        }

        static IEnumerator Pop(Transform t, float from, float to, float seconds, bool hideAtEnd)
        {
            var time = 0f;
            while (time < seconds && t != null)
            {
                time += Time.unscaledDeltaTime;
                var k = Mathf.SmoothStep(0f, 1f, time / seconds);
                t.localScale = Vector3.one * Mathf.Lerp(from, to, k);
                yield return null;
            }

            if (t != null)
            {
                t.localScale = Vector3.one * to;
                if (hideAtEnd)
                    t.gameObject.SetActive(false);
            }
        }
    }
}
