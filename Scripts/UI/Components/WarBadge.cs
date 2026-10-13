using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 战争徽记：帝国窗口里各国、各势力的行上，正在打仗的国家挂一枚战争图标——悬停看战况，点开战争详情窗口。
// 一国同时打几场仗时取最要紧的一场：自己是主攻/主守的优先，其次死伤多的
public sealed class WarBadge : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public const string IconPath = "ui/icons/iconWar";
    private Kingdom _kingdom;

    public static War MainWar(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.isRekt()) return null;
        return kingdom.getWars().Where(war => war != null && !war.isRekt() && !war.hasEnded())
            .OrderByDescending(war => war.main_attacker == kingdom || war.main_defender == kingdom)
            .ThenByDescending(war => war.getDeadAttackers() + war.getDeadDefenders())
            .FirstOrDefault();
    }

    // 横排行里加一枚(没打仗不加)；返回是否加了，调用方据此让出名字的宽度
    public static bool Add(Transform row, Kingdom kingdom, float size = 10f)
    {
        if (row == null || MainWar(kingdom) == null) return false;
        GameObject icon = Create(row, kingdom, size);
        LayoutElement element = icon.AddComponent<LayoutElement>();
        element.preferredWidth = element.minWidth = size;
        element.preferredHeight = element.minHeight = size;
        return true;
    }

    // 不在布局里的卡片(官制树的行政区卡)：钉在给定位置
    public static void Place(RectTransform card, Kingdom kingdom, Vector2 position, float size = 10f)
    {
        if (card == null || MainWar(kingdom) == null) return;
        var rect = (RectTransform)Create(card, kingdom, size).transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
    }

    private static GameObject Create(Transform parent, Kingdom kingdom, float size)
    {
        var icon = new GameObject("WarBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        icon.transform.SetParent(parent, false);
        ((RectTransform)icon.transform).sizeDelta = new Vector2(size, size);
        Image image = icon.GetComponent<Image>();
        image.sprite = SpriteTextureLoader.getSprite(IconPath);
        image.preserveAspect = true;
        image.raycastTarget = true;
        icon.AddComponent<WarBadge>()._kingdom = kingdom;
        return icon;
    }

    // 原版键值行(EmpireCraftStatsRow)：打仗时行首换成战争图标(调用方传 IconFor)并可点；行对象池化复用，不打仗时撤掉
    public static void Bind(KeyValueField field, Kingdom kingdom)
    {
        if (field?.icon == null) return;
        bool atWar = MainWar(kingdom) != null;
        WarBadge badge = field.icon.GetComponent<WarBadge>();
        if (!atWar)
        {
            if (badge != null) badge._kingdom = null;
            return;
        }
        badge ??= field.icon.gameObject.AddComponent<WarBadge>();
        badge._kingdom = kingdom;
        field.icon.raycastTarget = true;
    }

    public static string IconFor(Kingdom kingdom, string normal) => MainWar(kingdom) != null ? "iconWar" : normal;

    public void OnPointerEnter(PointerEventData eventData)
    {
        War war = MainWar(_kingdom);
        if (war != null) Tooltip.show(this, "war", new TooltipData { war = war });
    }

    public void OnPointerExit(PointerEventData eventData) => Tooltip.hideTooltip();

    public void OnPointerClick(PointerEventData eventData)
    {
        War war = MainWar(_kingdom);
        if (war == null) return;
        Tooltip.hideTooltip();
        SelectedMetas.selected_war = war;
        ScrollWindow.showWindow("war");
    }
}
