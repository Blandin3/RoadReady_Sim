using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RoadReady.Traffic;
using UnityEngine;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// Tutorial guidance: a circle on the road ahead of the learner with the direction animation (the scrolling arrows
    /// of the Animation_Textures pack) just before it, showing where to head. Driving up to the circle makes it
    /// disappear, and it reappears further along the lane at another distance in view. Walking learners get a
    /// smaller circle on the spot they have to reach.
    /// The artwork is the "RoadReadyGuide" prefab in a Resources folder (built by the editor menu RoadReady >
    /// Build Tutorial Guide Prefab) with children Circle, Straight, TurnLeft and TurnRight, each lying flat with
    /// its forward (+Z) along the way to go.
    /// </summary>
    public class TutorialGuide : MonoBehaviour
    {
        public const string ResourceName = "RoadReadyGuide";

        static readonly float[] k_Distances = { 32f, 44f, 26f }; // metres ahead of the learner, cycling
        const float k_ArrowsBeforeCircle = 7f;
        const float k_ReachRadius = 3.5f;
        const float k_WalkReachRadius = 1.2f;
        const float k_TurnAngle = 12f;

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

            var half = nearest.Size.x * 0.5f;
            var lateral = Mathf.Clamp(Vector3.Dot(position - nearest.transform.position, nearest.transform.right), -half + 1.5f, half - 1.5f);
            var points = chain.Select(z => z.transform.position + z.transform.right * lateral).ToList();
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

            // Arrows just before the circle, bending the way the road does beyond it.
            if (!Sample(targetS - k_ArrowsBeforeCircle, out var arrowPoint, out var arrowDirection))
            {
                arrowPoint = point;
                arrowDirection = direction;
            }

            if (!Sample(targetS + 24f, out _, out var beyond))
                beyond = direction;
            var turn = Vector3.SignedAngle(direction, beyond, Vector3.up);
            var arrows = Mathf.Abs(turn) < k_TurnAngle ? m_Straight : turn > 0f ? m_TurnRight : m_TurnLeft;
            ShowArrows(arrows, GroundPoint(arrowPoint), arrowDirection);
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

        void ShowArrows(GameObject arrows, Vector3 position, Vector3 direction)
        {
            foreach (var part in new[] { m_Straight, m_TurnLeft, m_TurnRight })
                if (part != null)
                    part.SetActive(part == arrows);
            if (arrows == null)
                return;
            arrows.transform.position = position + Vector3.up * 0.03f;
            arrows.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            arrows.transform.localScale = Vector3.one * m_Scale;
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

            var direction = to.normalized;
            ShowArrows(m_Straight, GroundPoint(position + direction * Mathf.Min(2.5f, to.magnitude * 0.45f)), direction);
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
