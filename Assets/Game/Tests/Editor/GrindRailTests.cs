using NUnit.Framework;
using UnityEngine;

public class GrindRailTests
{
    private GameObject go;
    private GrindRail rail;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("RailTest");
        rail = go.AddComponent<GrindRail>();
        rail.localStart = Vector3.zero;
        rail.localEnd = new Vector3(0f, 0f, 10f);
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(go);

    [Test]
    public void LengthAndDirection()
    {
        Assert.AreEqual(10f, rail.Length, 0.0001f);
        TestUtil.AreClose(Vector3.forward, rail.Direction);
    }

    [Test]
    public void ClosestPoint_ProjectsOntoSegment()
    {
        Vector3 p = rail.ClosestPoint(new Vector3(3f, 2f, 4f), out float t);
        TestUtil.AreClose(new Vector3(0f, 0f, 4f), p);
        Assert.AreEqual(0.4f, t, 0.0001f);
    }

    [Test]
    public void ClosestPoint_ClampsToEnds()
    {
        Vector3 before = rail.ClosestPoint(new Vector3(0f, 0f, -5f), out float t0);
        Vector3 after = rail.ClosestPoint(new Vector3(0f, 0f, 25f), out float t1);

        TestUtil.AreClose(Vector3.zero, before);
        Assert.AreEqual(0f, t0);
        TestUtil.AreClose(new Vector3(0f, 0f, 10f), after);
        Assert.AreEqual(1f, t1);
    }

    [Test]
    public void Transform_IsApplied()
    {
        go.transform.position = new Vector3(5f, 1f, 0f);
        go.transform.localScale = new Vector3(1f, 1f, 2f);

        Assert.AreEqual(20f, rail.Length, 0.0001f);
        TestUtil.AreClose(new Vector3(5f, 1f, 10f), rail.PointAt(0.5f));
    }

    // ---- Перила со слоя Grind: линии строятся по BoxCollider ----

    private static GrindRail[] RailsFromBox(Vector3 scale)
    {
        GameObject box = new GameObject("GrindBox");
        box.transform.localScale = scale;
        BoxCollider c = box.AddComponent<BoxCollider>();   // центр 0, размер 1
        GrindRail.AddFromCollider(c);
        return box.GetComponents<GrindRail>();
    }

    [Test]
    public void AddFromCollider_ThinBar_OneLineAlongTopCenter()
    {
        GrindRail[] rails = RailsFromBox(new Vector3(0.1f, 0.1f, 6f));
        try
        {
            Assert.AreEqual(1, rails.Length);
            TestUtil.AreClose(new Vector3(0f, 0.05f, -3f), rails[0].Start);
            TestUtil.AreClose(new Vector3(0f, 0.05f, 3f), rails[0].End);
        }
        finally { Object.DestroyImmediate(rails[0].gameObject); }
    }

    [Test]
    public void AddFromCollider_WideBox_TwoLinesOnLongTopEdges()
    {
        GrindRail[] rails = RailsFromBox(new Vector3(8f, 1f, 2f));   // длинная сторона -- X
        try
        {
            Assert.AreEqual(2, rails.Length);
            foreach (GrindRail r in rails)
            {
                Assert.AreEqual(8f, r.Length, 0.0001f);
                Assert.AreEqual(0.5f, r.Start.y, 0.0001f, "линия по верху ящика");
                Assert.AreEqual(1f, Mathf.Abs(r.Start.z), 0.0001f, "линия по краю, а не по центру");
                TestUtil.AreClose(Vector3.right, r.Direction);
            }
            Assert.AreNotEqual(rails[0].Start.z, rails[1].Start.z, "края с двух сторон");
        }
        finally { Object.DestroyImmediate(rails[0].gameObject); }
    }
}
