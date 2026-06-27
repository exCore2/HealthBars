using System.Drawing;
using System.Numerics;
using ExileCore2.Shared.Attributes;
using ExileCore2.Shared.Interfaces;
using ExileCore2.Shared.Nodes;
using Newtonsoft.Json;

namespace HealthBars;

public class HealthBarsSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new(true);

    public ToggleNode IgnoreUiElementVisibility { get; set; } = new(false);
    public ToggleNode ShowInTown { get; set; } = new(false);
    public ToggleNode ShowInHideout { get; set; } = new(true);
    public ToggleNode EnableAbsolutePlayerBarPositioning { get; set; } = new(false);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning))]
    public RangeNode<Vector2> PlayerBarPosition { get; set; } = new(new Vector2(1000, 1000), Vector2.Zero, Vector2.One * 4000);

    public RangeNode<float> SmoothingFactor { get; set; } = new(0, 0, 0.99f);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public RangeNode<float> PlayerSmoothingFactor { get; set; } = new(1, 0, 1);

    public RangeNode<int> GlobalZOffset { get; set; } = new(0, -300, 300);

    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public RangeNode<int> PlayerZOffset { get; set; } = new(0, -300, 300);

    [Menu(null, "By default, bar is placed relative to the model top")]
    public ToggleNode PlaceBarRelativeToGroundLevel { get; set; } = new(false);

    [Menu(null, "By default, bar is placed relative to the model top")]
    [ConditionalDisplay(nameof(EnableAbsolutePlayerBarPositioning), false)]
    public ToggleNode PlacePlayerBarRelativeToGroundLevel { get; set; } = new(false);

    public RangeNode<int> DrawDistanceLimit { get; set; } = new(133, 0, 1000);
    public RangeNode<int> ShowMinionOnlyWhenBelowHp { get; set; } = new(50, 1, 100);
    public RangeNode<int> DpsEstimateDuration { get; set; } = new(2000, 500, 10000);
    [Menu("Culling Strike settings", 100)]
    public EmptyNode CullingStrike { get; set; }
    [Menu("Has Culling Strike","You have a source of Culling Strike such as from a passive or support gem", 100)]
    public ToggleNode HasCullingStrike { get; set; } = new(false);
    [ConditionalDisplay(nameof(HasCullingStrike), true)]
    [Menu("Culling Strike Threshold", "If you have Culling the Hordes or some other threshold increase", 100)]
    public RangeNode<int> CullingThreshold { get; set; } = new(0, 0, 300);

    public ColorNode CombatDamageColor { get; set; } = Color.Red;
    public ColorNode CombatHealColor { get; set; } = Color.Green;
    public ToggleNode ResizeBarsToFitText { get; set; } = new(true);
    public ToggleNode UseShadedTexture { get; set; } = new(true);

    [JsonIgnore]
    public ButtonNode ExportDefaultConfig { get; set; } = new ButtonNode();

    public ToggleNode UseOldConfigFormat { get; set; } = new(false);

    public BossOverlaySettings BossOverlaySettings { get; set; } = new();
    public CommonCastBarSettings CommonCastBarSettings { get; set; } = new();

    public UnitSettings Self { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x008000),
        TextFormat = { Value = "{percent}%" },
        ShowDps = { Value = false },
    };

    public UnitSettings UniqueEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xffa500),
        OutlineColor = Extensions.FromHex(0xffa500),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 200 },
        Height = { Value = 25 },
        IncludeInBossOverlay = { Value = true },
    };

    public UnitSettings RareEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xf4ff19),
        OutlineColor = Extensions.FromHex(0xf4ff19),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 125 },
        Height = { Value = 20 },
    };

    public UnitSettings MagicEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x8888ff),
        OutlineColor = Extensions.FromHex(0x8888ff),
        TextColor = Extensions.FromHex(0x66ff99),
        Width = { Value = 100 },
        Height = { Value = 15 },
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings NormalEnemy { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0xff0000),
        TextColor = Extensions.FromHex(0x66ff66),
        Width = { Value = 75 },
        Height = { Value = 10 },
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings Minions { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x90ee90),
        TextFormat = { Value = "" },
        ShowDps = { Value = false },
    };

    public UnitSettings Players { get; set; } = new()
    {
        LifeColor = Extensions.FromHex(0x008000),
        TextFormat = { Value = "{percent}%" },
        ShowDps = { Value = false },
    };
}

