using UnityEngine;
using UnityEngine.UI;

// Перспективный наклон графики UI (для карточек меню, см. MenuButtonHover).
// Канва рисуется без перспективы, поэтому просто повернуть RectTransform -- карточка только сплющится.
// Здесь вершины сдвигаются сами: плоскость карточки наклонена (глубина z = tilt.x * x + tilt.y * y,
// x и y -- от центра карточки space в пикселях), и точка уменьшается к центру, как в камере с фокусом focal.
// Край со стороны tilt уходит вглубь и становится меньше. Все графики одной карточки считают в одном space,
// поэтому рамка и надписи наклоняются вместе.
[RequireComponent(typeof(Graphic))]
public class MenuCardTilt : BaseMeshEffect
{
    public RectTransform space;
    public float focal = 700f;

    private Vector2 tilt;

    public void Set(Vector2 value)
    {
        if ((value - tilt).sqrMagnitude < 1e-8f) return;
        tilt = value;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || tilt == Vector2.zero) return;
        Transform self = transform;
        Transform s = space != null ? space : self;
        bool same = s == self;
        UIVertex v = default;
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            Vector3 p = same ? v.position : s.InverseTransformPoint(self.TransformPoint(v.position));
            float z = tilt.x * p.x + tilt.y * p.y;
            float k = focal / Mathf.Max(1f, focal + z);
            Vector3 q = new Vector3(p.x * k, p.y * k, p.z);
            v.position = same ? q : self.InverseTransformPoint(s.TransformPoint(q));
            vh.SetUIVertex(v, i);
        }
    }
}
