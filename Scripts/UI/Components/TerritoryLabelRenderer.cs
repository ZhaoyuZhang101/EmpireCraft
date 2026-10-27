using System;
using System.Collections.Generic;
using EmpireCraft;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Layer;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public enum TerritoryLabelOrientation
{
    Auto,
    Horizontal,
    Vertical
}

public sealed class TerritoryLabelStyle
{
    public Color text_color = new Color(1f, 1f, 1f, 0.8f);
    public Color outline_color = new Color(0.08f, 0.07f, 0.05f, 0.8f);
    public TerritoryLabelOrientation orientation = TerritoryLabelOrientation.Auto;
    public float size_multiplier = 0.9f;
    public float territory_padding = 0.82f;
    public int min_font_size = 3;
    public int max_font_size = 512;
    public FontStyle font_style = FontStyle.Normal;
    public bool rich_text;
    public bool use_gold_gradient;
    // Only the Empire label may wrap long Latin names; ordinary nameplates stay single-line.
    public bool wrap_english_name;
    public bool bridge_internal_gaps;
    // Maximum bend for a baseline fitted from the territory's actual center line.
    public float arc_curvature;
    public Color gold_top = new Color32(255, 244, 194, 255);
    public Color gold_bottom = new Color32(184, 156, 68, 255);
}

// Renders one rotated line inside the territory, independently of WorldBox nameplates.
public static class TerritoryLabelRenderer
{
    private const float GeometryRefreshInterval = 0.75f;
    private const float LatinOutlineDistance = 8f;
    private const float CollisionSeparationPixels = 2f;
    private static readonly Dictionary<string, RuntimeLabel> _labels = new Dictionary<string, RuntimeLabel>();
    private static readonly List<RuntimeLabel> _render_order = new List<RuntimeLabel>();
    private static readonly List<ScreenLabelBounds> _occupied_label_bounds = new List<ScreenLabelBounds>();
    private static TerritoryLabelRendererHost _host;
    private static RectTransform _root;
    private static Text _text_template;
    // 已建好的本机字体(按字体名)；字体设置改变时清空(见 TerritoryFontSettings)
    private static readonly Dictionary<string, Font> _os_fonts = new(StringComparer.OrdinalIgnoreCase);
    private static Font _latin_font;
    private static bool _latin_font_resolved;
    private static Sprite _culture_lock_sprite;
    private static Texture2D _culture_lock_texture;
    private static int _submission_frame = -1;
    private static MetaType _submission_mode = MetaType.None;

    private static readonly string[] LatinFontNames =
    {
        "Georgia",
        "Arial Black",
        "Trebuchet MS",
        "Segoe UI"
    };

    // 铭牌最后的兜底不直接用游戏的铭牌字体：那是原版城名、界面共用的动态字体，按 128 号字往里拼上百个国名用字，
    // 图集(最大 4096)装不下，Unity 每帧报 "Failed to update dynamic font (Roboto-Bold) texture"，原版的字也跟着
    // 变空白、掉帧(玩家日志 2026-10-22)。改用模组自己建的一份本机字体，图集单独一张，装满了也只影响领土铭牌
    private static readonly string[] LabelFallbackFontNames =
    {
        "Microsoft YaHei",
        "Microsoft YaHei UI",
        "SimHei",
        "PingFang SC",
        "Hiragino Sans GB",
        "Noto Sans CJK SC",
        "Source Han Sans SC",
        "WenQuanYi Micro Hei",
        "Arial Unicode MS",
        "Arial"
    };
    private static Font _label_fallback_font;

