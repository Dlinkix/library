using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "DataGame", menuName = "Game/Game Data")]
public class DataGame : ScriptableObject
{
    #region enemy
    [System.Serializable]
    public class EnemyData
    {
        public string enemyName;
        public int maxHealth = 100;
        public float speed = 5f;
        public int damage = 10;
        public Color color = Color.red;
    }
    #endregion

    #region card

    [System.Serializable] 
    public class CardData
    {
        public int lightCost;

        [Header("Атаки карты")]
        public AttackData[] attacks;

        public string cardName;  // переименовал с Name на cardName (чтобы не путать)

        [Header("Визуал")]
        public Sprite cardSprite;  // Image нельзя хранить в SO, используй Sprite

        [Header("Пассивные эффекты при розыгрыше")]
        public PassiveAction[] passiveActions;

        [Header("Эффекты в конце хода")]
        public StatusEffect[] endOfTurnEffects;

        [Header("Эффекты при сбросе")]
        public PassiveAction[] onDiscardEffects;
    }

    [System.Serializable]
    public class AttackData
    {
        public string attackName = "Обычная атака";
        public int RollMin;
        public int RollMax;
        public int staggerDamage;
        public float attackDuration = 1f;

        [Header("Эффекты от атаки")]
        public StatusEffect[] onHitEffects;

        public AudioClip attackSound;
    }

    [System.Serializable]
    public class PassiveAction
    {
        public PassiveEffectType effectType;

        [Header("Параметры для разных эффектов")]
        public int value = 1;
        public StatusEffect statusToApply;
        public bool onPlay = true;
        public bool onDiscard = false;
        public bool onDraw = false;

        [TextArea(2, 3)]
        public string customDescription;

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(customDescription))
                return customDescription;

            switch (effectType)
            {
                case PassiveEffectType.DrawCard:
                    return $"Вытянуть {value} карт(у)";
                case PassiveEffectType.GainLight:
                    return $"Получить {value} света";
                case PassiveEffectType.HealPlayer:
                    return $"Восстановить {value} HP";
                case PassiveEffectType.ReduceCardCost:
                    return $"Следующая карта стоит на {value} меньше";
                case PassiveEffectType.CopyCard:
                    return "Скопировать карту";
                case PassiveEffectType.Discard:
                    return $"Сбросить {value} карт(у)";
                case PassiveEffectType.Burn:
                    return $"Наложить Горение ({value} урона в конце сцены)";
                case PassiveEffectType.Paralysis:
                    return $"Наложить Паралич (до {value} кубов теряют 3 к макс. значения)";
                case PassiveEffectType.Bleed:
                    return $"Наложить Кровотечение ({value} урона при атаке)";
                case PassiveEffectType.Fairy:
                    return $"Наложить Фею ({value} урона при броске куба)";
                case PassiveEffectType.Protection:
                    return $"Наложить Защиту (-{value} к получаемому урону)";
                case PassiveEffectType.StaggerProtection:
                    return $"Наложить Защиту стаггера (-{value} к стаггер урону)";
                case PassiveEffectType.Fragile:
                    return $"Наложить Хрупкость (+{value} к получаемому урону)";
                case PassiveEffectType.Strength:
                    return $"Наложить Силу (+{value} к атакующим кубам)";
                case PassiveEffectType.Feeble:
                    return $"Наложить Слабость (-{value} к атакующим кубам)";
                case PassiveEffectType.Endurance:
                    return $"Наложить Выносливость (+{value} к защитным кубам)";
                case PassiveEffectType.Disarm:
                    return $"Наложить Обезоруживание (-{value} к защитным кубам)";
                case PassiveEffectType.Haste:
                    return $"Наложить Ускорение (+{value} к скорости)";
                case PassiveEffectType.Bind:
                    return $"Наложить Связывание (-{value} к скорости)";
                case PassiveEffectType.NullifyPower:
                    return "Наложить Аннулирование силы (игнорирует эффекты силы)";
                case PassiveEffectType.Immobilized:
                    return "Наложить Обездвиживание (не может действовать)";
                case PassiveEffectType.Charge:
                    return $"Получить {value} Заряда(ов)";
                case PassiveEffectType.Smoke:
                    return $"Наложить Дым ({value}%, +5% урона за стек)";
                case PassiveEffectType.Persistence:
                    return $"Наложить Стойкость ({value}% шанс воскреснуть)";
                case PassiveEffectType.Erosion:
                    return $"Наложить Эрозию ({value} урона в конце сцены и при попадании)";
                default:
                    return effectType.ToString();
            }
        }
    }

    public enum PassiveEffectType
    {
        ApplyStatusEffect,
        ReduceCardCost,
        CopyCard,
        Discard,
        DrawCard,
        GainLight,
        HealPlayer,
        Burn,
        Paralysis,
        Bleed,
        Fairy,
        Protection,
        StaggerProtection,
        Fragile,
        Strength,
        Feeble,
        Endurance,
        Disarm,
        Haste,
        Bind,
        NullifyPower,
        Immobilized,
        Charge,
        Smoke,
        Persistence,
        Erosion,
    }

    public enum StatusEffectType
    {
        ReduceCardCost,
        CopyCard,
        Discard,
        DrawCard,
        GainLight,
        HealPlayer,
        Burn,
        Paralysis,
        Bleed,
        Fairy,
        Protection,
        StaggerProtection,
        Fragile,
        Strength,
        Feeble,
        Endurance,
        Disarm,
        Haste,
        Bind,
        NullifyPower,
        Immobilized,
        Charge,
        Smoke,
        Persistence,
        Erosion,
    }

    [System.Serializable]
    public class StatusEffect
    {
        public StatusEffectType type;
        public int intensity;
        public int duration;
        public bool canStack;
        public int maxStack;
    }

    #endregion

    [Header("Enemy Data")]
    public List<EnemyData> allenemyData = new List<EnemyData>();

    [Header("All Cards")]
    public List<CardData> allCards = new List<CardData>();
}