using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "DataGame", menuName = "Game/Game Data")]
public class DataGame : ScriptableObject
{

    [System.Serializable]
    public class PlayerData
    {
        public string playerName;
        public int maxHealth = 40;
        public int maxStagger = 16;
        public int baseSpeedMin = 1;
        public int baseSpeedMax = 4;
        public int maxCardOnHand = 16;
        public int dekaPlayer = 10;
        public int baseStartLight = 3;
        public List<int> cardPlayerIds = new List<int>();
        public int diceRollPlayer;
    }



    [System.Serializable]
    public class EnemyData
    {
        public string enemyName;
        public int maxHealth = 100;
        public int maxStagger = 16;
        public int baseSpeedMin = 1;
        public int baseSpeedMaxn = 3;
        public int dekaPlayer = 10;
        public int baseStartLight = 3;
        public List<int> cardEnemyIds = new List<int>();
        public Color color = Color.red;
        public int diceRollEnemy;
    }

    [System.Serializable]
    public class EncounterEnemyEntry
    {
        [Min(0)] public int enemyDataIndex;
        [Min(1)] public int count = 1;
    }

    [System.Serializable]
    public class RoomEncounterConfig
    {
        public MapRoomType roomType = MapRoomType.Mob;
        public List<EncounterEnemyEntry> enemies = new List<EncounterEnemyEntry>();
    }


    [System.Serializable]
    public class CardData
    {
        public int cardId;
        public string cardName;
        public int lightCost;
        public Sprite cardSprite;
        public AttackData[] attacks;
        public PassiveAction[] passiveActions;
        public StatusEffect[] endOfTurnEffects;
        public PassiveAction[] onDiscardEffects;

        public string GetShortDescription()
        {
            if (passiveActions != null)
            {
                for (int i = 0; i < passiveActions.Length; i++)
                {
                    PassiveAction action = passiveActions[i];
                    if (action != null)
                    {
                        return action.GetDescription();
                    }
                }
            }

            if (attacks != null)
            {
                for (int i = 0; i < attacks.Length; i++)
                {
                    AttackData attack = attacks[i];
                    if (attack != null)
                    {
                        return $"{attack.attackName} {attack.RollMin}-{attack.RollMax}";
                    }
                }
            }

            return "No effect";
        }
    }

    [System.Serializable]
    public class AttackData
    {
        public string attackName = "Basic Strike";
        public int RollMin;
        public int RollMax;
        public int staggerDamage;
        public float attackDuration = 1f;
        public StatusEffect[] onHitEffects;
      //  public AudioClip attackSound;
        public Type type; 
        public enum Type
            {Damage,
            Block,
            Escape,      
            }
    }

    [System.Serializable]
    public class PassiveAction
    {
        public PassiveEffectType effectType;
        public int value = 1;
        public StatusEffect statusToApply;
        public bool onPlay = true;
        public bool onDiscard = false;
        public bool onDraw = false;
        [TextArea(2, 3)] public string customDescription;