[Submenu(CollapsedByDefault = true)]
public class UnitSettings
{
    public ToggleNode Show { get; set; } = new(true);
    public RangeNode<float> Width { get; set; } = new(100, 20, 250);
    public RangeNode<float> Height { get; set; } = new(20, 5, 150);
    public RangeNode<int> OutlineThickness { get; set; } = new(1, 0, 20);
    public RangeNode<int> HealthSegments { get; set; } = new(1, 1, 10);
    public RangeNode<float> HealthSegmentHeight { get; set; } = new(0.5f, 0, 1);
    public ColorNode LifeColor { get; set; } = Color.White;
    public ColorNode WardColor { get; set; } = Color.BlueViolet;
    public ColorNode EsColor { get; set; } = Color.Aqua;
    public ColorNode ManaColor { get; set; } = Color.Blue;
    public ColorNode CullableColor { get; set; } = Color.White;
    public ColorNode BackgroundColor { get; set; } = Color.Black;
    public ColorNode OutlineColor { get; set; } = Color.Transparent;
    public ColorNode TextColor { get; set; } = Color.White;
    public ColorNode TextBackground { get; set; } = Color.Black;
    public ColorNode HealthSegmentColor { get; set; } = Color.Black;
    public RangeNode<Vector2> TextPosition { get; set; } = new(new Vector2(0, -1), new Vector2(-1, -1), new Vector2(1, 1));
    public ToggleNode ShowDps { get; set; } = new(true);
    public RangeNode<float> HoverOpacity { get; set; } = new(1, 0, 1);
    [Menu(null, "Combines Life and ES into a single bar that's filled proportionally to the total EHP")]
    public ToggleNode CombineLifeAndEs { get; set; } = new(true);
    [Menu(null, "Combines Life and Mana into a single bar that's filled proportionally to the total EHP")]
    public ToggleNode CombineLifeAndMana { get; set; } = new(false);
    public ToggleNode ManaProtectsLife { get; set; } = new(false);
    public ToggleNode ShowWard { get; set; } = new(true);

    [ConditionalDisplay(nameof(CombineLifeAndEs), false)]
    public RangeNode<float> EsBarHeight { get; set; } = new(1 / 3f, 0, 1);

    public ToggleNode IncludeInBossOverlay { get; set; } = new(false);

    [Menu(null,
        "Text template to show next to the bar. Available variables are:\n" +
        "{percent} - current percent of total HP (Life + ES)\n" +
        "{current} - current HP (Life + ES) value\n" +
        "{total} - total HP (Life + ES)\n" +
        "{currentes} - current ES\n" +
        "{currentlife} - current Life\n\n" +
        "Example:\n" +
        "{percent}% {current}/{total} -> 50% 5.00K/10.0K")]
    public TextNode TextFormat { get; set; } = "{percent}% {current}/{total}";

    public CastBarSettings CastBarSettings { get; set; } = new();
}

[Submenu]
public class CastBarSettings
{
    public ToggleNode Show { get; set; } = new(false);
    public RangeNode<float> Height { get; set; } = new(30, 5, 150);
    public ColorNode BackgroundColor { get; set; } = Color.Black;
    public ColorNode FillColor { get; set; } = Color.Aqua;
    public ColorNode DangerTextColor { get; set; } = Color.Red;
    public ColorNode NoDangerTextColor { get; set; } = Color.Blue;
    public ColorNode StageTextColor { get; set; } = Color.White;
    public RangeNode<float> YOffset { get; set; } = new(0, -5, 5);
    public ToggleNode ShowStageNames { get; set; } = new(true);
}

[Submenu(CollapsedByDefault = true)]
public class CommonCastBarSettings
{
    public RangeNode<int> MaxSkillNameLength { get; set; } = new(5, 0, 30);
    public RangeNode<int> MaxSkillNameLengthForBossOverlay { get; set; } = new(20, 0, 30);
    public ToggleNode ShowNextStageName { get; set; } = new(true);
    public ToggleNode ShowNextStageNameInBossOverlay { get; set; } = new(true);
    public ToggleNode DebugShowAllSkillStages { get; set; } = new(false);

    [Menu(null, "Remove part of castbar after the last \"interesting\" point unless there are none in the animation")]
    public ToggleNode CutOffBackswing { get; set; } = new(true);
}

[Submenu(CollapsedByDefault = true)]
public class BossOverlaySettings
{
    public ToggleNode Show { get; set; } = new(true);
    public RangeNode<int> ItemSpacing { get; set; } = new(100, 0, 500);
    public RangeNode<int> BarHeight { get; set; } = new(20, 0, 150);
    public RangeNode<int> Width { get; set; } = new(500, 0, 1000);
    public RangeNode<Vector2> Location { get; set; } = new(new Vector2(10, 300), Vector2.Zero, Vector2.One * 4000);
    public ToggleNode ShowCastBarStageNames { get; set; } = new ToggleNode(false);
    public ToggleNode ShowMonsterNames { get; set; } = new ToggleNode(true);
    public RangeNode<int> MaxEntries { get; set; } = new(5, 0, 10);
}
