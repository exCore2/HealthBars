using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Cache;
using ExileCore2.Shared.Enums;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using Newtonsoft.Json;
using RectangleF = ExileCore2.Shared.RectangleF;
using Vector2 = System.Numerics.Vector2;

namespace HealthBars;

public class HealthBars : BaseSettingsPlugin<HealthBarsSettings>
{
    private const string ShadedHealthbarTexture = "healthbar.png";
    private const string FlatHealthbarTexture = "chest.png";
    private string OldConfigPath => Path.Combine(DirectoryFullName, "config", "ignored_entities.txt");
    private string NewConfigCustomPath => Path.Join(ConfigDirectory, "entityConfig.json");

    private Camera Camera => GameController.IngameState.Camera;
    private IngameUIElements IngameUi => GameController.IngameState.IngameUi;
    private Vector2 WindowRelativeSize => new Vector2(_windowRectangle.Value.Width / 2560, _windowRectangle.Value.Height / 1600);
    private string HealthbarTexture => TexturePrefix + (Settings.UseShadedTexture ? ShadedHealthbarTexture : FlatHealthbarTexture);

    private readonly ConcurrentDictionary<string, EntityTreatmentRule> _pathRuleCache = new();
    private static readonly EntityTreatmentRule EmptyTreatmentRule = new();
    private bool _canTick = true;
    private IndividualEntityConfig _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig());
    private Vector2 _oldPlayerCoord;
    private HealthBar _playerBar;
    private CachedValue<bool> _ingameUiCheckVisible;
    private CachedValue<RectangleF> _windowRectangle;
    private bool _settingsHooksAttached;

    public override void OnLoad()
    {
        CanUseMultiThreading = true;
        Graphics.InitImage(TexturePrefix + ShadedHealthbarTexture, Path.Combine(DirectoryFullName, ShadedHealthbarTexture));
        Graphics.InitImage(TexturePrefix + FlatHealthbarTexture, Path.Combine(DirectoryFullName, FlatHealthbarTexture));
    }

    public override bool Initialise()
    {
        _windowRectangle = new TimeCache<RectangleF>(() =>
            GameController.Window.GetWindowRectangleReal() with { Location = Vector2.Zero }, 250);
        _ingameUiCheckVisible = new TimeCache<bool>(() =>
            IngameUi.FullscreenPanels.Any(x => x.IsVisibleLocal) ||
            IngameUi.LargePanels.Any(x => x.IsVisibleLocal), 250);
        LoadConfig();
        Settings.PlayerZOffset.OnValueChanged += OnPlayerPositionSettingChanged;
        Settings.PlacePlayerBarRelativeToGroundLevel.OnValueChanged += OnPlayerPositionSettingChanged;
        Settings.EnableAbsolutePlayerBarPositioning.OnValueChanged += OnPlayerPositionSettingChanged;
        Settings.Enable.OnValueChanged += OnEnableChanged;
        Settings.ExportDefaultConfig.OnPressed += ExportDefaultConfig;
        _settingsHooksAttached = true;
        return true;
    }

    private void OnPlayerPositionSettingChanged(object _, int __) => _oldPlayerCoord = Vector2.Zero;
    private void OnPlayerPositionSettingChanged(object _, bool __) => _oldPlayerCoord = Vector2.Zero;

    private void OnEnableChanged(object _, bool enabled)
    {
        if (!enabled) _canTick = false;
    }

    private void ExportDefaultConfig()
    {
        try
        {
            var directory = Path.GetDirectoryName(NewConfigCustomPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(NewConfigCustomPath, GetEmbeddedConfigString());
        }
        catch (Exception ex) { DebugWindow.LogError($"HealthBars config export failed: {ex}"); }
    }

    public override void OnPluginDestroyForHotReload()
    {
        DetachSettingsHooks();
        base.OnPluginDestroyForHotReload();
    }

    public override void Dispose()
    {
        DetachSettingsHooks();
        base.Dispose();
    }

    private void DetachSettingsHooks()
    {
        if (!_settingsHooksAttached) return;
        Settings.PlayerZOffset.OnValueChanged -= OnPlayerPositionSettingChanged;
        Settings.PlacePlayerBarRelativeToGroundLevel.OnValueChanged -= OnPlayerPositionSettingChanged;
        Settings.EnableAbsolutePlayerBarPositioning.OnValueChanged -= OnPlayerPositionSettingChanged;
        Settings.Enable.OnValueChanged -= OnEnableChanged;
        Settings.ExportDefaultConfig.OnPressed -= ExportDefaultConfig;
        _settingsHooksAttached = false;
    }

    private void LoadConfig()
    {
        _pathRuleCache.Clear();
        if (Settings.UseOldConfigFormat)
        {
            LoadOldEntityConfigFormat();
        }
        else
        {
            if (File.Exists(NewConfigCustomPath))
            {
                try
                {
                    var content = File.ReadAllText(NewConfigCustomPath);
                    _entityConfig = new IndividualEntityConfig(JsonConvert.DeserializeObject<SerializedIndividualEntityConfig>(content));
                    return;
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError($"Unable to load custom config file, falling back to default: {ex}");
                }
            }

            _entityConfig = LoadEmbeddedConfig();
        }
    }

    private static IndividualEntityConfig LoadEmbeddedConfig()
    {
        var content = GetEmbeddedConfigString();
        return new IndividualEntityConfig(JsonConvert.DeserializeObject<SerializedIndividualEntityConfig>(content));
    }

    private static string GetEmbeddedConfigString()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("entityConfig.default.json");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        return content;
    }

    private void LoadOldEntityConfigFormat()
    {
        if (File.Exists(OldConfigPath))
        {
            var ignoredEntities = File.ReadAllLines(OldConfigPath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Where(line => !line.StartsWith("#"))
                .ToList();
            _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig
            {
                EntityPathConfig = ignoredEntities.ToDictionary(
                    x => $"^{Regex.Escape(x)}",
                    _ => new EntityTreatmentRule { Ignore = true }),
            });
        }
        else
        {
            _entityConfig = new IndividualEntityConfig(new SerializedIndividualEntityConfig());
            LogError($"Ignored entities file does not exist. Path: {OldConfigPath}");
        }
    }


    public override void AreaChange(AreaInstance area)
    {
        _oldPlayerCoord = Vector2.Zero;
        LoadConfig();
    }

    private bool SkipHealthBar(HealthBar healthBar, bool checkDistance)
    {
        if (checkDistance && (!float.IsFinite(healthBar.Distance) || healthBar.Distance > Settings.DrawDistanceLimit)) return true;
        if (healthBar.Life == null) return true;
        if (!healthBar.Entity.IsAlive) return true;
        if (!float.IsFinite(healthBar.HpPercent) || healthBar.HpPercent < 0.001f) return true;

        return false;
    }

    private void HpBarWork(HealthBar healthBar)
    {
        healthBar.Skip = SkipHealthBar(healthBar, true);
        if (healthBar.Skip && !ShowInBossOverlay(healthBar)) return;

        healthBar.CheckUpdate();
        if ((healthBar.Settings?.Show != true ||
             healthBar.Type == CreatureType.Minion && healthBar.HpPercent * 100 > Settings.ShowMinionOnlyWhenBelowHp) &&
            !ShowInBossOverlay(healthBar))
        {
            healthBar.Skip = true;
            return;
        }

        var worldCoords = healthBar.Entity.Pos;
        if (!Settings.PlaceBarRelativeToGroundLevel)
        {
            if (healthBar.Entity.GetComponent<Render>()?.Bounds is { } boundsNum)
            {
                worldCoords.Z -= 2 * boundsNum.Z;
            }
        }

        worldCoords.Z += Settings.GlobalZOffset;
        var mobScreenCoords = Camera.WorldToScreen(worldCoords);
        if (mobScreenCoords == Vector2.Zero || !IsFinite(mobScreenCoords)) return;
        mobScreenCoords = Vector2.Lerp(mobScreenCoords, healthBar.LastPosition, healthBar.LastPosition == Vector2.Zero ? 0 : Math.Clamp(Settings.SmoothingFactor, 0, 1));
        healthBar.LastPosition = mobScreenCoords;
        var scaledWidth = healthBar.Settings.Width * WindowRelativeSize.X;
        var scaledHeight = healthBar.Settings.Height * WindowRelativeSize.Y;

        healthBar.DisplayArea = new RectangleF(mobScreenCoords.X - scaledWidth / 2f, mobScreenCoords.Y - scaledHeight / 2f, scaledWidth,
            scaledHeight);

        if (healthBar.Distance > 80 && !_windowRectangle.Value.Intersects(healthBar.DisplayArea))
        {
            healthBar.Skip = true;
        }
    }

    public override void Tick()
    {
        if (!Settings.Enable)
        {
            _canTick = false;
            return;
        }
        _canTick = true;

        if (!Settings.IgnoreUiElementVisibility && _ingameUiCheckVisible?.Value == true ||
            Camera == null ||
            !Settings.ShowInTown && GameController.Area.CurrentArea.IsTown ||
            !Settings.ShowInHideout && GameController.Area.CurrentArea.IsHideout)
        {
            _canTick = false;
            return;
        }

        TickLogic();
    }

    private void TickLogic()
    {
        foreach (var validEntity in EnumerateHealthBarEntities())
        {
            var healthBar = validEntity.GetHudComponent<HealthBar>();
            if (healthBar == null) continue;

            try
            {
                HpBarWork(healthBar);
            }
            catch (Exception e)
            {
                DebugWindow.LogError(e.Message);
            }
        }

        PositionPlayerBar();
    }

    private void PositionPlayerBar()
    {
        if (!Settings.Self.Show || _playerBar is not { } playerBar)
        {
            return;
        }

        var worldCoords = playerBar.Entity.Pos;
        if (!Settings.PlacePlayerBarRelativeToGroundLevel)
        {
            if (playerBar.Entity.GetComponent<Render>()?.Bounds is { } boundsNum)
            {
                worldCoords.Z -= 2 * boundsNum.Z;
            }
        }

        worldCoords.Z += Settings.PlayerZOffset;
        var result = Camera.WorldToScreen(worldCoords);
        if (!IsFinite(result)) return;

        if (Settings.EnableAbsolutePlayerBarPositioning)
        {
            _oldPlayerCoord = result = Settings.PlayerBarPosition;
        }
        else
        {
            if (_oldPlayerCoord == Vector2.Zero)
            {
                _oldPlayerCoord = result;
            }
            else if (Settings.PlayerSmoothingFactor >= 1)
            {
                if ((_oldPlayerCoord - result).LengthSquared() < 40 * 40)
                    result = _oldPlayerCoord;
                else
                    _oldPlayerCoord = result;
            }
            else
            {
                result = Vector2.Lerp(result, _oldPlayerCoord, _oldPlayerCoord == Vector2.Zero ? 0 : Math.Max(0, Settings.PlayerSmoothingFactor));
                _oldPlayerCoord = result;
            }
        }

        var scaledWidth = playerBar.Settings.Width * WindowRelativeSize.X;
        var scaledHeight = playerBar.Settings.Height * WindowRelativeSize.Y;

        var background = new RectangleF(result.X, result.Y, 0, 0);
        background.Inflate(scaledWidth / 2f, scaledHeight / 2f);
        playerBar.DisplayArea = background;
    }

    public override void Render()
    {
        if (!Settings.Enable || !_canTick) return;
        var bossOverlayItems = new List<HealthBar>();
        foreach (var entity in EnumerateHealthBarEntities())
        {
            if (entity.GetHudComponent<HealthBar>() is not { } healthBar)
            {
                continue;
            }

            if (!healthBar.Skip)
            {
                try
                {
                    DrawBar(healthBar);
                    if (IsCastBarEnabled(healthBar))
                    {
                        var lifeArea = healthBar.DisplayArea;
                        DrawCastBar(healthBar,
                            lifeArea with
                            {
                                Y = lifeArea.Y + lifeArea.Height * (healthBar.Settings.CastBarSettings.YOffset + 1),
                                Height = healthBar.Settings.CastBarSettings.Height,
                            }, healthBar.Settings.CastBarSettings.ShowStageNames,
                            Settings.CommonCastBarSettings.ShowNextStageName,
                            Settings.CommonCastBarSettings.MaxSkillNameLength);
                    }
                }
                catch (Exception ex)
                {
                    DebugWindow.LogError(ex.ToString());
                }
            }

            if (ShowInBossOverlay(healthBar) && !SkipHealthBar(healthBar, false))
            {
                bossOverlayItems.Add(healthBar);
            }
        }

        bossOverlayItems.Sort((x, y) => x.StableId.CompareTo(y.StableId));
        DrawBossOverlay(bossOverlayItems);
    }

    private IEnumerable<Entity> EnumerateHealthBarEntities()
    {
        if (GameController.EntityListWrapper?.ValidEntitiesByType is not { } byType)
        {
            yield break;
        }

        if (byType.TryGetValue(EntityType.Monster, out var monsters) && monsters is not null)
        {
            foreach (var entity in monsters)
            {
                yield return entity;
            }
        }

        if (byType.TryGetValue(EntityType.Player, out var players) && players is not null)
        {
            foreach (var entity in players)
            {
                yield return entity;
            }
        }
    }

    private void DrawBossOverlay(IEnumerable<HealthBar> items)
    {
        if (!Settings.BossOverlaySettings.Show)
        {
            return;
        }

        var barPosition = Settings.BossOverlaySettings.Location.Value;
        foreach (var healthBar in items.Take(Settings.BossOverlaySettings.MaxEntries))
        {
            try
            {
                var lifeRect = new RectangleF(barPosition.X, barPosition.Y, Settings.BossOverlaySettings.Width, Settings.BossOverlaySettings.BarHeight);
                DrawBar(healthBar, lifeRect, false, false, Settings.BossOverlaySettings.ShowMonsterNames ? healthBar.Entity.RenderName : null);
                barPosition.Y += lifeRect.Height;
                if (IsCastBarEnabled(healthBar))
                {
                    DrawCastBar(healthBar, lifeRect with { Y = lifeRect.Bottom },
                        Settings.BossOverlaySettings.ShowCastBarStageNames,
                        Settings.CommonCastBarSettings.ShowNextStageNameInBossOverlay,
                        Settings.CommonCastBarSettings.MaxSkillNameLengthForBossOverlay);
                    barPosition.Y += lifeRect.Height;
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError(ex.ToString());
            }

            barPosition.Y += Settings.BossOverlaySettings.ItemSpacing;
        }
    }

    private void DrawBar(HealthBar bar)
    {
        var enableResizing = Settings.ResizeBarsToFitText;
        var showDps = bar.Settings.ShowDps;
        DrawBar(bar, bar.DisplayArea, enableResizing, showDps, null);
    }

    private void DrawBar(HealthBar bar, RectangleF barArea, bool enableResizing, bool showDps, string textPrefix)
    {
        var barText = $"{textPrefix} {GetTemplatedText(bar)}";
        barText = string.IsNullOrWhiteSpace(barText) ? null : barText.Trim();

        if (barText != null && enableResizing)
        {
            var barTextSize = Graphics.MeasureText(barText);
            barArea.Inflate(Math.Max(0, (barTextSize.X - barArea.Width) / 2), Math.Max(0, (barTextSize.Y - barArea.Height) / 2));
        }

        var alphaMulti = GetAlphaMulti(bar, barArea);
        if (alphaMulti == 0)
        {
            return;
        }

        Graphics.DrawImage(HealthbarTexture, barArea, bar.Settings.BackgroundColor.MultiplyAlpha(alphaMulti));
        var barSources = new List<(float Current, float Max, Color Color)>();
        if (bar.Settings.ShowWard)
        {
            barSources.Add((bar.CurrentWard, bar.MaxWard, bar.Settings.WardColor));
        }

        if (bar.Settings.ShowLife)
        {
            barSources.Add((bar.Life.CurHP, bar.Life.MaxHP, bar.Color));
        }

        if (bar.Settings.ShowES && bar.Settings.CombineLifeAndEs)
        {
            barSources.Add((bar.Life.CurES, bar.Life.MaxES, bar.Settings.EsColor));
        }

        if (bar.Settings.CombineLifeAndMana)
        {
            barSources.Add((bar.Life.CurMana, bar.Life.MaxMana, bar.Settings.ManaColor));
        }

        var totalPool = barSources.Sum(x => x.Max);
        if (totalPool > 0)
        {
            var currentLeft = barArea.Left;
            foreach (var barSource in barSources)
            {
                if (barSource.Current > 0)
                {
                    var width = barArea.Width * barSource.Current / totalPool;
                    Graphics.DrawImage(HealthbarTexture, barArea with { Left = currentLeft, Width = width }, barSource.Color.MultiplyAlpha(alphaMulti));
                    currentLeft += width;
                }
            }
        }

        if (bar.Settings.ShowES && !bar.Settings.CombineLifeAndEs)
        {
            var esWidth = barArea.Width * bar.EsPercent;
            Graphics.DrawImage(HealthbarTexture, new RectangleF(barArea.X, barArea.Y, esWidth, barArea.Height * bar.Settings.EsBarHeight),
                bar.Settings.EsColor.MultiplyAlpha(alphaMulti));
        }

        var segmentCount = bar.Settings.HealthSegments.Value;
        for (int i = 1; i < segmentCount; i++)
        {
            var x = i / (float)segmentCount * barArea.Width;
            var notchRect = new RectangleF(
                barArea.X + x,
                barArea.Bottom - barArea.Height * bar.Settings.HealthSegmentHeight,
                1,
                barArea.Height * bar.Settings.HealthSegmentHeight);
            Graphics.DrawImage(FlatHealthbarTexture, notchRect, bar.Settings.HealthSegmentColor.MultiplyAlpha(alphaMulti));
        }

        if (bar.Settings.OutlineThickness > 0 && bar.Settings.OutlineColor.Value.A > 0)
        {
            var outlineRect = barArea;
            outlineRect.Inflate(1, 1);
            Graphics.DrawFrame(outlineRect, bar.Settings.OutlineColor.MultiplyAlpha(alphaMulti), bar.Settings.OutlineThickness.Value);
        }

        ShowHealthbarText(bar, barText, alphaMulti, barArea);
        if (showDps)
        {
            ShowDps(bar, alphaMulti, barArea);
        }
    }

    private static float GetAlphaMulti(HealthBar bar, RectangleF barArea)
    {
        // ReSharper disable once CompareOfFloatsByEqualityOperator
        var alphaMulti = bar.Settings.HoverOpacity != 1
                         && ImGui.IsMouseHoveringRect(barArea.TopLeft, barArea.BottomRight, false)
            ? bar.Settings.HoverOpacity
            : 1f;
        return alphaMulti;
    }

    private void ShowDps(HealthBar bar, float alphaMulti, RectangleF area)
    {
        const int margin = 2;
        if (bar.EhpHistory.Count < 2) return;
        var hpFirst = bar.EhpHistory.First();
        var hpLast = bar.EhpHistory.Last();

        var timeDiff = hpLast.Time - hpFirst.Time;
        var hpDiff = hpFirst.Value - hpLast.Value;

        var dps = hpDiff / timeDiff.TotalSeconds;
        if (dps == 0)
        {
            return;
        }

        var damageColor = dps < 0
            ? Settings.CombatHealColor
            : Settings.CombatDamageColor;

        var dpsText = dps.FormatHp();
        var textArea = Graphics.MeasureText(dpsText);
        var textCenter = new Vector2(area.Center.X, area.Bottom + textArea.Y / 2 + margin);
        Graphics.DrawBox(textCenter - textArea / 2, textCenter + textArea / 2, bar.Settings.TextBackground.MultiplyAlpha(alphaMulti));
        Graphics.DrawText(dpsText, textCenter - textArea / 2, damageColor.MultiplyAlpha(alphaMulti));
    }

    private void ShowHealthbarText(HealthBar bar, string text, float alphaMulti, RectangleF area)
    {
        if (text != null)
        {
            var textArea = Graphics.MeasureText(text);
            var barCenter = area.Center;
            var textOffset = bar.Settings.TextPosition.Value.Mult(area.Width + textArea.X, area.Height + textArea.Y) / 2;
            var textCenter = barCenter + textOffset;
            var textTopLeft = textCenter - textArea / 2;
            var textRect = new RectangleF(textTopLeft.X, textTopLeft.Y, textArea.X, textArea.Y);
            area.Contains(ref textRect, out var textIsInsideBar);
            if (!textIsInsideBar)
            {
                Graphics.DrawBox(textTopLeft, textTopLeft + textArea, bar.Settings.TextBackground.MultiplyAlpha(alphaMulti));
            }

            Graphics.DrawText(text, textTopLeft, bar.Settings.TextColor.MultiplyAlpha(alphaMulti));
        }
    }

    private static string GetTemplatedText(HealthBar bar)
    {
        var textFormat = bar.Settings.TextFormat.Value;
        if (string.IsNullOrWhiteSpace(textFormat))
        {
            return null;
        }

        return textFormat
            .Replace("{percent}", Math.Floor(bar.EhpPercent * 100).ToString(CultureInfo.InvariantCulture))
            .Replace("{current}", bar.CurrentEhp.FormatHp())
            .Replace("{total}", bar.MaxEhp.FormatHp())
            .Replace("{currentes}", bar.Life.CurES.FormatHp())
            .Replace("{currentlife}", bar.Life.CurHP.FormatHp())
            .Replace("{currentmana}", bar.Life.CurMana.FormatHp())
            .Replace("{currentward}", bar.CurrentWard.FormatHp())
            ;
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static readonly HashSet<string> DangerousStages =
    [
        "contact",
        "slam",
        "teleport",
        "small_beam_blast",
        "medium_beam_blast",
        "large_beam_blast",
        "clone_beam_blast",
        "beam_l",
        "beam_r",
        "clap",
        "stab",
        "slash",
        "ice_shard",
        "wind_force",
        "wave",
    ];

    private static readonly string TexturePrefix = "hb_";

    private void DrawCastBar(HealthBar bar, RectangleF area, bool drawStageNames, bool showNextStageName, int maxSkillNameLength)
    {
        if (!bar.Entity.TryGetComponent<Actor>(out var actor))
        {
            return;
        }

        if (actor?.AnimationController is not { } ac || actor.Action != ActionFlags.UsingAbility || ac.RawAnimationSpeed == 0)
        {
            return;
        }

        var stages = ac.CurrentAnimation.AllStages.ToList();
        var settings = bar.Settings.CastBarSettings;
        var maxRawProgress = Settings.CommonCastBarSettings.CutOffBackswing
            ? stages.LastOrDefault(x => DangerousStages.Contains(x.StageNameSafe()))?.StageStart ?? ac.MaxRawAnimationProgress
            : ac.MaxRawAnimationProgress;
        if (ac.RawAnimationProgress > maxRawProgress)
        {
            return;
        }

        var alphaMulti = GetAlphaMulti(bar, area);
        if (alphaMulti == 0)
        {
            return;
        }

        var width = area.Width;
        var height = area.Height;
        var maxProgress = ac.TransformProgress(maxRawProgress);
        var topLeft = area.TopLeft;
        var bottomRight = topLeft + new Vector2(width, height);
        Graphics.DrawBox(topLeft, bottomRight, settings.BackgroundColor.MultiplyAlpha(alphaMulti));
        Graphics.DrawBox(topLeft, topLeft + new Vector2(width * ac.TransformedRawAnimationProgress / maxProgress, height), settings.FillColor.MultiplyAlpha(alphaMulti));

        var nextDangerousStage = stages.FirstOrDefault(x => x.StageStart > ac.RawAnimationProgress && DangerousStages.Contains(x.StageNameSafe()));
        var stageIn = nextDangerousStage != null
            ? (ac.TransformProgress(nextDangerousStage.StageStart) - ac.TransformedRawAnimationProgress) / ac.AnimationSpeed
            : ac.AnimationCompletesIn.TotalSeconds;
        var mainText = (nextDangerousStage != null && showNextStageName, maxSkillNameLength) switch
        {
            (true, <= 0) => $"{nextDangerousStage?.StageNameSafe()} in {stageIn:F1}",
            (false, <= 0) => $"{stageIn:F1}",
            (true, var v and > 0) => $"{actor.CurrentAction?.Skill?.Name?.Truncate(v)} {nextDangerousStage?.StageNameSafe()} in {stageIn:F1}",
            (false, var v and > 0) => $"{actor.CurrentAction?.Skill?.Name?.Truncate(v)} in {stageIn:F1}",
        };
        var oldTextSize = Graphics.MeasureText(mainText);
        using (Graphics.SetTextScale(Math.Min(height / oldTextSize.Y, width / oldTextSize.X)))
        {
            var color = (nextDangerousStage != null ? settings.DangerTextColor : settings.NoDangerTextColor).MultiplyAlpha(alphaMulti);
            Graphics.DrawText(mainText, topLeft, color);
        }

        var occupiedSlots = new Dictionary<int, float>();
        var textLineHeight = Graphics.MeasureText("A").Y;
        var displayAllSkillStages = Settings.CommonCastBarSettings.DebugShowAllSkillStages;
        foreach (var stage in stages.Where(x => displayAllSkillStages || DangerousStages.Contains(x.StageNameSafe())))
        {
            var normalizedStageStart = ac.TransformProgress(stage.StageStart) / maxProgress;
            if (ReferenceEquals(stage, nextDangerousStage) && Math.Abs(normalizedStageStart - 1) < 1e-3)
            {
                continue;
            }

            var stageX = topLeft.X + normalizedStageStart * width;
            if (drawStageNames)
            {
                var line = Enumerable.Range(0, 100).FirstOrDefault(x => occupiedSlots.GetValueOrDefault(x, float.NegativeInfinity) < stageX);
                var text = displayAllSkillStages ? $"{normalizedStageStart}:{stage.StageNameSafe()}" : $"{stage.StageNameSafe()}";
                var textSize = Graphics.MeasureText(text);
                occupiedSlots[line] = stageX + textSize.X + 20;
                var textStart = new Vector2(stageX, topLeft.Y + height + line * textLineHeight);
                Graphics.DrawBox(textStart, textStart + textSize, settings.BackgroundColor.MultiplyAlpha(alphaMulti));
                Graphics.DrawText(text, textStart, settings.StageTextColor.MultiplyAlpha(alphaMulti));
                Graphics.DrawLine(textStart, topLeft with { X = textStart.X }, 1, Color.Green.MultiplyAlpha(alphaMulti));
            }
            else
            {
                Graphics.DrawLine(topLeft with { X = stageX }, bottomRight with { X = stageX }, 1, Color.Green.MultiplyAlpha(alphaMulti));
            }
        }
    }

    public override void EntityAdded(Entity entity)
    {
        if (entity.Type != EntityType.Monster && entity.Type != EntityType.Player ||
            entity.GetComponent<Life>() != null && !entity.IsAlive ||
            FindRule(entity.Path).Ignore == true)
        {
            return;
        }

        var healthBar = new HealthBar(entity, Settings);
        entity.SetHudComponent(healthBar);
        if (entity.Address == GameController.Player.Address)
        {
            _playerBar = healthBar;
        }
    }

    private EntityTreatmentRule FindRule(string path)
    {
        return _pathRuleCache.GetOrAdd(path, p => _entityConfig.Rules.FirstOrDefault(x => x.Regex.IsMatch(p)).Rule ?? EmptyTreatmentRule);
    }

    private bool ShowInBossOverlay(HealthBar bar)
    {
        return Settings.BossOverlaySettings.Show &&
               (FindRule(bar.Entity.Path).ShowInBossOverlay ?? bar.Settings.IncludeInBossOverlay.Value);
    }

    private bool IsCastBarEnabled(HealthBar bar)
    {
        return FindRule(bar.Entity.Path).ShowCastBar ?? bar.Settings.CastBarSettings.Show.Value;
    }
}