    private static Font LabelFallbackFont(Font gameFont)
    {
        if (_label_fallback_font != null) return _label_fallback_font;
        try
        {
            var names = new List<string>();
            if (gameFont?.fontNames != null)
                foreach (string name in gameFont.fontNames)
                    if (!string.IsNullOrEmpty(name) && !names.Contains(name)) names.Add(name);
            foreach (string name in LabelFallbackFontNames)
                if (!names.Contains(name)) names.Add(name);
            _label_fallback_font = Font.CreateDynamicFontFromOSFont(names.ToArray(), 64);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"EmpireCraft could not create territory fallback font: {exception.Message}");
        }
        return _label_fallback_font != null ? _label_fallback_font : gameFont;
    }

    public static readonly TerritoryLabelStyle EmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.8f),
        outline_color = new Color32(54, 46, 20, 204),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    public static readonly TerritoryLabelStyle FadedEmpireStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.224f),
        outline_color = new Color32(54, 46, 20, 49),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    // 天下图层(用户 2026-10-19 嫌与帝国法理分不开，看对比图后定"紫金")：排版同帝国标签，上淡金下紫的渐变、深紫描边
    public static readonly TerritoryLabelStyle TianxiaStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.85f),
        outline_color = new Color32(36, 14, 58, 220),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        gold_top = new Color32(255, 240, 196, 255),
        gold_bottom = new Color32(138, 82, 200, 255),
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    // 未立核心、按文化连片的天下：同一套紫，淡一些、偏灰
    public static readonly TerritoryLabelStyle UncoredTianxiaStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.6f),
        outline_color = new Color32(30, 22, 40, 170),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        use_gold_gradient = true,
        gold_top = new Color32(236, 228, 210, 255),
        gold_bottom = new Color32(140, 124, 162, 255),
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    // 同盟标签(帝国视图的补充)：蓝色文字，排版同帝国标签
    public static readonly TerritoryLabelStyle AllianceStyle = new TerritoryLabelStyle
    {
        text_color = new Color32(110, 180, 255, 214),
        outline_color = new Color32(14, 30, 64, 204),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    public static readonly TerritoryLabelStyle FadedAllianceStyle = new TerritoryLabelStyle
    {
        text_color = new Color32(110, 180, 255, 57),
        outline_color = new Color32(14, 30, 64, 49),
        size_multiplier = 0.90f,
        territory_padding = 0.95f,
        min_font_size = 6,
        font_style = FontStyle.Normal,
        wrap_english_name = true,
        bridge_internal_gaps = true,
        arc_curvature = 0.1f
    };

    public static readonly TerritoryLabelStyle KingdomStyle = new TerritoryLabelStyle
    {
        text_color = new Color(1f, 1f, 1f, 0.9f),
        size_multiplier = 0.95f,
        min_font_size = 3,
        font_style = FontStyle.Normal,
        arc_curvature = 0.08f
    };

    public static readonly TerritoryLabelStyle RebellionKingdomStyle = new TerritoryLabelStyle
    {
        text_color = new Color32(235, 65, 57, 204),
        outline_color = new Color32(74, 15, 12, 204),
        size_multiplier = 0.9f,
        min_font_size = 3,
        font_style = FontStyle.Normal,
        arc_curvature = 0.08f
    };

    public static readonly TerritoryLabelStyle FeudalVassalStyle = new TerritoryLabelStyle
    {
        text_color = KingdomStyle.text_color,
        size_multiplier = KingdomStyle.size_multiplier,
        min_font_size = KingdomStyle.min_font_size,
        arc_curvature = KingdomStyle.arc_curvature,
        rich_text = true
    };

    public static readonly TerritoryLabelStyle FeudalVassalRebellionStyle = new TerritoryLabelStyle
    {
        text_color = RebellionKingdomStyle.text_color,
        outline_color = RebellionKingdomStyle.outline_color,
        size_multiplier = RebellionKingdomStyle.size_multiplier,
        min_font_size = RebellionKingdomStyle.min_font_size,
        arc_curvature = RebellionKingdomStyle.arc_curvature,
        rich_text = true
    };

    public static void RenderLawLayer(int zoneOptionState)
    {
        BeginFrame(MetaTypeExtension.KingdomTitle);
        City hoveredCity = World.world?.getMouseTilePosCachedFrame()?.zone_city;
        // A city cache can be stale after an old-save load or a title reassignment.
        // The title remains the authoritative source for an imperial administrative area.
        EmpireCore hoveredEmpireCore = hoveredCity?.GetEmpireCore() ??
                                        GetClaimingEmpireCore(hoveredCity?.GetTitle());
        long hoveredEmpireId = hoveredEmpireCore != null && hoveredEmpireCore.id > 0 ? hoveredEmpireCore.id : 0;

        if (zoneOptionState == 0)
        {
            foreach (EmpireCore core in EmpireCoreManager.EmpireCores.Values)
            {
                if (core == null || core.id <= 0) continue;
                bool expanded = hoveredEmpireId == core.id;
                SubmitEmpireCore($"law-empire:{core.id}", EmpireCoreManager.GetStatusDisplayName(core), core,
                    expanded ? FadedEmpireStyle : EmpireStyle);
            }
        }

        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            if (title == null || title.isRekt() || title.data == null) continue;

            EmpireCore claimingCore = GetClaimingEmpireCore(title);
            bool claimedByEmpire = claimingCore != null;
            bool expandedByHover = zoneOptionState == 0 && claimedByEmpire && claimingCore.id == hoveredEmpireId;

            // In the empire overview, imperial subjects stay collapsed until the mouse enters that empire.
            if (zoneOptionState == 0 && claimedByEmpire && !expandedByHover) continue;

            IEnumerable<City> cities = title.getCities();
            bool fullyOpaque = expandedByHover || hoveredCity?.GetTitle() == title;

            // Expanded imperial subjects must remain visible even if their bounds cross the faded empire label
            // (or another expanded subject label). Other labels keep the normal anti-overlap behaviour.
            bool showCultureLock = title.data.culture_locked &&
                                   !CultureService.IsFixedDeJureCultureEnabled();
            SubmitCities($"law-kingdom:{title.id}", title.data.name, cities, KingdomStyle, fullyOpaque,
                expandedByHover, showCultureLock);
        }
        EndFrame();
    }

    public static void BeginFrame(MetaType mode)
    {
        if (!EnsureHost()) return;
        _submission_frame = Time.frameCount;
        _submission_mode = mode;
    }

    // icon：可选，画在名字前面的小图标(如理念图层的理念徽章)，随文字一起旋转、缩放
    // sealFont：这个铭牌用内置篆书(华夏国号，见 TerritoryFontSettings.SealHuaxiaNames)
    public static void SubmitCities(string id, string text, IEnumerable<City> cities, TerritoryLabelStyle style,
        bool fullyOpaque = false, bool ignoreOverlap = false, bool showCultureLock = false, Sprite icon = null,
        bool sealFont = false)
    {
        if (string.IsNullOrWhiteSpace(id) || cities == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        RuntimeLabel label = GetOrCreateLabel(id);
        label.seal_font = sealFont;
        label.UpdateFromCities(text, cities, style ?? KingdomStyle, fullyOpaque, ignoreOverlap, showCultureLock, icon);
    }

    public static void SubmitEmpireCore(string id, string text, EmpireCore core, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || core == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromEmpireCore(text, core, style ?? EmpireStyle, fullyOpaque);
    }

    // Future country layers can use zones to get the same exact containment as the law layer.
    public static void SubmitZones(string id, string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || zones == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromZones(text, zones, style ?? KingdomStyle, fullyOpaque);
    }

    // Point input is for non-zone overlays and uses an oriented point-cloud fit.
    public static void SubmitPoints(string id, string text, IEnumerable<Vector3> points, TerritoryLabelStyle style,
        bool fullyOpaque = false)
    {
        if (string.IsNullOrWhiteSpace(id) || points == null || !EnsureHost()) return;
        MarkSubmissionFrame();
        GetOrCreateLabel(id).UpdateFromPoints(text, points, style ?? KingdomStyle, fullyOpaque);
    }

    public static void EndFrame()
    {
        if (_submission_frame != Time.frameCount) return;
        foreach (RuntimeLabel label in _labels.Values)
            if (label.last_seen_frame != _submission_frame) label.Hide();
    }

    private static bool ContainsLatin(string text, bool richText = false)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (richText && c == '<')
            {
                int tagEnd = text.IndexOf('>', i + 1);
                if (tagEnd >= 0) { i = tagEnd; continue; }
            }
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
        }
        return false;
    }

    private static Font ResolveInstalledFont(string[] candidates, Font fallback, ref Font cachedFont, ref bool resolved)
    {
        if (resolved) return cachedFont ?? fallback;
        resolved = true;

        try
        {
            string[] installedFonts = Font.GetOSInstalledFontNames();
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                string candidate = candidates[candidateIndex];
                for (int installedIndex = 0; installedIndex < installedFonts.Length; installedIndex++)
                {
                    if (!string.Equals(installedFonts[installedIndex], candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    cachedFont = Font.CreateDynamicFontFromOSFont(installedFonts[installedIndex], 64);
                    if (cachedFont != null) return cachedFont;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"EmpireCraft could not load a territory label font: {exception.Message}");
        }

        return fallback;
    }

    // 字体按铭牌字体设置(TerritoryFontSettings)：游戏字体 / 自动挑本机的传统书体 / 指定字体。
    // 自动模式下含英文字母的名字用西文衬线字体。
    // sealFont(华夏国号)：依次试内置篆书(小篆 → 中山王篆)。
    // 隶书兜底：选中的字体(或篆书)缺字、加载出错、取不到字形(会显示成空白)时一律改用本机隶书，本机没有隶书才用游戏字体。
    // display：实际要显示的文字(繁体篆书会把简体转成繁体)
    // 每帧每个铭牌都要问一次：按(文字, 富文本, 篆书)缓存结果，字体设置改变时清空(见 ResetFonts)
    private static readonly Dictionary<(string, bool, bool, Font), (Font font, string display)> _resolve_cache = new();

    private static Font ResolveTerritoryFont(Font fallback, string text, bool richText, bool sealFont, out string display)
    {
        var key = (text ?? "", richText, sealFont, fallback);
        if (_resolve_cache.TryGetValue(key, out var cached))
        {
            display = cached.display;
            return cached.font;
        }
        if (_resolve_cache.Count > 4096) _resolve_cache.Clear();
        Font font = ResolveTerritoryFontUncached(fallback, text, richText, sealFont, out display);
        _resolve_cache[key] = (font, display);
        return font;
    }

    // 本机字体(动态字体)的字形图集最大 4096×4096，按 128 号字只装得下九百来个字；大地图上各城、各国的名字用字太多就装不下，
    // Unity 每拼一次字报一次错(玩家日志 2026-10-16：隶书刷了上百条)，装不下的字显示成空白。认得这些字却拼不进图集，
    // 就记下这个字体两分钟内先不用，这期间新铭牌改用兜底字体，已经画好的铭牌照旧
    private const float ExhaustedSeconds = 120f;
    private static readonly Dictionary<Font, float> _exhausted_fonts = new();

    // 降档拼字(玩家截图 2026-10-27：大地图上 STLiti 报"all the needed characters do not fit onto a single texture"，
    // 连报二十几次)：本机字体按参考字号 128 拼字，4096 的图集只装得下九百来个字(主行 128、小字行 70 两种字号还各占一份)，
    // 兜底的隶书也装满时就没有退路，Unity 每拼一次报一次错。现在图集装不下时，这个字体的拼字字号逐档降(RasterSteps)，
    // 用它的铭牌全部按新字号重排、按比例放大回原来的显示大小——同样的字只占几分之一的图集；降到最低一档还装不下才改用兜底字体。
    // 本局不再升回(换字体设置时清空，见 ResetFonts)
    private static readonly int[] RasterSteps = { 128, 88, 64, 48 };
    private static readonly Dictionary<Font, int> _raster_step = new();

    private static int RasterSize(Font font) =>
        font != null && font.dynamic && _raster_step.TryGetValue(font, out int step)
            ? RasterSteps[step]
            : TerritoryLabelProjection.ReferenceFontSize;

    // 降一档；已是最低档返回 false
    private static bool StepDown(Font font)
    {
        if (font == null || !font.dynamic) return false;
        _raster_step.TryGetValue(font, out int step);
        if (step >= RasterSteps.Length - 1) return false;
        _raster_step[font] = step + 1;
        Debug.Log($"[EmpireCraft][铭牌字体] {font.name} 图集装不下，拼字字号降到 {RasterSteps[step + 1]}");
        return true;
    }

    // 这些字按 size 都拼得进图集
    private static bool Fits(Font font, string glyphs, int size, FontStyle style)
    {
        if (font == null || !font.dynamic || string.IsNullOrEmpty(glyphs)) return true;
        foreach (char character in glyphs)
        {
            if (char.IsWhiteSpace(character) || !font.HasCharacter(character)) continue;
            if (!font.GetCharacterInfo(character, out CharacterInfo info, size, style) || info.glyphWidth <= 0 || info.glyphHeight <= 0)
                return false;
        }
        return true;
    }

    // 富文本里的 <size=N> 按拼字字号同比缩
    private static string ScaleSizeTags(string text, float factor) =>
        factor == 1f || string.IsNullOrEmpty(text) || text.IndexOf("<size=", StringComparison.Ordinal) < 0
            ? text
            : global::System.Text.RegularExpressions.Regex.Replace(text, "<size=(\\d+)>",
                match => $"<size={Mathf.Max(1, Mathf.RoundToInt(int.Parse(match.Groups[1].Value) * factor))}>");

    private static bool Exhausted(Font font) =>
        font != null && _exhausted_fonts.TryGetValue(font, out float at) && Time.unscaledTime - at < ExhaustedSeconds &&
        Time.unscaledTime >= at;

    private static Font ResolveTerritoryFontUncached(Font fallback, string text, bool richText, bool sealFont,
        out string display)
    {
        display = text;
        if (sealFont && TerritoryFontSettings.SealHuaxiaNames)
        {
            foreach (BundledFont bundled in BundledTerritoryFonts.SealChain)
                if (bundled.TryAdapt(text, richText, out string adapted) && CanRender(bundled.Font, adapted, richText))
                {
                    display = adapted;
                    return bundled.Font;
                }
            return ClericalFallback(text, richText, out display) ?? fallback;
        }
        string choice = TerritoryFontSettings.Choice;
        if (choice == TerritoryFontSettings.Game) return fallback;
        Font selected;
        if (choice == TerritoryFontSettings.Auto && ContainsLatin(text, richText))
            selected = ResolveInstalledFont(LatinFontNames, fallback, ref _latin_font, ref _latin_font_resolved);
        else
        {
            string fontName = TerritoryFontSettings.ResolveFontName();
            selected = fontName == null ? fallback : GetOsFont(fontName) ?? fallback;
        }
        if (selected == null || selected == fallback) return fallback;
        if (Exhausted(selected)) return ClericalFallback(text, richText, out display, selected) ?? fallback;
        // 展示字体字库较小；名字缺字时回退，不能让整张铭牌变成空白
        BundledFont chosen = BundledTerritoryFonts.Of(selected);
        if (chosen != null)
        {
            if (chosen.TryAdapt(text, richText, out string adapted) && CanRender(selected, adapted, richText))
            {
                display = adapted;
                return selected;
            }
        }
        else if (HasAll(selected, text, richText) && CanRender(selected, text, richText)) return selected;
        return ClericalFallback(text, richText, out display, selected) ?? fallback;
    }

    private static bool HasAll(Font font, string text, bool richText)
    {
        bool insideTag = false;
        foreach (char character in text ?? "")
        {
            if (richText && character == '<') { insideTag = true; continue; }
            if (insideTag) { if (character == '>') insideTag = false; continue; }
            if (char.IsWhiteSpace(character)) continue;
            if (!font.HasCharacter(character)) return false;
        }
        return true;
    }

    // 兜底字体(默认本机隶书，可在字体窗口里另选，见 TerritoryFontSettings.FallbackCandidates)：第一个字全、画得出来的
    public static Font ClericalFallback(string text, bool richText, out string display, Font except = null)
    {
        display = text;
        foreach (string name in TerritoryFontSettings.FallbackCandidates())
        {
            Font font = GetOsFont(name);
            if (font == null || font == except || Exhausted(font)) continue;
            // 兜底选了内置篆书：按它的字表查字(繁体篆书先转繁体)
            BundledFont bundled = BundledTerritoryFonts.Find(name);
            string shown = text;
            bool has = bundled != null ? bundled.TryAdapt(text, richText, out shown) : HasAll(font, text, richText);
            if (has && CanRender(font, shown, richText))
            {
                display = shown;
                return font;
            }
        }
        return null;
    }

    // 字体真的画得出这些字吗：先让字体把字形画进图集，再看每个字都有大小、图集贴图存在。
    // 从文件读的字体有时认得字却画不出来(显示成空白)，这里查出来就改用隶书
    public static bool CanRender(Font font, string text, bool richText)
    {
        if (font == null) return false;
        try
        {
            var builder = new global::System.Text.StringBuilder();
            bool insideTag = false;
            foreach (char character in text ?? "")
            {
                if (richText && character == '<') { insideTag = true; continue; }
                if (insideTag) { if (character == '>') insideTag = false; continue; }
                if (!char.IsWhiteSpace(character)) builder.Append(character);
            }
            string glyphs = builder.ToString();
            if (glyphs.Length == 0) return true;
            // 字形包：把字拼进贴图，拼得进就画得出
            BakedGlyphFont baked = BundledTerritoryFonts.BakedOf(font);
            if (baked != null) return baked.Ensure(glyphs);
            if (Exhausted(font)) return false;
            while (true)
            {
                int size = RasterSize(font);
                font.RequestCharactersInTexture(glyphs, size, FontStyle.Normal);
                bool missing = false;
                foreach (char character in glyphs)
                    if (!font.GetCharacterInfo(character, out CharacterInfo info, size, FontStyle.Normal) ||
                        info.glyphWidth <= 0 || info.glyphHeight <= 0)
                    {
                        // 字体里有这个字却拼不出来：图集满了，先降档再试，降到底才放弃这个字体
                        if (!font.dynamic || !font.HasCharacter(character)) return false;
                        missing = true;
                        break;
                    }
                if (!missing) break;
                if (StepDown(font)) continue;
                _exhausted_fonts[font] = Time.unscaledTime;
                _resolve_cache.Clear();
                return false;
            }
            return font.material != null && font.material.mainTexture != null;
        }
        catch
        {
            return false;
        }
    }

    public static Font GetOsFont(string fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return null;
        if (_os_fonts.TryGetValue(fontName, out Font cached)) return cached;
        Font font = null;
        try
        {
            BundledFont bundled = BundledTerritoryFonts.Find(fontName);
            font = bundled != null ? bundled.Load() : Font.CreateDynamicFontFromOSFont(fontName, 64);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"EmpireCraft could not load font {fontName}: {exception.Message}");
        }
        _os_fonts[fontName] = font;
        return font;
    }

    // 字体设置改变：保留字体对象，作废铭牌的材质与文字尺寸缓存。
    public static void ResetFonts()
    {
        // 复用已经加载的 Font，反复选择不再创建新图集或让旧材质引用失效。
        var missing = new List<string>();
        foreach (KeyValuePair<string, Font> pair in _os_fonts)
            if (pair.Value == null) missing.Add(pair.Key);
        foreach (string name in missing) _os_fonts.Remove(name);
        if (_latin_font == null) _latin_font_resolved = false;
        _resolve_cache.Clear();
        // 换了字体设置：拼字字号回到参考字号，装不下再降(见 RasterSize)
        _raster_step.Clear();
        foreach (RuntimeLabel label in _labels.Values) label.InvalidateFont();
    }

    public static void HideAll()
    {
        foreach (RuntimeLabel label in _labels.Values) label.Hide();
        _submission_frame = -1;
        _submission_mode = MetaType.None;
    }

    public static void ResetWorldState()
    {
        HideAll();
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        _host = null;
        _root = null;
        _text_template = null;
        _labels.Clear();
        _render_order.Clear();
        _occupied_label_bounds.Clear();
    }

    internal static void HostLateUpdate(TerritoryLabelRendererHost host)
    {
        if (host != _host) return;
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        NameplateManager manager = canvas == null ? null : canvas.GetComponent<NameplateManager>();
        if (_submission_frame < 0 || !Zones.showMapNames() || manager == null ||
            manager.getCurrentMode() != _submission_mode)
        {
            HideAll();
            return;
        }
        _render_order.Clear();
        foreach (RuntimeLabel label in _labels.Values)
            if (label.last_seen_frame == _submission_frame) _render_order.Add(label);
        _render_order.Sort((left, right) => right.RenderPriority.CompareTo(left.RenderPriority));
        _occupied_label_bounds.Clear();
        foreach (RuntimeLabel label in _render_order) label.RenderSubmitted(_submission_frame);
    }

    internal static void HostDestroyed(TerritoryLabelRendererHost host)
    {
        if (host != _host) return;
        _host = null;
        _root = null;
        _text_template = null;
        _labels.Clear();
        _render_order.Clear();
        _occupied_label_bounds.Clear();
        _submission_frame = -1;
        _submission_mode = MetaType.None;
    }

    private static void MarkSubmissionFrame()
    {
        if (_submission_frame != Time.frameCount) _submission_frame = Time.frameCount;
    }

    private static EmpireCore GetClaimingEmpireCore(KingdomTitle title)
    {
        if (title == null) return null;

        EmpireCore capitalCore = title.title_capital?.GetEmpireCore();
        if (capitalCore != null && EmpireCoreManager.ContainsTitle(capitalCore, title)) return capitalCore;

        foreach (City city in title.getCities())
        {
            EmpireCore core = city?.GetEmpireCore();
            if (core != null && EmpireCoreManager.ContainsTitle(core, title)) return core;
        }
        return null;
    }

    private static bool IsClaimedByEmpireCore(KingdomTitle title) => GetClaimingEmpireCore(title) != null;

    private static RuntimeLabel GetOrCreateLabel(string id)
    {
        if (_labels.TryGetValue(id, out RuntimeLabel label)) return label;
        label = new RuntimeLabel(id);
        _labels.Add(id, label);
        return label;
    }

    private static bool EnsureHost()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        if (canvas == null) return false;
        if (_root != null && _root.parent == canvas.transform) return true;
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);

        _labels.Clear();
        _text_template = null;
        GameObject rootObject = new GameObject("EmpireCraftTerritoryLabels", typeof(RectTransform), typeof(Canvas), typeof(TerritoryLabelRendererHost));
        _root = rootObject.GetComponent<RectTransform>();
        _root.SetParent(canvas.transform, false);
        // Disable pixel snapping for this overlay only; vanilla nameplates retain their settings.
        Canvas overlayCanvas = rootObject.GetComponent<Canvas>();
        overlayCanvas.overridePixelPerfect = true;
        overlayCanvas.pixelPerfect = false;
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;
        _root.SetAsLastSibling();
        _host = rootObject.GetComponent<TerritoryLabelRendererHost>();
        return true;
    }

    private static Text GetTextTemplate()
    {
        if (_text_template != null) return _text_template;
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        NameplateManager manager = canvas == null ? null : canvas.GetComponent<NameplateManager>();
        _text_template = manager?.prefab?._text_name;
        return _text_template;
    }

    private static Sprite GetCultureLockSprite()
    {
        if (_culture_lock_sprite != null) return _culture_lock_sprite;

        const int width = 12;
        const int height = 14;
        var pixels = new Color32[width * height];
        Color32 outline = new Color32(67, 45, 12, 255);
        Color32 gold = new Color32(235, 181, 48, 255);
        Color32 highlight = new Color32(255, 226, 112, 255);

        void Set(int x, int y, Color32 color)
        {
            if (x >= 0 && x < width && y >= 0 && y < height)
                pixels[y * width + x] = color;
        }

        void Fill(int minX, int maxX, int minY, int maxY, Color32 color)
        {
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    Set(x, y, color);
        }

        Fill(1, 10, 1, 8, outline);
        Fill(2, 9, 2, 7, gold);
        Fill(2, 9, 7, 7, highlight);

        Fill(4, 7, 13, 13, outline);
        Fill(3, 8, 12, 12, outline);
        Fill(4, 7, 12, 12, highlight);
        for (int y = 9; y <= 11; y++)
        {
            Set(2, y, outline);
            Set(3, y, gold);
            Set(8, y, gold);
            Set(9, y, outline);
        }

        Fill(5, 6, 4, 6, outline);
        Set(5, 3, outline);
        Set(6, 3, outline);

        _culture_lock_texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "EmpireCraftCultureLock",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        _culture_lock_texture.SetPixels32(pixels);
        _culture_lock_texture.Apply(false, true);
        _culture_lock_sprite = Sprite.Create(
            _culture_lock_texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            height
        );
        _culture_lock_sprite.name = "EmpireCraftCultureLock";
        return _culture_lock_sprite;
    }

    private sealed class RuntimeLabel
    {
        private const int MaxCandidateCenters = 48;
        private const int SearchIterations = 7;
        private const float ZoneSize = 8f;
        private const float SampleSpacing = 3.25f;

        private readonly string _id;
        public bool seal_font;
        private readonly List<Vector3> _points = new List<Vector3>(64);
        private readonly List<TileZone> _zones = new List<TileZone>(64);
        private readonly List<TileZone> _walk_component = new List<TileZone>(64);
        private readonly List<TileZone> _largest_component = new List<TileZone>(64);
        private readonly HashSet<int> _zone_ids = new HashSet<int>();
        private readonly HashSet<int> _component_zone_ids = new HashSet<int>();
        private readonly HashSet<int> _visited_ids = new HashSet<int>();
        private readonly Queue<TileZone> _zone_queue = new Queue<TileZone>();
        private readonly Queue<Vector2Int> _gap_queue = new Queue<Vector2Int>();
        private readonly HashSet<int> _gap_tiles = new HashSet<int>();
        private readonly Dictionary<int, bool> _small_gap_cache = new Dictionary<int, bool>();
        private TerritoryPlacement _placement;
        private Text _text;
        // 字形包字体的第二行小字(宗主、周诸侯金字)：字形包字体不认 <size>、量不出行高，两行会叠在一起，拆出来另画在名字下面
        private Text _sub_text;
        private TerritoryLabelOutline _sub_outline;
        private const float SubLineScale = 70f / 128f;
        private Image _culture_lock_badge;
        private Image _leading_icon;
        private TerritoryLabelGoldGradient _gold_gradient;
        private TerritoryLabelOutline _outline;
        private int _source_signature;
        private string _placement_text;
        private TerritoryLabelOrientation _placement_orientation;
        private float _placement_padding;
        private bool _placement_bridges_gaps;
        private float _next_geometry_refresh;
        private bool _has_geometry_result;
        private string _render_text;
        private TerritoryLabelStyle _render_style;
        private bool _render_fully_opaque;
        private bool _render_ignore_overlap;
        private bool _render_show_culture_lock;
        private Sprite _render_icon;
        private bool _render_requested;
        private string _metrics_text;
        private FontStyle _metrics_style;
        private Font _metrics_font;
        // 量字时的拼字字号与放大倍数(参考字号 / 拼字字号，见 RasterSize)
        private int _metrics_raster;
        private float _raster_factor = 1f;
        private Font _fallback_font;
        private float _reference_width;
        private float _reference_height;
        public int last_seen_frame;

        public float RenderPriority
        {
            get
            {
                float area = _placement.valid ? _placement.half_width * _placement.half_height : 0f;
                return (_render_style?.wrap_english_name == true ? 1000000f : 0f) + area;
            }
        }

        public RuntimeLabel(string id) => _id = id;

        public void UpdateFromCities(string text, IEnumerable<City> cities, TerritoryLabelStyle style,
            bool fullyOpaque, bool ignoreOverlap = false, bool showCultureLock = false, Sprite icon = null)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectCityZones(cities, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque, ignoreOverlap, showCultureLock, icon);
        }

        public void UpdateFromEmpireCore(string text, EmpireCore core, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                // 地名只铺在本来的城上(兼属的城两家都算，铺上去名字会压到别家)
                CollectCityZones(EmpireCoreManager.EnumerateOwnCities(core), _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void UpdateFromZones(string text, IEnumerable<TileZone> zones, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                CollectZones(zones, _zones, _zone_ids);
                RefreshZonePlacement(text, style, inputsChanged);
            }
            QueueRender(text, style, fullyOpaque);
        }

        public void UpdateFromPoints(string text, IEnumerable<Vector3> points, TerritoryLabelStyle style,
            bool fullyOpaque)
        {
            text = FormatDisplayText(text, style);
            last_seen_frame = Time.frameCount;
            bool inputsChanged = PlacementInputsChanged(text, style);
            if (!_has_geometry_result || inputsChanged || Time.unscaledTime >= _next_geometry_refresh)
            {
                _points.Clear();
                int signature = 17;
                unchecked
                {
                    foreach (Vector3 point in points)
                    {
                        _points.Add(point);
                        signature = signature * 31 + Mathf.RoundToInt(point.x * 4f);
                        signature = signature * 31 + Mathf.RoundToInt(point.y * 4f);
                    }
                }
                if (!_has_geometry_result || inputsChanged || signature != _source_signature)
                {
                    _placement = BuildPointPlacement(_points, text, style);
                    _source_signature = signature;
                    RememberPlacementInputs(text, style);
                    _has_geometry_result = true;
                }
                _next_geometry_refresh = Time.unscaledTime + GeometryRefreshInterval;
            }
            QueueRender(text, style, fullyOpaque);
        }

        private static string StripTags(string text) =>
            global::System.Text.RegularExpressions.Regex.Replace(text ?? "", "<[^>]*>", "");

        // 第二行小字：名字下面另起一个小一号的字，随铭牌一起旋转、缩放
        private void UpdateSubLine(string subLine, BakedGlyphFont baked)
        {
            if (subLine == null || baked == null)
            {
                _text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                if (_sub_text != null && _sub_text.gameObject.activeSelf) _sub_text.gameObject.SetActive(false);
                return;
            }
            if (_sub_text == null)
            {
                var child = new GameObject("SubLine", typeof(RectTransform), typeof(Text));
                child.transform.SetParent(_text.transform, false);
                _sub_text = child.GetComponent<Text>();
                _sub_text.alignment = TextAnchor.MiddleCenter;
                _sub_text.horizontalOverflow = HorizontalWrapMode.Overflow;
                _sub_text.verticalOverflow = VerticalWrapMode.Overflow;
                _sub_text.resizeTextForBestFit = false;
                _sub_text.supportRichText = true;
                _sub_text.raycastTarget = false;
                _sub_outline = child.AddComponent<TerritoryLabelOutline>();
                RectTransform childRect = _sub_text.rectTransform;
                childRect.anchorMin = childRect.anchorMax = childRect.pivot = new Vector2(0.5f, 0.5f);
            }
            _sub_text.font = _text.font;
            _sub_text.fontSize = 0;
            _sub_text.text = subLine;
            RectTransform rect = _sub_text.rectTransform;
            rect.sizeDelta = new Vector2(_reference_width / SubLineScale + 2f, baked.LineHeight + 2f);
            rect.localScale = new Vector3(SubLineScale, SubLineScale, 1f);
            // 名字一行在上、小字一行在下，两行合起来居中(字形包的字以基线为中线，名字行在铭牌框的正中：
            // 把框的支点往下挪半个小字行，名字行就上移半个小字行；小字行相对支点再往下半个名字行)
            float total = baked.LineHeight * (1f + SubLineScale);
            rect.localPosition = new Vector3(0f, -total * 0.5f + baked.LineHeight * SubLineScale * 0.5f, 0f);
            _text.rectTransform.pivot = new Vector2(0.5f, 0.5f - baked.LineHeight * SubLineScale * 0.5f / Mathf.Max(1f, total));
            if (!_sub_text.gameObject.activeSelf) _sub_text.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _render_requested = false;
            if (_text != null && _text.gameObject.activeSelf) _text.gameObject.SetActive(false);
            if (_culture_lock_badge != null && _culture_lock_badge.gameObject.activeSelf)
                _culture_lock_badge.gameObject.SetActive(false);
            if (_leading_icon != null && _leading_icon.gameObject.activeSelf)
                _leading_icon.gameObject.SetActive(false);
        }

        private void QueueRender(string text, TerritoryLabelStyle style, bool fullyOpaque, bool ignoreOverlap = false,
            bool showCultureLock = false, Sprite icon = null)
        {
            _render_icon = icon;
            _render_text = text;
            _render_style = style;
            _render_fully_opaque = fullyOpaque;
            _render_ignore_overlap = ignoreOverlap;
            _render_show_culture_lock = showCultureLock;
            _render_requested = true;
        }

        private static string FormatDisplayText(string text, TerritoryLabelStyle style)
        {
            text ??= string.Empty;
            return style?.wrap_english_name == true ? OverallHelperFunc.WrapEnglishDisplayName(text) : text;
        }

        public void RenderSubmitted(int activeSubmissionFrame)
        {
            if (last_seen_frame != activeSubmissionFrame) { Hide(); return; }
            if (_render_requested)
                Render(_render_text, _render_style, _render_fully_opaque, _render_ignore_overlap,
                    _render_show_culture_lock, _render_icon);
        }

        private bool PlacementInputsChanged(string text, TerritoryLabelStyle style)
        {
            return !string.Equals(_placement_text, text, StringComparison.Ordinal) ||
                   _placement_orientation != style.orientation ||
                   !Mathf.Approximately(_placement_padding, style.territory_padding) ||
                   _placement_bridges_gaps != style.bridge_internal_gaps;
        }

        private void RefreshZonePlacement(string text, TerritoryLabelStyle style, bool inputsChanged)
        {
            int signature = CalculateZoneSignature(_zones);
            if (!_has_geometry_result || inputsChanged || signature != _source_signature)
            {
                // Never join exclaves. Only the dominant connected territory is allowed to host its label.
                FindLargestConnectedComponent(_zones, _zone_ids, _walk_component, _largest_component,
                    _visited_ids, _zone_queue);
                _component_zone_ids.Clear();
                for (int i = 0; i < _largest_component.Count; i++)
                    _component_zone_ids.Add(_largest_component[i].id);
                _small_gap_cache.Clear();
                _placement = BuildZonePlacement(_largest_component, _component_zone_ids, text, style,
                    style.bridge_internal_gaps);
                _source_signature = signature;
                RememberPlacementInputs(text, style);
                _has_geometry_result = true;
            }
            _next_geometry_refresh = Time.unscaledTime + GeometryRefreshInterval;
        }

        private void RememberPlacementInputs(string text, TerritoryLabelStyle style)
        {
            _placement_text = text;
            _placement_orientation = style.orientation;
            _placement_padding = style.territory_padding;
            _placement_bridges_gaps = style.bridge_internal_gaps;
        }

        private void Render(string value, TerritoryLabelStyle style, bool fullyOpaque, bool ignoreOverlap,
            bool showCultureLock, Sprite icon = null)
        {
            if (!_placement.valid || string.IsNullOrWhiteSpace(value) || World.world?.camera == null)
            {
                Hide();
                return;
            }

            Camera camera = World.world.camera;
            Vector3 centerScreen = camera.WorldToScreenPoint(_placement.center);
            Vector3 alongScreen = camera.WorldToScreenPoint(_placement.center + _placement.axis * _placement.half_width);
            Vector3 acrossScreen = camera.WorldToScreenPoint(_placement.center + _placement.normal * _placement.half_height);
            if (centerScreen.z < 0f)
            {
                Hide();
                return;
            }

            Vector2 alongDelta = (Vector2)(alongScreen - centerScreen);
            Vector2 acrossDelta = (Vector2)(acrossScreen - centerScreen);
            float canvasScale = GetCanvasScale();
            float availableWidth = alongDelta.magnitude * 2f / canvasScale;
            float availableHeight = acrossDelta.magnitude * 2f / canvasScale;
            float screenMargin = Mathf.Max(availableWidth, availableHeight) * canvasScale * 0.55f;
            if (centerScreen.x < -screenMargin || centerScreen.x > Screen.width + screenMargin ||
                centerScreen.y < -screenMargin || centerScreen.y > Screen.height + screenMargin)
            {
                Hide();
                return;
            }

            EnsureText();
            if (_text == null) return;

            Font desiredFont = ResolveTerritoryFont(_fallback_font ?? _text.font, value.Trim(), style.rich_text, seal_font,
                out string trimmedValue);
            if (_text.font != desiredFont)
            {
                _text.font = desiredFont;
                // Text.mainTexture 随当前字体取图集；不保留上一个字体的显式材质。
                _text.material = null;
                _text.SetAllDirty();

                _metrics_text = null;
                _metrics_font = null;
            }

            if (_gold_gradient == null)
                _gold_gradient = _text.GetComponent<TerritoryLabelGoldGradient>() ??
                                 _text.gameObject.AddComponent<TerritoryLabelGoldGradient>();
            RectTransform rect = _text.rectTransform;
            if (_text.supportRichText != style.rich_text)
            {
                _text.supportRichText = style.rich_text;
                _metrics_text = null;
            }
            bool containsLatin = ContainsLatin(trimmedValue, style.rich_text);
            FontStyle effectiveFontStyle = containsLatin ? FontStyle.Bold : style.font_style;
            if (_metrics_text != trimmedValue || _metrics_style != effectiveFontStyle || _metrics_font != _text.font ||
                _metrics_raster != RasterSize(_text.font))
            {
                // 字形包字体不是动态字体：不认字号和字形样式(设了会报警告)，字形已按参考字号排好
                BakedGlyphFont baked = BundledTerritoryFonts.BakedOf(_text.font);
                string mainLine = trimmedValue, subLine = null;
                if (baked != null)
                {
                    int newline = trimmedValue.IndexOf('\n');
                    if (newline >= 0)
                    {
                        mainLine = trimmedValue.Substring(0, newline);
                        subLine = global::System.Text.RegularExpressions.Regex.Replace(trimmedValue.Substring(newline + 1),
                            "</?size[^>]*>", "");
                    }
                }
                _text.fontStyle = baked != null ? FontStyle.Normal : effectiveFontStyle;
                int raster = baked != null ? 0 : RasterSize(_text.font);
                if (baked != null)
                {
                    _text.text = mainLine;
                    _text.fontSize = 0;
                    // 富文本标签不是字：去掉标签再拼字形(不然拼到 < 就停了，后面的字缺)
                    baked.Ensure(StripTags(mainLine + (subLine ?? "")));
                }
                else
                {
                    // 图集装不下就降档重拼(见 RasterSize)
                    string glyphs = StripTags(mainLine);
                    while (true)
                    {
                        raster = RasterSize(_text.font);
                        _text.text = ScaleSizeTags(mainLine, (float)raster / TerritoryLabelProjection.ReferenceFontSize);
                        _text.fontSize = raster;
                        _text.font?.RequestCharactersInTexture(glyphs, raster, effectiveFontStyle);
                        if (Fits(_text.font, glyphs, raster, effectiveFontStyle) || !StepDown(_text.font)) break;
                    }
                }
                _raster_factor = raster > 0 ? (float)TerritoryLabelProjection.ReferenceFontSize / raster : 1f;
                _metrics_raster = raster > 0 ? raster : RasterSize(_text.font);
                _text.cachedTextGenerator.Invalidate();
                _text.cachedTextGeneratorForLayout.Invalidate();
                _reference_width = Mathf.Max(1f, _text.preferredWidth * _raster_factor);
                _reference_height = Mathf.Max(1f, _text.preferredHeight * _raster_factor);
                // 字形包字体的行高 Unity 量不出来，按字形包的上伸加下伸算；有第二行小字再加一行小字高
                if (baked != null)
                {
                    _reference_height = Mathf.Max(_reference_height, baked.LineHeight);
                    if (subLine != null)
                    {
                        _reference_width = Mathf.Max(_reference_width, baked.Width(StripTags(subLine)) * SubLineScale);
                        _reference_height += baked.LineHeight * SubLineScale;
                    }
                }
                UpdateSubLine(subLine, baked);
                rect.sizeDelta = new Vector2(_reference_width / _raster_factor + 2f, _reference_height / _raster_factor + 2f);
                _metrics_text = trimmedValue;
                _metrics_style = effectiveFontStyle;
                _metrics_font = _text.font;
            }
            float outlineDistance = containsLatin
                ? LatinOutlineDistance
                : TerritoryLabelProjection.OutlineDistance;
            float scale = TerritoryLabelProjection.FitScale(availableWidth, availableHeight,
                _reference_width, _reference_height, style.size_multiplier, style.max_font_size,
                outlineDistance);
            float visibility = TerritoryLabelProjection.Visibility(scale, style.min_font_size);
            if (visibility <= 0f) { Hide(); return; }
            float renderedFontPixels = scale * TerritoryLabelProjection.ReferenceFontSize * canvasScale;
            bool displayCultureLock = showCultureLock && (fullyOpaque || renderedFontPixels >= 14f);
            float lockReferenceSize = Mathf.Clamp(_reference_height * 0.26f, 20f, 40f);
            // 名字前的图标：与字同高，字太小(远景)时不画
            bool displayIcon = icon != null && (fullyOpaque || renderedFontPixels >= 10f);
            float iconReferenceSize = _reference_height * 0.9f;
            float textAlpha = (fullyOpaque ? 1f : style.text_color.a) * visibility;
            if (style.use_gold_gradient)
            {
                _text.color = Color.white;
                _gold_gradient.Configure(style.gold_top, style.gold_bottom, textAlpha);
            }
            else
            {
                Color textColor = style.text_color;
                textColor.a = textAlpha;
                _text.color = textColor;
                _gold_gradient.DisableGradient();
            }
            _outline.enabled = true;
            Color outlineColor = containsLatin
                ? new Color(0f, 0f, 0f, style.outline_color.a)
                : style.outline_color;
            if (fullyOpaque && outlineColor.a > 0f) outlineColor.a = 1f;
            // 小字行的颜色写在 <color> 标签里，改 Text.color 的透明度淡不下去：字、描边按不淡的颜色画，
            // 整行透明度交给 CanvasRenderer(画完再乘)，在下面算完占屏淡出后一并设
            Color subOutlineColor = outlineColor;
            outlineColor.a *= visibility;
            _outline.effectColor = outlineColor;
            // 描边按拼字字号的本地单位；放得小时屏幕上至少 MinOutlinePixels
            float outlineLocal = outlineDistance / _raster_factor;
            float pixelsPerLocal = scale * _raster_factor * canvasScale;
            if (pixelsPerLocal > 0f)
                outlineLocal = Mathf.Max(outlineLocal, TerritoryLabelProjection.MinOutlinePixels / pixelsPerLocal);
            _outline.effectDistance = new Vector2(outlineLocal, -outlineLocal);
            bool hasSubLine = _sub_text != null && _sub_text.gameObject.activeSelf;
            if (hasSubLine)
            {
                _sub_text.color = Color.white;
                _sub_outline.effectColor = subOutlineColor;
                _sub_outline.effectDistance = _outline.effectDistance;
            }

            float rotation = Mathf.Atan2(alongDelta.y, alongDelta.x) * Mathf.Rad2Deg;
            float arcHeight = Mathf.Abs(_placement.curvature) * _reference_width * scale * canvasScale;
            float widthPixels = _reference_width * scale * canvasScale + outlineDistance * 2f;
            if (displayCultureLock)
                widthPixels += lockReferenceSize * scale * canvasScale * 1.45f;
            if (displayIcon)
                widthPixels += iconReferenceSize * scale * canvasScale * 1.25f;
            float heightPixels = _reference_height * scale * canvasScale + outlineDistance * 2f + arcHeight;
            float radians = rotation * Mathf.Deg2Rad;
            float boundWidth = Mathf.Abs(Mathf.Cos(radians)) * widthPixels +
                               Mathf.Abs(Mathf.Sin(radians)) * heightPixels;
            float boundHeight = Mathf.Abs(Mathf.Sin(radians)) * widthPixels +
                                Mathf.Abs(Mathf.Cos(radians)) * heightPixels;
            ScreenLabelBounds collisionBounds = new ScreenLabelBounds((Vector2)centerScreen,
                widthPixels * 0.5f, heightPixels * 0.5f, radians);
            float screenCoverageVisibility = TerritoryLabelProjection.ScreenCoverageVisibility(
                boundWidth / Mathf.Max(1f, Screen.width), boundHeight / Mathf.Max(1f, Screen.height));
            if (screenCoverageVisibility < 1f)
            {
                textAlpha *= screenCoverageVisibility;
                if (style.use_gold_gradient)
                    _gold_gradient.Configure(style.gold_top, style.gold_bottom, textAlpha);
                else
                {
                    Color textColor = style.text_color;
                    textColor.a = textAlpha;
                    _text.color = textColor;
                }

                outlineColor.a *= screenCoverageVisibility;
                _outline.effectColor = outlineColor;
            }
            if (hasSubLine) _sub_text.canvasRenderer.SetAlpha(visibility * screenCoverageVisibility);
            if (!ignoreOverlap && OverlapsSubmittedLabel(collisionBounds))
            {
                Hide();
                return;
            }
            _occupied_label_bounds.Add(collisionBounds);

            rect.position = centerScreen;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            rect.localScale = new Vector3(scale * _raster_factor, scale * _raster_factor, 1f);
            TerritoryLabelArc arc = _text.GetComponent<TerritoryLabelArc>() ??
                                    _text.gameObject.AddComponent<TerritoryLabelArc>();
            arc.Configure(_placement.curvature);
            UpdateCultureLockBadge(displayCultureLock, lockReferenceSize / _raster_factor,
                (fullyOpaque ? 1f : 0.78f) * visibility * screenCoverageVisibility);
            UpdateLeadingIcon(displayIcon, icon, iconReferenceSize / _raster_factor,
                (fullyOpaque ? 1f : 0.9f) * visibility * screenCoverageVisibility);
            if (ignoreOverlap) rect.SetAsLastSibling();
            if (!_text.gameObject.activeSelf) _text.gameObject.SetActive(true);
            _text.enabled = true;
        }

        private static bool OverlapsSubmittedLabel(ScreenLabelBounds bounds)
        {
            for (int i = 0; i < _occupied_label_bounds.Count; i++)
                if (bounds.Overlaps(_occupied_label_bounds[i])) return true;
            return false;
        }

        public void InvalidateFont()
        {
            _metrics_text = null;
            _metrics_font = null;
            if (_text == null) return;
            _text.material = null;
            _text.cachedTextGenerator.Invalidate();
            _text.cachedTextGeneratorForLayout.Invalidate();
            _text.SetAllDirty();
        }

        private void EnsureText()
        {
            if (_text != null || _root == null) return;
            GameObject textObject = new GameObject($"TerritoryLabel_{_id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(_root, false);
            _text = textObject.GetComponent<Text>();
            Text template = GetTextTemplate();
            Font fallbackFont = LabelFallbackFont(template?.font ?? Resources.GetBuiltinResource<Font>("Arial.ttf"));
            _fallback_font = fallbackFont;
            _text.font = fallbackFont;
            _text.fontSize = TerritoryLabelProjection.ReferenceFontSize;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.resizeTextForBestFit = false;
            _text.supportRichText = false;
            _text.raycastTarget = false;
            _text.material = null;
            RectTransform rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            _gold_gradient = textObject.AddComponent<TerritoryLabelGoldGradient>();
            _outline = textObject.AddComponent<TerritoryLabelOutline>();
            textObject.AddComponent<TerritoryLabelArc>();
        }

        private void UpdateLeadingIcon(bool visible, Sprite sprite, float referenceSize, float alpha)
        {
            if (!visible || sprite == null)
            {
                if (_leading_icon != null) _leading_icon.gameObject.SetActive(false);
                return;
            }
            if (_leading_icon == null)
            {
                GameObject iconObject = new GameObject($"TerritoryLabelIcon_{_id}", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(_text.transform, false);
                _leading_icon = iconObject.GetComponent<Image>();
                _leading_icon.preserveAspect = true;
                _leading_icon.raycastTarget = false;
                RectTransform iconRect = _leading_icon.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
            }
            _leading_icon.sprite = sprite;
            RectTransform rect = _leading_icon.rectTransform;
            rect.sizeDelta = new Vector2(referenceSize, referenceSize);
            rect.anchoredPosition = new Vector2(-(_reference_width / _raster_factor * 0.5f + referenceSize * 0.65f), 0f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            _leading_icon.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            _leading_icon.transform.SetAsLastSibling();
            if (!_leading_icon.gameObject.activeSelf) _leading_icon.gameObject.SetActive(true);
        }

        private void UpdateCultureLockBadge(bool visible, float referenceSize, float alpha)
        {
            if (!visible)
            {
                if (_culture_lock_badge != null)
                    _culture_lock_badge.gameObject.SetActive(false);
                return;
            }

            if (_culture_lock_badge == null)
            {
                GameObject badgeObject = new GameObject(
                    $"CultureLockBadge_{_id}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image)
                );
                badgeObject.transform.SetParent(_text.transform, false);
                _culture_lock_badge = badgeObject.GetComponent<Image>();
                _culture_lock_badge.sprite = GetCultureLockSprite();
                _culture_lock_badge.preserveAspect = true;
                _culture_lock_badge.raycastTarget = false;

                RectTransform badgeRect = _culture_lock_badge.rectTransform;
                badgeRect.anchorMin = new Vector2(0.5f, 0.5f);
                badgeRect.anchorMax = new Vector2(0.5f, 0.5f);
                badgeRect.pivot = new Vector2(0.5f, 0.5f);
            }

            RectTransform rect = _culture_lock_badge.rectTransform;
            rect.sizeDelta = new Vector2(referenceSize, referenceSize);
            rect.anchoredPosition = new Vector2(
                _reference_width / _raster_factor * 0.5f + referenceSize * 0.8f,
                _reference_height / _raster_factor * 0.02f
            );
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            _culture_lock_badge.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            _culture_lock_badge.transform.SetAsLastSibling();
            if (!_culture_lock_badge.gameObject.activeSelf)
                _culture_lock_badge.gameObject.SetActive(true);
        }

        private TerritoryPlacement BuildZonePlacement(List<TileZone> component, HashSet<int> zoneIds,
            string text, TerritoryLabelStyle style, bool allowSmallEmptyGaps)
        {
            if (component == null || component.Count == 0) return default;
            float textAspect = EstimateTextAspect(text, style.rich_text);
            Vector3 centroid = CalculateZoneCentroid(component);
            float principalAngle = CalculatePrincipalAngle(component, centroid);
            List<float> angles = BuildCandidateAngles(principalAngle, style.orientation);
            List<Vector3> centers = BuildCandidateCenters(component, centroid);
            float bestHalfHeight = 0f;
            Vector3 bestCenter = default;
            Vector3 bestAxis = Vector3.right;

            for (int angleIndex = 0; angleIndex < angles.Count; angleIndex++)
            {
                float radians = angles[angleIndex] * Mathf.Deg2Rad;
                Vector3 axis = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians));
                Vector3 normal = new Vector3(-axis.y, axis.x);
                GetProjectedBounds(component, axis, normal, out float minAlong, out float maxAlong,
                    out float minAcross, out float maxAcross);
                for (int centerIndex = 0; centerIndex < centers.Count; centerIndex++)
                {
                    Vector3 center = centers[centerIndex];
                    float centerAlong = Vector3.Dot(center, axis);
                    float centerAcross = Vector3.Dot(center, normal);
                    float high = Mathf.Min(
                        Mathf.Min(centerAlong - minAlong, maxAlong - centerAlong) / textAspect,
                        Mathf.Min(centerAcross - minAcross, maxAcross - centerAcross));
                    if (high <= bestHalfHeight) continue;
                    float low = 0f;
                    for (int iteration = 0; iteration < SearchIterations; iteration++)
                    {
                        float candidate = (low + high) * 0.5f;
                        if (FitsInsideSparse(center, axis, normal, candidate * textAspect, candidate, zoneIds,
                                allowSmallEmptyGaps)) low = candidate;
                        else high = candidate;
                    }
                    if (low <= bestHalfHeight) continue;
                    bestHalfHeight = low;
                    bestCenter = center;
                    bestAxis = axis;
                }
            }

            if (bestHalfHeight <= 0.2f) return default;
            float halfHeight = bestHalfHeight * Mathf.Clamp(style.territory_padding, 0.5f, 0.98f);
            float halfWidth = halfHeight * textAspect;
            Vector3 bestNormal = new Vector3(-bestAxis.y, bestAxis.x);
            for (int attempt = 0; attempt < 12 &&
                 !FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds,
                     allowSmallEmptyGaps); attempt++)
            {
                halfWidth *= 0.9f;
                halfHeight *= 0.9f;
            }
            if (!FitsInsideDense(bestCenter, bestAxis, bestNormal, halfWidth, halfHeight, zoneIds,
                    allowSmallEmptyGaps)) return default;
            float curvature = CalculateTerritoryCurvature(component, bestCenter, bestAxis, halfWidth,
                halfHeight, style.arc_curvature);
            return TerritoryPlacement.Create(bestCenter, bestAxis, halfWidth, halfHeight, curvature);
        }

        private static TerritoryPlacement BuildPointPlacement(List<Vector3> points, string text, TerritoryLabelStyle style)
        {
            if (points == null || points.Count == 0) return default;
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < points.Count; i++) centroid += points[i];
            centroid /= points.Count;
            float angle = style.orientation == TerritoryLabelOrientation.Horizontal ? 0f :
                style.orientation == TerritoryLabelOrientation.Vertical ? 90f : CalculatePrincipalAngle(points, centroid);
            float radians = angle * Mathf.Deg2Rad;
            Vector3 axis = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector3 normal = new Vector3(-axis.y, axis.x);
            GetProjectedBounds(points, axis, normal, out float minAlong, out float maxAlong,
                out float minAcross, out float maxAcross);
            float aspect = EstimateTextAspect(text, style.rich_text);
            float halfHeight = Mathf.Min((maxAlong - minAlong) / (2f * aspect),
                (maxAcross - minAcross) * 0.5f) * Mathf.Clamp(style.territory_padding, 0.5f, 0.94f);
            if (halfHeight <= 0.2f) return default;
            float halfWidth = halfHeight * aspect;
            float curvature = CalculateTerritoryCurvature(points, centroid, axis, halfWidth, halfHeight,
                style.arc_curvature);
            return TerritoryPlacement.Create(centroid, axis, halfWidth, halfHeight, curvature);
        }

        private bool FitsInsideSparse(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            if (halfWidth <= 0f || halfHeight <= 0f) return false;
            int steps = Mathf.Max(4, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            for (int stripe = 0; stripe < 5; stripe++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, stripe * 0.25f);
                for (int step = 0; step <= steps; step++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, step / (float)steps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds,
                            allowSmallEmptyGaps)) return false;
                }
            }
            return true;
        }

        private bool FitsInsideDense(Vector3 center, Vector3 axis, Vector3 normal,
            float halfWidth, float halfHeight, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            int xSteps = Mathf.Max(2, Mathf.CeilToInt(halfWidth * 2f / SampleSpacing));
            int ySteps = Mathf.Max(2, Mathf.CeilToInt(halfHeight * 2f / SampleSpacing));
            for (int y = 0; y <= ySteps; y++)
            {
                float across = Mathf.Lerp(-halfHeight, halfHeight, y / (float)ySteps);
                for (int x = 0; x <= xSteps; x++)
                {
                    float along = Mathf.Lerp(-halfWidth, halfWidth, x / (float)xSteps);
                    if (!ContainsTerritoryPoint(center + axis * along + normal * across, zoneIds,
                            allowSmallEmptyGaps)) return false;
                }
            }
            return true;
        }

        private bool ContainsTerritoryPoint(Vector3 point, HashSet<int> zoneIds, bool allowSmallEmptyGaps)
        {
            if (World.world == null) return false;
            int x = Mathf.FloorToInt(point.x);
            int y = Mathf.FloorToInt(point.y);
            WorldTile tile = World.world.GetTile(x, y);
            if (tile?.zone != null && zoneIds.Contains(tile.zone.id)) return true;
            return allowSmallEmptyGaps && IsSmallNeutralGap(x, y, zoneIds);
        }

        // A gap is usable only when it is a tiny, closed neutral pocket. City-owned enclaves always reject it.
        private bool IsSmallNeutralGap(int startX, int startY, HashSet<int> zoneIds)
        {
            // 两三个区块大小的无主缺口也允许文字跨过(连通判断同样桥接，见 FindLargestConnectedComponent)
            const int maxGapTiles = 160;
            int startKey = GetTileKey(startX, startY);
            if (_small_gap_cache.TryGetValue(startKey, out bool cached)) return cached;

            _gap_queue.Clear();
            _gap_tiles.Clear();
            _gap_queue.Enqueue(new Vector2Int(startX, startY));
            _gap_tiles.Add(startKey);
            int friendlyBorder = 0;
            bool valid = true;

            while (_gap_queue.Count > 0 && valid)
            {
                Vector2Int point = _gap_queue.Dequeue();
                WorldTile tile = World.world.GetTile(point.x, point.y);
                if (tile == null || tile.zone_city != null)
                {
                    valid = false;
                    break;
                }

                for (int direction = 0; direction < 4; direction++)
                {
                    int x = point.x + (direction == 0 ? 1 : direction == 1 ? -1 : 0);
                    int y = point.y + (direction == 2 ? 1 : direction == 3 ? -1 : 0);
                    WorldTile neighbour = World.world.GetTile(x, y);
                    if (neighbour == null)
                    {
                        valid = false;
                        break;
                    }
                    if (neighbour.zone != null && zoneIds.Contains(neighbour.zone.id))
                    {
                        friendlyBorder++;
                        continue;
                    }
                    // Even a very small country inside another country is never treated as empty space.
                    if (neighbour.zone_city != null)
                    {
                        valid = false;
                        break;
                    }
                    int key = GetTileKey(x, y);
                    if (_gap_tiles.Add(key))
                    {
                        if (_gap_tiles.Count > maxGapTiles)
                        {
                            valid = false;
                            break;
                        }
                        _gap_queue.Enqueue(new Vector2Int(x, y));
                    }
                }
            }

            valid &= friendlyBorder >= 4;
            foreach (int key in _gap_tiles) _small_gap_cache[key] = valid;
            return valid;
        }

        private static int GetTileKey(int x, int y)
        {
            unchecked { return x * 486187739 + y; }
        }

        private static List<float> BuildCandidateAngles(float principalAngle, TerritoryLabelOrientation orientation)
        {
            List<float> result = new List<float>(9);
            if (orientation == TerritoryLabelOrientation.Horizontal) result.Add(0f);
            else if (orientation == TerritoryLabelOrientation.Vertical) result.Add(90f);
            else
            {
                for (int offset = -30; offset <= 30; offset += 10) AddUniqueAngle(result, principalAngle + offset);
                AddUniqueAngle(result, 0f);
                AddUniqueAngle(result, 90f);
            }
            return result;
        }

        private static void AddUniqueAngle(List<float> angles, float angle)
        {
            angle = NormalizeReadableAngle(angle);
            for (int i = 0; i < angles.Count; i++)
                if (Mathf.Abs(Mathf.DeltaAngle(angles[i], angle)) < 1f) return;
            angles.Add(angle);
        }

        private static float NormalizeReadableAngle(float angle)
        {
            while (angle > 90f) angle -= 180f;
            while (angle < -90f) angle += 180f;
            return angle;
        }

        private static List<Vector3> BuildCandidateCenters(List<TileZone> component, Vector3 centroid)
        {
            List<Vector3> result = new List<Vector3>(Mathf.Min(component.Count, MaxCandidateCenters) + 1);
            Vector3 nearest = ZoneCenter(component[0]);
            float nearestDistance = (nearest - centroid).sqrMagnitude;
            for (int i = 1; i < component.Count; i++)
            {
                Vector3 point = ZoneCenter(component[i]);
                float distance = (point - centroid).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = point;
            }
            result.Add(nearest);
            int count = Mathf.Min(component.Count, MaxCandidateCenters);
            for (int i = 0; i < count; i++)
            {
                int index = count == 1 ? 0 : Mathf.RoundToInt(i * (component.Count - 1f) / (count - 1f));
                Vector3 point = ZoneCenter(component[index]);
                if ((point - nearest).sqrMagnitude > 0.1f) result.Add(point);
            }
            return result;
        }

        private static float CalculatePrincipalAngle(List<TileZone> zones, Vector3 centroid)
        {
            float xx = 0f, xy = 0f, yy = 0f;
            for (int i = 0; i < zones.Count; i++)
            {
                Vector3 delta = ZoneCenter(zones[i]) - centroid;
                xx += delta.x * delta.x;
                xy += delta.x * delta.y;
                yy += delta.y * delta.y;
            }
            return NormalizeReadableAngle(0.5f * Mathf.Atan2(2f * xy, xx - yy) * Mathf.Rad2Deg);
        }

        private static float CalculatePrincipalAngle(List<Vector3> points, Vector3 centroid)
        {
            float xx = 0f, xy = 0f, yy = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - centroid;
                xx += delta.x * delta.x;
                xy += delta.x * delta.y;
                yy += delta.y * delta.y;
            }
            return NormalizeReadableAngle(0.5f * Mathf.Atan2(2f * xy, xx - yy) * Mathf.Rad2Deg);
        }

        private static Vector3 CalculateZoneCentroid(List<TileZone> zones)
        {
            Vector3 result = Vector3.zero;
            for (int i = 0; i < zones.Count; i++) result += ZoneCenter(zones[i]);
            return result / zones.Count;
        }

        // Measure the territory's middle path in seven slices. This preserves a straight label for
        // straight countries and only bends labels where the country itself bends.
        private static float CalculateTerritoryCurvature(List<TileZone> zones, Vector3 center, Vector3 axis,
            float halfWidth, float halfHeight, float maximumCurvature)
        {
            if (zones == null || zones.Count < 3) return 0f;
            Vector3[] points = new Vector3[zones.Count];
            for (int i = 0; i < zones.Count; i++) points[i] = ZoneCenter(zones[i]);
            return CalculateTerritoryCurvature(points, center, axis, halfWidth, halfHeight, maximumCurvature);
        }

        private static float CalculateTerritoryCurvature(IList<Vector3> points, Vector3 center, Vector3 axis,
            float halfWidth, float halfHeight, float maximumCurvature)
        {
            const int sliceCount = 7;
            if (points == null || points.Count < 3 || halfWidth <= 0.01f || halfHeight <= 0.01f) return 0f;
            Vector3 normal = new Vector3(-axis.y, axis.x);
            float minAlong = float.MaxValue;
            float maxAlong = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float along = Vector3.Dot(points[i] - center, axis);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
            }
            float span = maxAlong - minAlong;
            if (span <= 0.1f) return 0f;

            float[] offsets = new float[sliceCount];
            int[] counts = new int[sliceCount];
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 delta = points[i] - center;
                float along = Vector3.Dot(delta, axis);
                int slice = Mathf.Clamp(Mathf.FloorToInt((along - minAlong) / span * sliceCount), 0,
                    sliceCount - 1);
                offsets[slice] += Vector3.Dot(delta, normal);
                counts[slice]++;
            }

            int first = -1;
            int last = -1;
            for (int i = 0; i < sliceCount; i++)
            {
                if (counts[i] == 0) continue;
                offsets[i] /= counts[i];
                if (first < 0) first = i;
                last = i;
            }
            if (first < 0 || last - first < 2) return 0f;

            float residualSum = 0f;
            float weightSum = 0f;
            for (int i = first + 1; i < last; i++)
            {
                if (counts[i] == 0) continue;
                float progress = (i - first) / (float)(last - first);
                float baseline = Mathf.Lerp(offsets[first], offsets[last], progress);
                float profile = 4f * progress * (1f - progress);
                residualSum += (offsets[i] - baseline) * profile;
                weightSum += profile * profile;
            }
            if (weightSum <= 0.01f) return 0f;

            float bend = residualSum / weightSum;
            float usableCurvature = Mathf.Min(Mathf.Abs(maximumCurvature), halfHeight * 0.45f / halfWidth);
            return Mathf.Clamp(bend / halfWidth, -usableCurvature, usableCurvature);
        }

        private static void GetProjectedBounds(List<TileZone> zones, Vector3 axis, Vector3 normal,
            out float minAlong, out float maxAlong, out float minAcross, out float maxAcross)
        {
            minAlong = minAcross = float.MaxValue;
            maxAlong = maxAcross = float.MinValue;
            float alongRadius = ZoneSize * 0.5f * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y));
            float acrossRadius = ZoneSize * 0.5f * (Mathf.Abs(normal.x) + Mathf.Abs(normal.y));
            for (int i = 0; i < zones.Count; i++)
            {
                Vector3 center = ZoneCenter(zones[i]);
                float along = Vector3.Dot(center, axis);
                float across = Vector3.Dot(center, normal);
                minAlong = Mathf.Min(minAlong, along - alongRadius);
                maxAlong = Mathf.Max(maxAlong, along + alongRadius);
                minAcross = Mathf.Min(minAcross, across - acrossRadius);
                maxAcross = Mathf.Max(maxAcross, across + acrossRadius);
            }
        }

        private static void GetProjectedBounds(List<Vector3> points, Vector3 axis, Vector3 normal,
            out float minAlong, out float maxAlong, out float minAcross, out float maxAcross)
        {
            minAlong = minAcross = float.MaxValue;
            maxAlong = maxAcross = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                float along = Vector3.Dot(points[i], axis);
                float across = Vector3.Dot(points[i], normal);
                minAlong = Mathf.Min(minAlong, along);
                maxAlong = Mathf.Max(maxAlong, along);
                minAcross = Mathf.Min(minAcross, across);
                maxAcross = Mathf.Max(maxAcross, across);
            }
        }

        private static Vector3 ZoneCenter(TileZone zone)
        {
            return new Vector3(zone.x * ZoneSize + ZoneSize * 0.5f, zone.y * ZoneSize + ZoneSize * 0.5f);
        }

        private static float EstimateTextAspect(string value, bool richText)
        {
            if (string.IsNullOrWhiteSpace(value)) return 1.5f;
            float width = 0f;
            float maxWidth = 0f;
            int lineCount = 1;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (richText && character == '<')
                {
                    int tagEnd = value.IndexOf('>', i + 1);
                    if (tagEnd >= 0) { i = tagEnd; continue; }
                }
                if (character == '\n')
                {
                    maxWidth = Mathf.Max(maxWidth, width);
                    width = 0f;
                    lineCount++;
                    continue;
                }
                float glyphScale = richText && lineCount > 1 ? 0.55f : 1f;
                if (char.IsWhiteSpace(character)) width += 0.35f * glyphScale;
                else if (character >= '\u3400' && character <= '\u9fff') width += glyphScale;
                else if (char.IsUpper(character)) width += 0.72f * glyphScale;
                else width += 0.58f * glyphScale;
            }
            maxWidth = Mathf.Max(maxWidth, width);
            return Mathf.Clamp(maxWidth * 1.08f / Mathf.Max(1f, lineCount * 0.92f), 1.4f, 24f);
        }
    }

    private readonly struct ScreenLabelBounds
    {
        private readonly Vector2 _center;
        private readonly float _half_width;
        private readonly float _half_height;
        private readonly float _radians;

        public ScreenLabelBounds(Vector2 center, float halfWidth, float halfHeight, float radians)
        {
            _center = center;
            _half_width = halfWidth;
            _half_height = halfHeight;
            _radians = radians;
        }

        public bool Overlaps(ScreenLabelBounds other)
        {
            return TerritoryLabelProjection.OrientedRectanglesOverlap(
                _center.x, _center.y, _half_width, _half_height, _radians,
                other._center.x, other._center.y, other._half_width, other._half_height,
                other._radians, CollisionSeparationPixels);
        }
    }

    private struct TerritoryPlacement
    {
        public bool valid;
        public Vector3 center;
        public Vector3 axis;
        public Vector3 normal;
        public float half_width;
        public float half_height;
        public float curvature;

        public static TerritoryPlacement Create(Vector3 center, Vector3 axis, float halfWidth, float halfHeight,
            float curvature = 0f)
        {
            axis.Normalize();
            return new TerritoryPlacement
            {
                valid = halfWidth > 0f && halfHeight > 0f,
                center = center,
                axis = axis,
                normal = new Vector3(-axis.y, axis.x),
                half_width = halfWidth,
                half_height = halfHeight,
                curvature = curvature
            };
        }
    }

    private static void CollectCityZones(IEnumerable<City> cities, List<TileZone> zones, HashSet<int> zoneIds)
    {
        zones.Clear();
        zoneIds.Clear();
        foreach (City city in cities)
        {
            if (city == null || city.isRekt() || city.zones == null) continue;
            for (int i = 0; i < city.zones.Count; i++)
            {
                TileZone zone = city.zones[i];
                if (zone == null || !zoneIds.Add(zone.id)) continue;
                zones.Add(zone);
            }
        }
    }

    private static void CollectZones(IEnumerable<TileZone> source, List<TileZone> zones, HashSet<int> zoneIds)
    {
        zones.Clear();
        zoneIds.Clear();
        foreach (TileZone zone in source)
        {
            if (zone == null || !zoneIds.Add(zone.id)) continue;
            zones.Add(zone);
        }
    }

    private static int CalculateZoneSignature(List<TileZone> zones)
    {
        unchecked
        {
            int sum = 0, xor = 0;
            for (int i = 0; i < zones.Count; i++)
            {
                int value = zones[i].id * 397;
                sum += value;
                xor ^= value + (value << 11);
            }
            return (zones.Count * 486187739) ^ sum ^ xor;
        }
    }

    // 国土中间的无主空地(没有任何城市的区块)不该把领土切成两块：连通判断允许跨过最多
    // MaxNeutralBridgeZones 个无主区块把两侧桥接起来；第三方国家的区块一律不能跨。
    private const int MaxNeutralBridgeZones = 2;

    private static void FindLargestConnectedComponent(List<TileZone> zones, HashSet<int> zoneIds,
        List<TileZone> walk, List<TileZone> largest, HashSet<int> visited, Queue<TileZone> queue)
    {
        largest.Clear();
        visited.Clear();
        queue.Clear();
        var bridgeQueue = new Queue<(TileZone zone, int gap)>();
        var bridgeBest = new Dictionary<int, int>();
        for (int i = 0; i < zones.Count; i++)
        {
            TileZone start = zones[i];
            if (!visited.Add(start.id)) continue;
            walk.Clear();
            bridgeQueue.Clear();
            bridgeBest.Clear();
            bridgeQueue.Enqueue((start, 0));
            while (bridgeQueue.Count > 0)
            {
                (TileZone zone, int gap) = bridgeQueue.Dequeue();
                bool friendly = zoneIds.Contains(zone.id);
                if (friendly) walk.Add(zone);
                TileZone[] neighbours = zone.neighbours;
                if (neighbours == null) continue;
                for (int n = 0; n < neighbours.Length; n++)
                {
                    TileZone neighbour = neighbours[n];
                    if (neighbour == null) continue;
                    if (zoneIds.Contains(neighbour.id))
                    {
                        if (visited.Add(neighbour.id)) bridgeQueue.Enqueue((neighbour, 0));
                        continue;
                    }
                    // 只有无国籍空地可以当桥；别国领土(含中立部落的城)不能跨
                    if (neighbour.city != null) continue;
                    int nextGap = (friendly ? 0 : gap) + 1;
                    if (nextGap > MaxNeutralBridgeZones) continue;
                    if (bridgeBest.TryGetValue(neighbour.id, out int best) && best <= nextGap) continue;
                    bridgeBest[neighbour.id] = nextGap;
                    bridgeQueue.Enqueue((neighbour, nextGap));
                }
            }
            if (walk.Count <= largest.Count) continue;
            largest.Clear();
            largest.AddRange(walk);
        }
    }

    private static float GetCanvasScale()
    {
        Canvas canvas = CanvasMain.instance == null ? null : CanvasMain.instance.canvas_map_names;
        return canvas == null ? 1f : Mathf.Max(0.01f, canvas.scaleFactor);
    }
}

