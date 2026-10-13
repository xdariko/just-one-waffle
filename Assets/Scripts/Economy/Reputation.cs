using System;
using UnityEngine;

// Уровень репутации кафе (EconomySettings.reputationLevels). Номер уровня — место в списке, с 1.
[Serializable]
public class ReputationLevel
{
    public string title = "Уровень";
    [Tooltip("Сколько очков репутации нужно набрать всего. У первого уровня — 0")]
    [Min(0)] public int points;
    [Tooltip("Премия при достижении уровня: попадает в заработок смены, в которую уровень взят")]
    [Min(0)] public int reward;
    [Tooltip("Сколько жетонов спокойного дня выдаётся при достижении уровня")]
    [Min(0)] public int calmTokens;
}

// Репутация на данный момент: уровень, его название и сколько осталось до следующего.
// Очки репутации — это все очки за обслуживание за всё время (SaveData.totalPoints): не тратятся и не убывают.
public readonly struct ReputationState
{
    public readonly int Level;
    public readonly int MaxLevel;
    public readonly string Title;
    public readonly long Points;
    // Пороги текущего и следующего уровня (у максимального NextAt = LevelAt)
    public readonly long LevelAt;
    public readonly long NextAt;
    public readonly string NextTitle;

    public ReputationState(int level, int maxLevel, string title, long points, long levelAt, long nextAt, string nextTitle)
    {
        Level = level;
        MaxLevel = maxLevel;
        Title = title;
        Points = points;
        LevelAt = levelAt;
        NextAt = nextAt;
        NextTitle = nextTitle;
    }

    public bool IsMax => Level >= MaxLevel;
    public long ToNext => IsMax ? 0 : Math.Max(0, NextAt - Points);

    public override string ToString() =>
        IsMax ? $"ур. {Level} «{Title}», {Points} очк. (максимум)" : $"ур. {Level} «{Title}», {Points} / {NextAt} до «{NextTitle}»";
}
