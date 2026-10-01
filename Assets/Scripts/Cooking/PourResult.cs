using System;
using UnityEngine;

// Оценка "насыпного/наливного" ингредиента (мука, молоко, сахар...)
public enum PourResult
{
    Perfect, // попали в зелёную зону
    Good,    // жёлтая зона — немного недосыпали/пересыпали
    Bad      // красная зона — сильный недобор или перебор
}

// Что сыпем/льём (в миску или в вафельницу)
public enum PourIngredient
{
    Flour,
    Milk,
    Batter, // тесто половником из миски в вафельницу
    Sugar   // сахар: держим палец над миской, как муку (значение в конце — не сдвигает сохранённые в префабах)
}

// Что добавляем порциями (одно нажатие — одна порция)
public enum PortionIngredient
{
    Sugar, // устарело: сахар теперь сыпется удержанием (PourIngredient.Sugar); порция = SugarPortionGrams
    Salt
}

// Сколько нужно насыпать и насколько можно ошибиться.
// Потом сюда будет приходить значение из рецепта заказа.
[Serializable]
public struct PourTarget
{
    [Tooltip("Идеальное количество (граммы/мл)")]
    public float amount;
    [Tooltip("Зелёная зона: ± от идеального (граммы/мл)")]
    public float greenTolerance;
    [Tooltip("Жёлтая зона: ± от идеального (граммы/мл), должна быть больше зелёной")]
    public float yellowTolerance;

    public PourTarget(float amount, float greenTolerance, float yellowTolerance)
    {
        this.amount = amount;
        this.greenTolerance = greenTolerance;
        this.yellowTolerance = yellowTolerance;
    }

    public float GreenMin => amount - greenTolerance;
    public float GreenMax => amount + greenTolerance;
    public float YellowMin => amount - yellowTolerance;
    public float YellowMax => amount + yellowTolerance;

    public PourResult Evaluate(float poured)
    {
        float diff = Mathf.Abs(poured - amount);
        if (diff <= greenTolerance) return PourResult.Perfect;
        if (diff <= yellowTolerance) return PourResult.Good;
        return PourResult.Bad;
    }
}