        public string GetDescription()
        {
            if (!string.IsNullOrEmpty(customDescription))
            {
                return customDescription;
            }

            switch (effectType)
            {
                case PassiveEffectType.DrawCard:
                    return $"Draw {value}";
                case PassiveEffectType.GainLight:
                    return $"Gain {value} Light";
                case PassiveEffectType.HealPlayer:
                    return $"Heal {value}";
                case PassiveEffectType.ReduceCardCost:
                    return $"Next card costs {value} less";
                case PassiveEffectType.CopyCard:
                    return "Copy a card";
                case PassiveEffectType.Discard:
                    return $"Discard {value}";
                case PassiveEffectType.Burn:
                    return $"Burn {value}";
                case PassiveEffectType.Paralysis:
                    return $"Paralysis {value}";
                case PassiveEffectType.Bleed:
                    return $"Bleed {value}";
                case PassiveEffectType.Fairy:
                    return $"Fairy {value}";
                case PassiveEffectType.Protection:
                    return $"Protection {value}";
                case PassiveEffectType.StaggerProtection:
                    return $"Stagger Protection {value}";
                case PassiveEffectType.Fragile:
                    return $"Fragile {value}";
                case PassiveEffectType.Strength:
                    return $"Strength {value}";
                case PassiveEffectType.Feeble:
                    return $"Feeble {value}";
                case PassiveEffectType.Endurance:
                    return $"Endurance {value}";
                case PassiveEffectType.Disarm:
                    return $"Disarm {value}";
                case PassiveEffectType.Haste:
                    return $"Haste {value}";
                case PassiveEffectType.Bind:
                    return $"Bind {value}";
                case PassiveEffectType.NullifyPower:
                    return "Nullify Power";
                case PassiveEffectType.Immobilized:
                    return "Immobilized";
                case PassiveEffectType.Charge:
                    return $"Charge {value}";
                case PassiveEffectType.Smoke:
                    return $"Smoke {value}";
                case PassiveEffectType.Persistence:
                    return $"Persistence {value}";
                case PassiveEffectType.Erosion:
                    return $"Erosion {value}";
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
    [Header("Enemy Data")]
    public List<PlayerData> playerData = new List<PlayerData>();

    [Header("Enemy Data")]
    [FormerlySerializedAs("allenemyData")]
    public List<EnemyData> enemyData = new List<EnemyData>();
    public int enemyCount = 1;

    [Header("Room Encounters")]
    public List<RoomEncounterConfig> roomEncounters = new List<RoomEncounterConfig>();

    [Header("All Cards")]
    public List<CardData> allCards = new List<CardData>();

    [Header("Starter Player Pool")]
    [SerializeField] private List<int> startPlayerPoolCardIds = new List<int>();

    private Dictionary<int, CardData> cardLookup;

    public IReadOnlyList<int> GetStartPlayerPoolCardIds()
    {
        if (startPlayerPoolCardIds != null && startPlayerPoolCardIds.Count > 0)
        {
            return startPlayerPoolCardIds;
        }

        List<int> fallbackIds = new List<int>();
        for (int i = 0; i < allCards.Count && fallbackIds.Count < 10; i++)
        {
            CardData card = allCards[i];
            if (card != null)
            {
                fallbackIds.Add(card.cardId);
            }
        }

        return fallbackIds;
    }

    public PlayerData GetPlayerData(int index = 0)
    {
        if (playerData == null || index < 0 || index >= playerData.Count)
        {
            return null;
        }

        return playerData[index];
    }

    public List<int> GetPlayerCardIds(int index = 0)
    {
        PlayerData selectedPlayerData = GetPlayerData(index);
        if (selectedPlayerData == null || selectedPlayerData.cardPlayerIds == null)
        {
            return new List<int>();
        }

        return new List<int>(selectedPlayerData.cardPlayerIds);
    }

    public EnemyData GetEnemyData(int index = 0)
    {
        if (enemyData == null || index < 0 || index >= enemyData.Count)
        {
            return null;
        }

        return enemyData[index];
    }

    public List<int> GetEnemyCardIds(int index = 0)
    {
        EnemyData selectedEnemyData = GetEnemyData(index);
        if (selectedEnemyData == null || selectedEnemyData.cardEnemyIds == null)
        {
            return new List<int>();
        }

        return new List<int>(selectedEnemyData.cardEnemyIds);
    }

    public int GetEnemyBaseSpeedMax(int index = 0)
    {
        EnemyData enemyData = GetEnemyData(index);
        if (enemyData == null)
        {
            return 0;
        }

        return enemyData.baseSpeedMaxn;
    }

    public int GetEnemyCount()
    {
        if (enemyData == null || enemyData.Count == 0)
        {
            return 0;
        }

        return Mathf.Max(0, enemyCount);
    }

    public List<EncounterEnemyEntry> GetEncounterPlan(MapRoomType roomType)
    {
        if (roomEncounters != null)
        {
            for (int i = 0; i < roomEncounters.Count; i++)
            {
                RoomEncounterConfig config = roomEncounters[i];
                if (config == null || config.roomType != roomType)
                {
                    continue;
                }

                if (config.enemies == null || config.enemies.Count == 0)
                {
                    break;
                }

                List<EncounterEnemyEntry> result = new List<EncounterEnemyEntry>(config.enemies.Count);
                for (int j = 0; j < config.enemies.Count; j++)
                {
                    EncounterEnemyEntry entry = config.enemies[j];
                    if (entry == null)
                    {
                        continue;
                    }

                    result.Add(new EncounterEnemyEntry
                    {
                        enemyDataIndex = Mathf.Max(0, entry.enemyDataIndex),
                        count = Mathf.Max(1, entry.count),
                    });
                }

                if (result.Count > 0)
                {
                    return result;
                }
            }
        }

        return BuildFallbackEncounterPlan(roomType);
    }

    public int GetRandomAllCardId()
    {
        List<int> validCardIds = new List<int>();

        for (int i = 0; i < allCards.Count; i++)
        {
            CardData card = allCards[i];
            if (card == null || card.cardId <= 0)
            {
                continue;
            }

            validCardIds.Add(card.cardId);
        }

        if (validCardIds.Count == 0)
        {
            return -1;
        }

        int randomIndex = Random.Range(0, validCardIds.Count);
        return validCardIds[randomIndex];
    }

    public bool TryGetCardById(int cardId, out CardData cardData)
    {
        BuildLookupIfNeeded();
        return cardLookup.TryGetValue(cardId, out cardData);
    }

    public CardData GetCardById(int cardId)
    {
        TryGetCardById(cardId, out CardData cardData);
        return cardData;
    }

    private void OnValidate()
    {
        EnsureCardIds();
        BuildLookupIfNeeded(true);
        EnsureRoomEncounterDefaults();

        if ((startPlayerPoolCardIds == null || startPlayerPoolCardIds.Count == 0) && allCards.Count > 0)
        {
            startPlayerPoolCardIds = new List<int>();
            for (int i = 0; i < allCards.Count && startPlayerPoolCardIds.Count < 10; i++)
            {
                CardData card = allCards[i];
                if (card != null)
                {
                    startPlayerPoolCardIds.Add(card.cardId);
                }
            }
        }
    }

    private void EnsureCardIds()
    {
        HashSet<int> usedIds = new HashSet<int>();
        int nextId = 1;

        for (int i = 0; i < allCards.Count; i++)
        {
            CardData card = allCards[i];
            if (card == null)
            {
                continue;
            }

            if (card.cardId <= 0 || usedIds.Contains(card.cardId))
            {
                while (usedIds.Contains(nextId))
                {
                    nextId++;
                }

                card.cardId = nextId;
            }

            usedIds.Add(card.cardId);

            if (card.cardId >= nextId)
            {
                nextId = card.cardId + 1;
            }
        }
    }

    private void BuildLookupIfNeeded(bool forceRebuild = false)
    {
        if (cardLookup != null && !forceRebuild)
        {
            return;
        }

        cardLookup = new Dictionary<int, CardData>();
        for (int i = 0; i < allCards.Count; i++)
        {
            CardData card = allCards[i];
            if (card == null || card.cardId <= 0)
            {
                continue;
            }

            if (!cardLookup.ContainsKey(card.cardId))
            {
                cardLookup.Add(card.cardId, card);
            }
        }
    }

    private void EnsureRoomEncounterDefaults()
    {
        if (roomEncounters == null)
        {
            roomEncounters = new List<RoomEncounterConfig>();
        }

        EnsureEncounterConfig(MapRoomType.Mob, 0, Mathf.Max(1, Mathf.Min(3, enemyCount)));
        EnsureEncounterConfig(MapRoomType.EliteMob, Mathf.Min(1, Mathf.Max(0, enemyData.Count - 1)), 2);
        EnsureEncounterConfig(MapRoomType.Boss, Mathf.Min(2, Mathf.Max(0, enemyData.Count - 1)), 1);
    }

    private void EnsureEncounterConfig(MapRoomType roomType, int defaultEnemyIndex, int defaultCount)
    {
        for (int i = 0; i < roomEncounters.Count; i++)
        {
            RoomEncounterConfig existing = roomEncounters[i];
            if (existing != null && existing.roomType == roomType)
            {
                if (existing.enemies == null)
                {
                    existing.enemies = new List<EncounterEnemyEntry>();
                }

                if (existing.enemies.Count == 0)
                {
                    existing.enemies.Add(new EncounterEnemyEntry
                    {
                        enemyDataIndex = Mathf.Max(0, defaultEnemyIndex),
                        count = Mathf.Max(1, defaultCount),
                    });
                }

                return;
            }
        }

        roomEncounters.Add(new RoomEncounterConfig
        {
            roomType = roomType,
            enemies = new List<EncounterEnemyEntry>
            {
                new EncounterEnemyEntry
                {
                    enemyDataIndex = Mathf.Max(0, defaultEnemyIndex),
                    count = Mathf.Max(1, defaultCount),
                }
            }
        });
    }

    private List<EncounterEnemyEntry> BuildFallbackEncounterPlan(MapRoomType roomType)
    {
        int fallbackIndex = 0;
        int fallbackCount = Mathf.Max(1, enemyCount);

        switch (roomType)
        {
            case MapRoomType.EliteMob:
                fallbackIndex = Mathf.Min(1, Mathf.Max(0, enemyData.Count - 1));
                fallbackCount = 2;
                break;
            case MapRoomType.Boss:
                fallbackIndex = Mathf.Min(2, Mathf.Max(0, enemyData.Count - 1));
                fallbackCount = 1;
                break;
        }

        return new List<EncounterEnemyEntry>
        {
            new EncounterEnemyEntry
            {
                enemyDataIndex = fallbackIndex,
                count = fallbackCount,
            }
        };
    }
}