[DefaultExecutionOrder(10000)]
public sealed class TerritoryLabelRendererHost : MonoBehaviour
{
    private void LateUpdate() => TerritoryLabelRenderer.HostLateUpdate(this);
    private void OnDestroy() => TerritoryLabelRenderer.HostDestroyed(this);
}

public sealed class TerritoryLabelGoldGradient : BaseMeshEffect
{
    private Color _top = new Color32(255, 244, 194, 255);
    private Color _bottom = new Color32(184, 156, 68, 255);
    private float _alpha = 1f;

    public void Configure(Color top, Color bottom, float alpha)
    {
        bool changed = !enabled || _top != top || _bottom != bottom || !Mathf.Approximately(_alpha, alpha);
        _top = top;
        _bottom = bottom;
        _alpha = Mathf.Clamp01(alpha);
        enabled = true;
        if (changed && graphic != null) graphic.SetVerticesDirty();
    }

    public void DisableGradient()
    {
        if (!enabled) return;
        enabled = false;
        if (graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;

        UIVertex vertex = default;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            minY = Mathf.Min(minY, vertex.position.y);
            maxY = Mathf.Max(maxY, vertex.position.y);
        }

        float height = Mathf.Max(0.001f, maxY - minY);
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            Color color = Color.Lerp(_bottom, _top, (vertex.position.y - minY) / height);
            color.a *= _alpha;
            vertex.color = color;
            vertexHelper.SetUIVertex(vertex, i);
        }
    }
}

