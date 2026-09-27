using System;
using UnityEngine;

// Пять зон горизонтальной шкалы в долях 0..1:
//   красная | жёлтая | ЗЕЛЁНАЯ | жёлтая | красная
//   0 … yellowMin … greenMin … greenMax … yellowMax … 1
// Перемешивание: слева вяло, справа слишком резко. Выпечка: слева сыро, справа сгорело.
[Serializable]
public struct GaugeZones
{
    [Range(0f, 1f)] public float yellowMin;
    [Range(0f, 1f)] public float greenMin;
    [Range(0f, 1f)] public float greenMax;
    [Range(0f, 1f)] public float yellowMax;

    public GaugeZones(float yellowMin, float greenMin, float greenMax, float yellowMax)
    {
        this.yellowMin = yellowMin;
        this.greenMin = greenMin;
        this.greenMax = greenMax;
        this.yellowMax = yellowMax;
    }

    public bool IsGreen(float v) => v >= greenMin && v <= greenMax;
    public bool IsYellow(float v) => !IsGreen(v) && v >= yellowMin && v <= yellowMax;
    public bool IsOvermix(float v) => v > yellowMax;

    // -2 красная слева, -1 жёлтая слева, 0 зелёная, 1 жёлтая справа, 2 красная справа
    public int Classify(float v)
    {
        if (v < yellowMin) return -2;
        if (v < greenMin) return -1;
        if (v <= greenMax) return 0;
        if (v <= yellowMax) return 1;
        return 2;
    }
}
