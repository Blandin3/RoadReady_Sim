using System.Collections.Generic;
using UnityEngine;

namespace RoadReady.Traffic
{
    /// <summary>
    /// Polyline lane/footpath made from child transforms (in order). NPC vehicles and pedestrians follow
    /// these; stop lines and crossings project onto them. Swap-in art does not affect it.
    /// </summary>
    [ExecuteAlways]
    public class WaypointPath : MonoBehaviour
    {
        [SerializeField] bool m_Loop;
        [SerializeField] Color m_GizmoColor = new Color(0.2f, 0.8f, 1f);
        [Tooltip("Lane half width used for corridor checks.")]
        [SerializeField] float m_LaneHalfWidth = 1.6f;

        readonly List<Vector3> m_Points = new List<Vector3>();
        readonly List<float> m_Cumulative = new List<float>();
        int m_LastChildCount = -1;

        public bool Loop => m_Loop;
        public float LaneHalfWidth => m_LaneHalfWidth;
        public int PointCount { get { Rebuild(); return m_Points.Count; } }

        public float Length
        {
            get
            {
                Rebuild();
                return m_Cumulative.Count > 0 ? m_Cumulative[m_Cumulative.Count - 1] : 0f;
            }
        }

        public Vector3 GetWaypoint(int index)
        {
            Rebuild();
            return m_Points[Mathf.Clamp(index, 0, m_Points.Count - 1)];
        }

        public float GetDistanceAtWaypoint(int index)
        {
            Rebuild();
            return m_Cumulative[Mathf.Clamp(index, 0, m_Cumulative.Count - 1)];
        }

        public void MarkDirty() => m_LastChildCount = -1;

        void OnTransformChildrenChanged() => MarkDirty();

        void Rebuild()
        {
            if (m_LastChildCount == transform.childCount && m_Points.Count > 0 && Application.isPlaying)
                return;
            m_LastChildCount = transform.childCount;
            m_Points.Clear();
            m_Cumulative.Clear();
            foreach (Transform child in transform)
                m_Points.Add(child.position);
            if (m_Loop && m_Points.Count > 2)
                m_Points.Add(m_Points[0]);

            var total = 0f;
            for (var i = 0; i < m_Points.Count; i++)
            {
                if (i > 0)
                    total += Vector3.Distance(m_Points[i - 1], m_Points[i]);
                m_Cumulative.Add(total);
            }
        }

        public Vector3 GetPoint(float distance)
        {
            Rebuild();
            if (m_Points.Count == 0) return transform.position;
            if (m_Points.Count == 1) return m_Points[0];
            distance = Wrap(distance);
            var i = FindSegment(distance);
            var segLength = m_Cumulative[i + 1] - m_Cumulative[i];
            var t = segLength > 1e-4f ? (distance - m_Cumulative[i]) / segLength : 0f;
            return Vector3.Lerp(m_Points[i], m_Points[i + 1], t);
        }

        public Vector3 GetTangent(float distance)
        {
            Rebuild();
            if (m_Points.Count < 2) return transform.forward;
            var i = FindSegment(Wrap(distance));
            var dir = m_Points[i + 1] - m_Points[i];
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
        }

        /// <summary>Distance along the path of the closest point to <paramref name="position"/>.</summary>
        public float GetClosestDistance(Vector3 position, out float lateralDistance)
        {
            Rebuild();
            lateralDistance = float.PositiveInfinity;
            var best = 0f;
            for (var i = 0; i < m_Points.Count - 1; i++)
            {
                var a = m_Points[i];
                var b = m_Points[i + 1];
                var ab = b - a;
                var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(position - a, ab) / ab.sqrMagnitude) : 0f;
                var p = a + ab * t;
                var d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(position.x, position.z));
                if (d < lateralDistance)
                {
                    lateralDistance = d;
                    best = m_Cumulative[i] + ab.magnitude * t;
                }
            }

            return best;
        }

        float Wrap(float distance)
        {
            var length = m_Cumulative[m_Cumulative.Count - 1];
            if (m_Loop && length > 0f)
                return Mathf.Repeat(distance, length);
            return Mathf.Clamp(distance, 0f, length);
        }

        int FindSegment(float distance)
        {
            for (var i = 0; i < m_Cumulative.Count - 1; i++)
            {
                if (distance <= m_Cumulative[i + 1])
                    return i;
            }

            return m_Cumulative.Count - 2;
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                MarkDirty();
            Rebuild();
            Gizmos.color = m_GizmoColor;
            for (var i = 0; i < m_Points.Count - 1; i++)
            {
                Gizmos.DrawLine(m_Points[i] + Vector3.up * 0.1f, m_Points[i + 1] + Vector3.up * 0.1f);
                var mid = (m_Points[i] + m_Points[i + 1]) * 0.5f + Vector3.up * 0.1f;
                var dir = (m_Points[i + 1] - m_Points[i]).normalized;
                if (dir.sqrMagnitude > 0f)
                {
                    var right = Vector3.Cross(Vector3.up, dir) * 0.4f;
                    Gizmos.DrawLine(mid, mid - dir * 0.8f + right);
                    Gizmos.DrawLine(mid, mid - dir * 0.8f - right);
                }
            }

            foreach (var p in m_Points)
                Gizmos.DrawSphere(p + Vector3.up * 0.1f, 0.15f);
        }
    }
}
