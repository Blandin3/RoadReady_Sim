using System.Collections.Generic;
using RoadReady.Hazards;
using RoadReady.Player;
using UnityEngine;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// The target bay of the reverse bay-parking scenario and everything the parking rule measures against: the
    /// painted side lines (a tyre on a line is a fault), cones (touching one is a fault), the pedestrian hazard that
    /// walks behind the car once it reverses, and the "all four wheels in the bay and straight" check.
    /// Kerbs / wheel stops are solid colliders marked with <see cref="ParkingKerb"/>.
    /// </summary>
    public class ParkingArea : MonoBehaviour
    {
        const float k_TyreHalfWidth = 0.11f;

        [Tooltip("Centre of the bay on the ground; +Z points into the bay (towards the kerb at its back).")]
        [SerializeField] Transform m_Bay;
        [Tooltip("Bay width (x) and length (z) in metres.")]
        [SerializeField] Vector2 m_BaySize = new Vector2(2.6f, 5.2f);
        [Tooltip("Painted lines a tyre must not touch. Each transform's scale x / z is the line's footprint.")]
        [SerializeField] List<Transform> m_Lines = new List<Transform>();
        [SerializeField] List<Transform> m_Cones = new List<Transform>();
        [SerializeField] float m_ConeRadius = 0.18f;
        [Tooltip("Pedestrian who walks behind the car once it starts reversing (Manual trigger).")]
        [SerializeField] HazardTrigger m_PedestrianHazard;

        public Transform Bay => m_Bay;
        public Vector2 BaySize => m_BaySize;
        public IReadOnlyList<Transform> Lines => m_Lines;
        public IReadOnlyList<Transform> Cones => m_Cones;
        public HazardTrigger PedestrianHazard => m_PedestrianHazard;

        /// <summary>Set by the parking rule once the car has been parked and secured long enough.</summary>
        public bool Completed { get; set; }

        public void Configure(Transform bay, Vector2 baySize, List<Transform> lines, List<Transform> cones, HazardTrigger pedestrianHazard)
        {
            m_Bay = bay;
            m_BaySize = baySize;
            m_Lines = lines;
            m_Cones = cones;
            m_PedestrianHazard = pedestrianHazard;
        }

        /// <summary>Ground contact points of the four tyres (front-left, front-right, rear-left, rear-right).</summary>
        public static Vector3[] WheelPoints(DriverController car)
        {
            var t = car.transform;
            var halfTrack = car.Width * 0.5f - 0.15f;
            var halfBase = car.Wheelbase * 0.5f;
            return new[]
            {
                t.TransformPoint(new Vector3(-halfTrack, 0f, halfBase)),
                t.TransformPoint(new Vector3(halfTrack, 0f, halfBase)),
                t.TransformPoint(new Vector3(-halfTrack, 0f, -halfBase)),
                t.TransformPoint(new Vector3(halfTrack, 0f, -halfBase)),
            };
        }

        public bool AllWheelsInside(DriverController car)
        {
            if (m_Bay == null)
                return false;
            foreach (var p in WheelPoints(car))
            {
                var local = m_Bay.InverseTransformPoint(p);
                if (Mathf.Abs(local.x) > m_BaySize.x * 0.5f || Mathf.Abs(local.z) > m_BaySize.y * 0.5f)
                    return false;
            }

            return true;
        }

        /// <summary>Angle between the car and the bay's long axis (either way round), in degrees.</summary>
        public float AngleToBay(Transform car)
        {
            if (m_Bay == null)
                return 180f;
            var angle = Vector3.Angle(Flat(car.forward), Flat(m_Bay.forward));
            return Mathf.Min(angle, 180f - angle);
        }

        /// <summary>Index of a painted line under a tyre, or -1.</summary>
        public int LineUnderTyre(DriverController car)
        {
            var wheels = WheelPoints(car);
            for (var i = 0; i < m_Lines.Count; i++)
            {
                var line = m_Lines[i];
                if (line == null)
                    continue;
                var half = new Vector2(line.lossyScale.x * 0.5f + k_TyreHalfWidth, line.lossyScale.z * 0.5f + k_TyreHalfWidth);
                foreach (var w in wheels)
                {
                    var local = Quaternion.Inverse(line.rotation) * (w - line.position);
                    if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y)
                        return i;
                }
            }

            return -1;
        }

        /// <summary>Index of a cone touching the car body, or -1.</summary>
        public int ConeTouching(DriverController car)
        {
            var t = car.transform;
            var half = new Vector2(car.Width * 0.5f, car.Length * 0.5f);
            for (var i = 0; i < m_Cones.Count; i++)
            {
                var cone = m_Cones[i];
                if (cone == null || !cone.gameObject.activeInHierarchy)
                    continue;
                var local = t.InverseTransformPoint(cone.position);
                var dx = Mathf.Max(0f, Mathf.Abs(local.x) - half.x);
                var dz = Mathf.Max(0f, Mathf.Abs(local.z) - half.y);
                if (dx * dx + dz * dz <= m_ConeRadius * m_ConeRadius)
                    return i;
            }

            return -1;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        void OnDrawGizmos()
        {
            if (m_Bay == null)
                return;
            Gizmos.color = Completed ? Color.green : Color.cyan;
            Gizmos.matrix = Matrix4x4.TRS(m_Bay.position, m_Bay.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_BaySize.x, 0.05f, m_BaySize.y));
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.yellow;
            foreach (var line in m_Lines)
                if (line != null)
                {
                    Gizmos.matrix = Matrix4x4.TRS(line.position, line.rotation, Vector3.one);
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(line.lossyScale.x, 0.02f, line.lossyScale.z));
                }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