// Bends the baseline along the territory-specific path calculated by TerritoryLabelRenderer.
// 领土铭牌的描边：八个方向各描一份(原版 Outline 只描四个斜角，描粗了横竖笔画边上会露缝)，
// 颜色的透明度乘上字本身的透明度(同原版 useGraphicAlpha)
public sealed class TerritoryLabelOutline : BaseMeshEffect
{
    private const float Axis = 1.41421356f;
    private static readonly Vector2[] Directions =
    {
        new(1f, 1f), new(1f, -1f), new(-1f, 1f), new(-1f, -1f),
        new(Axis, 0f), new(-Axis, 0f), new(0f, Axis), new(0f, -Axis)
    };
    private static readonly List<UIVertex> Source = new(), Output = new();
    private Color _color = Color.black;
    private Vector2 _distance = Vector2.one;

    public Color effectColor
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    public Vector2 effectDistance
    {
        get => _distance;
        set
        {
            if (_distance == value) return;
            _distance = value;
            if (graphic != null) graphic.SetVerticesDirty();
        }
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;
        Source.Clear();
        vertexHelper.GetUIVertexStream(Source);
        Output.Clear();
        float dx = Mathf.Abs(_distance.x), dy = Mathf.Abs(_distance.y);
        foreach (Vector2 direction in Directions)
            foreach (UIVertex vertex in Source)
            {
                UIVertex copy = vertex;
                copy.position.x += direction.x * dx;
                copy.position.y += direction.y * dy;
                Color32 color = _color;
                color.a = (byte)(color.a * vertex.color.a / 255);
                copy.color = color;
                Output.Add(copy);
            }
        Output.AddRange(Source);
        vertexHelper.Clear();
        vertexHelper.AddUIVertexTriangleStream(Output);
    }
}

public sealed class TerritoryLabelArc : BaseMeshEffect
{
    private float _curvature;

    public void Configure(float curvature)
    {
        curvature = Mathf.Clamp(curvature, -0.16f, 0.16f);
        bool changed = !Mathf.Approximately(_curvature, curvature);
        _curvature = curvature;
        enabled = !Mathf.Approximately(_curvature, 0f);
        if (changed && graphic != null) graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vertexHelper)
    {
        if (!IsActive() || vertexHelper.currentVertCount == 0) return;
        UIVertex vertex = default;
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            minX = Mathf.Min(minX, vertex.position.x);
            maxX = Mathf.Max(maxX, vertex.position.x);
        }

        float halfSpan = (maxX - minX) * 0.5f;
        if (halfSpan <= 0.01f) return;
        float centerX = (minX + maxX) * 0.5f;
        for (int i = 0; i < vertexHelper.currentVertCount; i++)
        {
            vertexHelper.PopulateUIVertex(ref vertex, i);
            float normalized = (vertex.position.x - centerX) / halfSpan;
            vertex.position.y += _curvature * halfSpan * (1f - normalized * normalized);
            vertexHelper.SetUIVertex(vertex, i);
        }
    }
}
