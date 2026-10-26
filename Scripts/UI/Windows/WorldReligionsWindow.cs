using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows
{
    // 天下宗教名册(用户 2026-10-27 定稿示意图 ①，主栏"文化与理念"组的按钮打开)：
    //   · 左栏：现存几教、信众；筛选(全部、有教阶、分出的教会)；按类型；
    //   · 右栏：主指标(最大宗教、国教最多、最近大事) → 两栏宗教卡(色条、名与地位、教首与类型·圣地、城/信众/国教/教权)，点开宗教窗口。
    public class WorldReligionsWindow : WideScrollWindow<WorldReligionsWindow>
    {
        private const int MaxCards = 24;

        private int _filter;
        private FaithType? _type;
        private Dictionary<Religion, ReligionDisplay.Stat> _stats = new();
        private float _worldPeople;
        private List<Religion> _all = new(), _shown = new();

        protected override void Prepare()
        {
            _stats = ReligionDisplay.Stats(out _worldPeople);
            _all = _stats.Keys.OrderByDescending(religion => _stats[religion].People).ToList();
            _shown = _all.Where(Matches).ToList();
        }

        private bool Matches(Religion religion)
        {
            if (_type != null && ReligionFaith.Of(religion) != _type) return false;
            return _filter switch
            {
                1 => ReligionFaith.HasHierarchy(religion),
                2 => religion.GetOrCreate().ParentReligionId >= 0,
                _ => true
            };
        }

        private void SetFilter(int filter, FaithType? type)
        {
            _filter = filter;
            _type = type;
            Refresh(resetScroll: true);
        }

        protected override void DrawRail(AutoVertLayoutGroup rail)
        {
            float people = _all.Sum(religion => _stats[religion].People);
            rail.AddRailTitle(LM.Get("WorldReligionsWindow Title"),
                string.Format(LM.Get("world_religions_summary"), _all.Count, UIHelper.ShortCount(people)));
            var menu = rail.BeginRailMenu();
            int index = 0;
            menu.AddRailOption(Icon("iconReligion"), LM.Get("world_religions_all"), _all.Count.ToString(), _filter == 0 && _type == null,
                () => SetFilter(0, null), index++);
            menu.AddRailOption(Icon("iconKings", "iconKingdom"), LM.Get("world_religions_hierarchy"),
                _all.Count(ReligionFaith.HasHierarchy).ToString(), _filter == 1, () => SetFilter(1, null), index++);
            menu.AddRailOption(Icon("iconFamily"), LM.Get("world_religions_split"),
                _all.Count(religion => religion.GetOrCreate().ParentReligionId >= 0).ToString(), _filter == 2,
                () => SetFilter(2, null), index++);

            var types = rail.BeginRailMenu(LM.Get("world_religions_by_type"));
            index = 0;
            foreach (IGrouping<FaithType, Religion> group in _all.GroupBy(ReligionFaith.Of).OrderByDescending(group => group.Count()))
            {
                FaithType type = group.Key;
                types.AddRailOption(null, ReligionDisplay.FaithName(type), group.Count().ToString(), _filter == 0 && _type == type,
                    () => SetFilter(0, type), index++);
            }
        }

        private static Sprite Icon(params string[] names) => UIHelper.FirstSprite(names.Select(name => "ui/icons/" + name).ToArray());

        protected override void DrawPage(AutoVertLayoutGroup page)
        {
            page.AddPageHeader(PageWidth, _type != null ? ReligionDisplay.FaithName(_type.Value) : LM.Get("world_religions_all"),
                LM.Get("world_religions_sorted"));
            if (_all.Count == 0)
            {
                page.AddEmptyCard(PageWidth, LM.Get("WorldReligionsWindow Title"), LM.Get("world_religions_none"));
                return;
            }
            Religion largest = _all.OrderByDescending(religion => _stats[religion].Cities).First();
            Religion widest = _all.OrderByDescending(religion => _stats[religion].States).First();
            (Religion latestReligion, ReligionExtension.ReligionEventData latest) = Latest();
            page.AddKpiRow(PageWidth,
                (LM.Get("world_religions_largest"), string.Format(LM.Get("world_religions_cities_value"), largest.name, _stats[largest].Cities),
                    WideKit.Gold),
                (LM.Get("world_religions_widest"), string.Format(LM.Get("world_religions_states_value"), widest.name, _stats[widest].States),
                    WideKit.Cyan),
                (LM.Get("world_religions_latest"), latest != null
                    ? $"{latestReligion.name} · {ReligionDisplay.KindName(latest.kind)}"
                    : LM.Get("label_none"), latest != null ? ReligionDisplay.KindHex(latest.kind) : WideKit.Muted));

            if (_shown.Count == 0)
            {
                page.AddEmptyCard(PageWidth, LM.Get("WorldReligionsWindow Title"), LM.Get("world_religions_filtered_none"));
                return;
            }
            page.AddCardGrid(PageWidth, _shown.Take(MaxCards).ToList(), 2, (row, width, religion) =>
            {
                ReligionDisplay.Stat stat = _stats[religion];
                var card = row.BeginColumnCard(width);
                card.AddColorStrip(width, ReligionDisplay.MainColor(religion));
                (string standing, string hex) = ReligionDisplay.Standing(religion);
                UIHelper.AddCardTitle(card, width, religion.name, WideKit.Gold, standing.ColorString(hex), 0f);
                City holy = PapacySystem.HolyCity(religion);
                Actor leader = PapacySystem.Pope(religion);
                card.AddPersonRow(width, leader,
                    string.Format(LM.Get("world_religions_caption"), ReligionDisplay.FaithName(religion),
                        holy != null ? holy.GetCityName() : LM.Get("papacy_see_lost")),
                    leader != null ? leader.getName() : ReligionDisplay.HierarchyName(religion),
                    leader != null ? ReligionDisplay.HeadCaption(religion, leader) : null);
                card.AddMetricRow(width,
                    (LM.Get("empire_list_metric_cities"), stat.Cities.ToString(), WideKit.Green),
                    (LM.Get("world_religions_people"), UIHelper.ShortCount(stat.People), WideKit.Value),
                    (LM.Get("world_religions_states"), stat.States.ToString(), WideKit.Cyan),
                    (LM.Get("papacy_authority"), ReligionFaith.HasHierarchy(religion) ? $"{PapacySystem.Authority(religion):0}" : "—",
                        WideKit.Gold));
                Religion target = religion;
                UIHelper.MakeClickable(card.gameObject, () => ReligionInfoWindow.Open(target));
            });
        }

        // 各宗教大事记里最新的一条
        private (Religion, ReligionExtension.ReligionEventData) Latest()
        {
            Religion owner = null;
            ReligionExtension.ReligionEventData latest = null;
            foreach (Religion religion in _all)
            {
                ReligionExtension.ReligionEventData last = ReligionChronicle.Events(religion).LastOrDefault();
                if (last == null || latest != null && last.at <= latest.at) continue;
                latest = last;
                owner = religion;
            }
            return (owner, latest);
        }
    }
}
