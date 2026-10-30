using UnityEngine;

namespace RoadReady.Scenarios
{
    /// <summary>
    /// Marks a solid kerb or wheel stop in the parking scenario. Bumping it is a parking fault (points off), not a
    /// crash, so the collision rule ignores these colliders.
    /// </summary>
    public class ParkingKerb : MonoBehaviour
    {
    }
}
