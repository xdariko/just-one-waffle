using System;
using System.Collections.Generic;
using UnityEngine;

// Как выглядит вафля в зависимости от пропечки (ассет: Create → Cooking → Waffle Look).
// Используют вафельница (вафля сбоку: на вафельнице и в пальце) и станция украшения (вафля сверху).
// Картинка этапа задана — берётся она, иначе общая картинка, иначе заглушка. Цвет этапа тонирует картинку:
// для своих картинок на каждый этап поставь цвет белым.
[CreateAssetMenu(menuName = "Cooking/Waffle Look", fileName = "WaffleLook")]
public class WaffleLook : ScriptableObject
{
    [Serializable]
    public class Stage
    {
        public BakeResult result;
        public Color tint = Color.white;
        [Tooltip("Своя картинка сбоку для этого этапа (пусто — общая)")]
        public Sprite side;
        [Tooltip("Своя картинка сверху для этого этапа (пусто — общая)")]
        public Sprite top;
    }

    [Header("Общие картинки (пусто — заглушки)")]
    [Tooltip("Вафля сбоку: лежит на вафельнице, её несут на украшение")]
    public Sprite side;
    [Tooltip("Вафля сверху: на доске украшения, по ней мажут соус")]
    public Sprite top;
    [Tooltip("Размер заглушки сбоку (без картинки), мировые единицы")]
    public Vector2 placeholderSideSize = new(1.4f, 0.3f);

    [Header("По пропечке")]
    public List<Stage> stages = new()
    {
        new Stage { result = BakeResult.Raw, tint = new Color(0.98f, 0.93f, 0.72f) },
        new Stage { result = BakeResult.Undercooked, tint = new Color(0.96f, 0.83f, 0.52f) },
        new Stage { result = BakeResult.Perfect, tint = new Color(0.86f, 0.6f, 0.26f) },
        new Stage { result = BakeResult.Overcooked, tint = new Color(0.55f, 0.33f, 0.15f) },
        new Stage { result = BakeResult.Burnt, tint = new Color(0.18f, 0.11f, 0.07f) }
    };

    private static WaffleLook fallback;

    // Ассет не задан в инспекторе — вид по умолчанию (заглушки и цвета выше)
    public static WaffleLook OrDefault(WaffleLook look)
    {
        if (look != null) return look;
        if (fallback == null)
        {
            fallback = CreateInstance<WaffleLook>();
            fallback.hideFlags = HideFlags.HideAndDontSave;
        }
        return fallback;
    }

    public Stage StageOf(BakeResult? result) =>
        result == null ? null : stages.Find(s => s != null && s.result == result.Value);

    public Color Tint(BakeResult? result) => StageOf(result)?.tint ?? Color.white;
    public Sprite Side(BakeResult? result) => StageOf(result)?.side != null ? StageOf(result).side : side;
    public Sprite Top(BakeResult? result) => StageOf(result)?.top != null ? StageOf(result).top : top;
}
