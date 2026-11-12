using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.Layer;
using Newtonsoft.Json.Linq;

namespace EmpireCraft.Scripts.Terminal;

// 身后(用户 2026-11-12 "玩家选择的君主死后可以选择是继续玩他的子嗣, 如果亡国了可以选择是不是接着玩下一个天命或者占领首都的国家")。
// 君主驾崩、失位、亡国以后席位照旧空出来、可以在地图上任选(见 Resolve)；这里另给几位"接着领"的人选，摆在选人页最上头：
//   · 原国还在：新君——是故君的子孙就写明"子嗣即位"，不是的也列出来(写明并非子嗣)；新君还没立的说一声，等着；
//   · 故君的子孙在别处为君的(分封出去的、另立的)也列；
//   · 原国亡了：占着故都的国家(是帝国成员的算它的朝廷)、故都所在天下如今受天命的国家(天命悬空就没有这一项)。
// 只列在位、能领的君主(同选人一样，别的席位领着的标出来、不能领)。领不领、领谁都由手机上定，不自动接手。
// 只在终端这头、不存档：君主在位时每月记一回他的子孙、国、都城与都城所在的天下
public static partial class TerminalHost
{
    private const int MaxLegacyOffers = 6;

    private sealed class Legacy
    {
        public long RulerId = -1, RealmId = -1, CapitalId = -1;
        public string Capital = "";
        public EmpireCore World;
        // 子孙(单位 id → child、grandchild)：只增不减，身故的留着也无妨
        public readonly Dictionary<long, string> Kin = new();
        public int Month = int.MinValue;
    }

    private static readonly ConditionalWeakTable<Seat, Legacy> Legacies = new();

    // 君主在位时记下(每回核对席位都调，一个月才真记一回；换了君主当即重记)
    private static void RememberLegacy(Seat seat)
    {
        Actor ruler = seat.Ruler;
        Kingdom realm = seat.Realm;
        if (ruler == null || realm == null) return;
        Legacy legacy = Legacies.GetOrCreateValue(seat);
        int month = MonthNow();
        long id = ruler.getID();
        if (legacy.RulerId == id && legacy.Month == month) return;
        if (legacy.RulerId != id)
        {
            legacy.Kin.Clear();
            legacy.World = null;
            legacy.CapitalId = -1;
            legacy.Capital = "";
        }
        legacy.RulerId = id;
        legacy.RealmId = realm.getID();
        legacy.Month = month;
        City capital = realm.capital;
        if (capital?.data != null && !capital.isRekt())
        {
            legacy.CapitalId = capital.getID();
            legacy.Capital = capital.data.name ?? "";
            legacy.World = TianxiaSystem.LeadOf(capital) ?? legacy.World;
        }
        try
        {
            foreach (Actor child in ruler.getChildren(pOnlyCurrentFamily: false))
            {
                if (child == null) continue;
                legacy.Kin[child.getID()] = "child";
                foreach (Actor grand in child.getChildren(pOnlyCurrentFamily: false))
                    if (grand != null && !legacy.Kin.ContainsKey(grand.getID())) legacy.Kin[grand.getID()] = "grandchild";
            }
        }
        catch (Exception)
        {
            // 亲子关系取不到：这个月不记，已记的留着
        }
    }

    // 换世界、读档：以前记的作废
    private static void ForgetLegacy(Seat seat) => Legacies.Remove(seat);

    // 给手机的：身后可以接着领的君主。why：heir 子孙为君(kin 写是子是孙)、successor 原国新君(并非子孙)、
    // capital 占着故都的国家、capital_mandate 占着故都又受天命、mandate 受天命的国家、interregnum 原国新君未立(只有 realm)
    private static JArray LegacyJson(Seat seat)
    {
        var list = new JArray();
        if (seat.VacantReason == "none" || !Legacies.TryGetValue(seat, out Legacy legacy) || legacy.RulerId < 0) return list;
        var seen = new HashSet<long>();
        void Offer(Kingdom kingdom, string why, string kin = null)
        {
            Actor king = kingdom?.king;
            if (list.Count >= MaxLegacyOffers || !Eligible(king) || !seen.Add(king.getID())) return;
            Seat taken = TakenBy(king.getID(), seat);
            var item = new JObject
            {
                ["id"] = king.getID().ToString(), ["kid"] = kingdom.getID().ToString(), ["ruler"] = king.getName(), ["realm"] = NameOf(kingdom),
                ["why"] = why, ["kind"] = Role(kingdom), ["cities"] = kingdom.cities?.Count ?? 0, ["age"] = king.getAge(), ["look"] = Look(king),
                ["capital"] = legacy.Capital
            };
            if (kin != null) item["kin"] = kin;
            if (taken != null) item["taken"] = SeatLabel(taken);
            list.Add(item);
        }
        Kingdom old = World.world.kingdoms.get(legacy.RealmId);
        bool standing = old?.data != null && !old.isRekt() && !old.wild;
        if (standing)
        {
            if (Eligible(old.king))
            {
                bool descendant = legacy.Kin.TryGetValue(old.king.getID(), out string kin);
                Offer(old, descendant ? "heir" : "successor", descendant ? kin : null);
            }
            else list.Add(new JObject { ["why"] = "interregnum", ["realm"] = NameOf(old) });
        }
        // 在别处为君的子孙
        foreach (KeyValuePair<long, string> pair in legacy.Kin)
        {
            Actor descendant = World.world.units.get(pair.Key);
            if (Eligible(descendant)) Offer(descendant.kingdom, "heir", pair.Value);
        }
        if (standing) return list;
        // 亡国：占着故都的、受天命的
        City capital = legacy.CapitalId < 0 ? null : World.world.cities.get(legacy.CapitalId);
        if (capital?.data == null || capital.isRekt()) capital = null;
        EmpireCore lead = (capital == null ? null : TianxiaSystem.LeadOf(capital)) ?? legacy.World;
        Kingdom mandate = TianxiaSystem.Holder(lead);
        Kingdom holder = capital?.kingdom;
        if (holder?.data != null && !holder.isRekt() && !holder.wild)
        {
            holder = holder.GetEmpire()?.CoreKingdom ?? holder;
            Offer(holder, holder == mandate ? "capital_mandate" : "capital");
        }
        Offer(mandate, "mandate");
        return list;
    }
}
