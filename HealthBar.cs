using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Reflection;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Enums;
using RectangleF = ExileCore2.Shared.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace HealthBars;

public class HealthBar
{
    private static long _idCounter;
    private readonly Stopwatch _dpsStopwatch = Stopwatch.StartNew();
    private bool _isHostile;
    private readonly CachedValue<float> _distanceCache;

    public HealthBar(Entity entity, HealthBarsSettings settings)
    {
        Entity = entity;
        AllSettings = settings;
        _distanceCache = new TimeCache<float>(() => entity.DistancePlayer, 200);
        Update();
    }

    public void CheckUpdate()
    {
        var entityIsHostile = Entity.IsHostile;

        if (_isHostile != entityIsHostile)
        {
            _isHostile = entityIsHostile;
            Update();
        }

        if (Settings.ShowDps)
        {
            DpsRefresh();
        }
    }

    public bool Skip { get; set; } = false;
    public Vector2 LastPosition { get; set; }
    private HealthBarsSettings AllSettings { get; }
    public long StableId { get; } = Interlocked.Increment(ref _idCounter);

    public UnitSettings Settings => Type switch
    {
        CreatureType.Player when Entity.Equals(Entity.Player) => AllSettings.Self,
        CreatureType.Player => AllSettings.Players,
        CreatureType.Minion => AllSettings.Minions,
        CreatureType.Normal => AllSettings.NormalEnemy,
        CreatureType.Magic => AllSettings.MagicEnemy,
        CreatureType.Rare => AllSettings.RareEnemy,
        CreatureType.Unique => AllSettings.UniqueEnemy,
        _ => AllSettings.Minions,
    };

    public RectangleF DisplayArea { get; set; }
    public float Distance => _distanceCache.Value;
    public Entity Entity { get; }
    public CreatureType Type { get; private set; }
    public Life Life => Entity.GetComponent<Life>();
    public float HpPercent => Life.HPPercentage;
    public float EsPercent => Life.ESPercentage;
    public float ManaPercent => Life.MPPercentage;
    public float EhpPercent => CurrentEhp / (float)MaxEhp;

    // Ward is not exposed by every pinned ExileCore2/GameOffsets2 build. Resolve it
    // reflectively so the PoE2 plugin remains loadable and reports zero when the
    // current runtime has no Ward component/property.
    private static readonly PropertyInfo WardProperty = typeof(Life).GetProperty("Ward");
    private static readonly PropertyInfo WardCurrentProperty = WardProperty?.PropertyType.GetProperty("Current");
    private static readonly PropertyInfo WardMaxProperty = WardProperty?.PropertyType.GetProperty("Max");

    private static int ReadWard(Life life, PropertyInfo property)
    {
        try
        {
            var ward = WardProperty?.GetValue(life);
            return ward == null ? 0 : Convert.ToInt32(property?.GetValue(ward) ?? 0);
        }
        catch { return 0; }
    }

    public int CurrentWard => ReadWard(Life, WardCurrentProperty);
    public int MaxWard => ReadWard(Life, WardMaxProperty);

    public int CurrentEhp => (Settings.ShowLife ? Life.CurHP : 0) + (Settings.ShowES ? Life.CurES : 0) + (Settings.ManaProtectsLife ? Life.CurMana : 0) +
                             (Settings.ShowWard ? CurrentWard : 0);

    public int MaxEhp => (Settings.ShowLife ? Life.MaxHP : 0) + (Settings.ShowES ? Life.MaxES : 0) + (Settings.ManaProtectsLife ? Life.MaxMana : 0) +
                         (Settings.ShowWard ? MaxWard : 0);
    public readonly Queue<(DateTime Time, int Value)> EhpHistory = new Queue<(DateTime, int)>();

    public Color Color
    {
        get
        {
            if (IsHidden(Entity))
                return Color.LightGray;

            if (ShouldDrawCullingStrikeIndicator())
                return Settings.CullableColor;

            return Settings.LifeColor;
        }
    }

    private static bool IsHidden(Entity entity)
    {
        try
        {
            return entity.IsHidden;
        }
        catch
        {
            return false;
        }
    }

    private void Update()
    {
        Type = GetEntityType();
    }

    private CreatureType GetEntityType()
    {
        if (Entity.HasComponent<Player>())
        {
            return CreatureType.Player;
        }

        if (Entity.HasComponent<Monster>())
        {
            var objectMagicProperties = Entity.GetComponent<ObjectMagicProperties>();
            if (Entity.IsHostile)
            {
                return objectMagicProperties?.Rarity switch
                {
                    MonsterRarity.White => CreatureType.Normal,
                    MonsterRarity.Magic => CreatureType.Magic,
                    MonsterRarity.Rare => CreatureType.Rare,
                    MonsterRarity.Unique => CreatureType.Unique,
                    _ => CreatureType.Minion
                };
            }

            return CreatureType.Minion;
        }

        return CreatureType.Minion;
    }

    private void DpsRefresh()
    {
        if (_dpsStopwatch.ElapsedMilliseconds >= 200)
        {
            var hp = CurrentEhp;
            if (hp == MaxEhp && EhpHistory.TryPeek(out var entry) && hp == entry.Value)
            {
                EhpHistory.Clear();
            }
            else
            {
                while (EhpHistory.TryPeek(out entry) &&
                       DateTime.UtcNow - entry.Time > TimeSpan.FromMilliseconds(AllSettings.DpsEstimateDuration))
                {
                    EhpHistory.Dequeue();
                }
            }

            EhpHistory.Enqueue((DateTime.UtcNow, hp));
        }
    }
    internal bool ShouldDrawCullingStrikeIndicator()
    {
        if (!AllSettings.HasCullingStrike)
            return false;

        float cullingMultiplier = 1.0f + (AllSettings.CullingThreshold / 100f);

        // PoE2 0.5 "Runes of Aldur" rarity-tiered Culling Strike (Legacy of Deathblow rune):
        // Normal 35% / Magic 20% / Rare 10% / Unique 5%.
        return Type switch
        {
            CreatureType.Normal => HpPercent <= 0.35f * cullingMultiplier,
            CreatureType.Magic => HpPercent <= 0.20f * cullingMultiplier,
            CreatureType.Rare => HpPercent <= 0.10f * cullingMultiplier,
            CreatureType.Unique => HpPercent <= 0.05f * cullingMultiplier,
            _ => false
        };
    }
}
