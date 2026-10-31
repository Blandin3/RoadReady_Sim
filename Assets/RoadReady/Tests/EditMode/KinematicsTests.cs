using NUnit.Framework;
using RoadReady.Core;
using UnityEngine;

namespace RoadReady.Tests
{
    /// <summary>Time-to-collision maths behind the near-miss and gap-acceptance rules.</summary>
    public class KinematicsTests
    {
        [Test]
        public void HeadOnApproach_GivesTimeToReachTheOther()
        {
            // Car at 10 m/s, stationary pedestrian 20 m ahead.
            var ttc = Kinematics.TimeToCollision(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, new Vector3(20f, 0f, 0f), Vector3.zero, 1f);

            Assert.AreEqual(2f, ttc, 1e-4f);
        }

        [Test]
        public void PassingWideOfTheOther_IsNoCollision()
        {
            var ttc = Kinematics.TimeToCollision(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, new Vector3(20f, 0f, 10f), Vector3.zero, 1f);

            Assert.IsTrue(float.IsPositiveInfinity(ttc));
        }

        [Test]
        public void MovingApart_IsNoCollision()
        {
            var ttc = Kinematics.TimeToCollision(Vector3.zero, new Vector3(-10f, 0f, 0f), 1f, new Vector3(20f, 0f, 0f), Vector3.zero, 1f);

            Assert.IsTrue(float.IsPositiveInfinity(ttc));
        }

        [Test]
        public void HeightIsIgnored()
        {
            var ttc = Kinematics.TimeToCollision(Vector3.zero, new Vector3(10f, 0f, 0f), 1f, new Vector3(20f, 5f, 0f), Vector3.zero, 1f);

            Assert.AreEqual(2f, ttc, 1e-4f);
        }

        [Test]
        public void SignedYaw_IsPositiveToTheRight()
        {
            Assert.AreEqual(90f, Kinematics.SignedYaw(Vector3.forward, Vector3.right), 1e-3f);
            Assert.AreEqual(-90f, Kinematics.SignedYaw(Vector3.forward, Vector3.left), 1e-3f);
        }
    }
}
