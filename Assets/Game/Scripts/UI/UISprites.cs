using System.Collections.Generic;
using UnityEngine;

// Процедурные спрайты для UI "кожаной сумки": кожа со строчкой (9-slice),
// клапан, ручка, пряжка, рамка подсветки. Генерируются один раз и кэшируются.
public static class UISprites
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // Скруглённый прямоугольник из кожи со строчкой по краю. Режется 9-slice.
    public static Sprite Leather(Color baseColor, Color stitchColor, int radius = 14, int stitchInset = 7, int size = 96)
    {
        string key = $"leather{baseColor}{stitchColor}{radius}{stitchInset}{size}";
        if (cache.TryGetValue(key, out Sprite s)) return s;

        Texture2D t = NewTex(size, size);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundRectDist(x + 0.5f - half, y + 0.5f - half, half, half, radius);
                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) { t.SetPixel(x, y, Color.clear); continue; }

                Color c = baseColor * Grain(x, y);
                // Тёмный "кант" по краю и светлый блик сверху -- объём
                c *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(-d / 5f));
                if (y > size - radius && d > -4f) c *= 1.12f;

                // Строчка: пунктир на расстоянии stitchInset от края
                if (Mathf.Abs(d + stitchInset) < 1f && ((x + y) / 4) % 2 == 0)
                    c = stitchColor;

                c.a = alpha;
                t.SetPixel(x, y, c);
            }
        t.Apply();

        int border = radius + stitchInset + 3;
        s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        cache[key] = s;
        return s;
    }

    // Клапан сумки: сверху прямой, снизу закруглён "языком"
    public static Sprite Flap(Color baseColor, Color stitchColor, int w = 256, int h = 96)
    {
        string key = $"flap{baseColor}{stitchColor}{w}{h}";
        if (cache.TryGetValue(key, out Sprite s)) return s;

        Texture2D t = NewTex(w, h);
        float hw = w * 0.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Нижняя кромка -- пологая дуга: по краям выше, в центре ниже
                float nx = (x + 0.5f - hw) / hw;
                float edge = h * 0.35f * nx * nx;          // высота нижней кромки над низом текстуры
                float dBottom = edge - (y + 0.5f);         // >0 -- ниже кромки (снаружи)
                float dSide = Mathf.Abs(x + 0.5f - hw) - (hw - 1f);
                float d = Mathf.Max(dBottom, dSide);
                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) { t.SetPixel(x, y, Color.clear); continue; }

                Color c = baseColor * Grain(x, y) * Mathf.Lerp(0.65f, 1f, Mathf.Clamp01(-d / 6f));
                if (Mathf.Abs(d + 7f) < 1f && ((x + y) / 4) % 2 == 0) c = stitchColor;
                c.a = alpha;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    // Ручка сумки: толстая полудуга
    public static Sprite Handle(Color color, int w = 256, int h = 128, float thickness = 18f)
    {
        string key = $"handle{color}{w}{h}{thickness}";
        if (cache.TryGetValue(key, out Sprite s)) return s;

        Texture2D t = NewTex(w, h);
        Vector2 c0 = new Vector2(w * 0.5f, 0f);
        float rx = w * 0.5f - thickness, ry = h - thickness;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) - c0;
                float e = Mathf.Sqrt((p.x * p.x) / (rx * rx) + (p.y * p.y) / (ry * ry)); // 1 -- на средней линии дуги
                float d = Mathf.Abs(e - 1f) * Mathf.Min(rx, ry) - thickness * 0.5f;
                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) { t.SetPixel(x, y, Color.clear); continue; }
                Color c = color * Grain(x, y) * Mathf.Lerp(0.7f, 1.05f, Mathf.Clamp01(-d / (thickness * 0.5f)));
                c.a = alpha;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 100f);
        cache[key] = s;
        return s;
    }

    // Металлическая пряжка: рамка с прорезью
    public static Sprite Buckle(Color metal, int w = 48, int h = 40)
    {
        string key = $"buckle{metal}{w}{h}";
        if (cache.TryGetValue(key, out Sprite s)) return s;

        Texture2D t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float outer = RoundRectDist(x + 0.5f - w * 0.5f, y + 0.5f - h * 0.5f, w * 0.5f, h * 0.5f, 6f);
                float inner = RoundRectDist(x + 0.5f - w * 0.5f, y + 0.5f - h * 0.5f, w * 0.5f - 8f, h * 0.5f - 8f, 3f);
                float d = Mathf.Max(outer, -inner);
                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) { t.SetPixel(x, y, Color.clear); continue; }
                float shine = Mathf.Lerp(0.7f, 1.25f, (float)y / h);
                Color c = metal * shine;
                c.a = alpha;
                t.SetPixel(x, y, c);
            }
        t.Apply();
        s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        cache[key] = s;
        return s;
    }

    // Рамка подсветки выбранного слота (9-slice)
    public static Sprite Frame(int thickness = 4, int radius = 12, int size = 64)
    {
        string key = $"frame{thickness}{radius}{size}";
        if (cache.TryGetValue(key, out Sprite s)) return s;

        Texture2D t = NewTex(size, size);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = RoundRectDist(x + 0.5f - half, y + 0.5f - half, half, half, radius);
                float a = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(thickness + 0.5f + d);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply();
        int border = radius + thickness;
        s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
            new Vector4(border, border, border, border));
        cache[key] = s;
        return s;
    }

    private static Texture2D NewTex(int w, int h)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        return t;
    }

    // Знаковое расстояние до скруглённого прямоугольника с полуразмерами (hx, hy)
    private static float RoundRectDist(float px, float py, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(px) - (hx - r), qy = Mathf.Abs(py) - (hy - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    // Зернистость кожи
    private static float Grain(int x, int y)
    {
        float n = Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.6f + Mathf.PerlinNoise(x * 1.7f + 50f, y * 1.7f) * 0.4f;
        return 0.86f + n * 0.22f;
    }
}
