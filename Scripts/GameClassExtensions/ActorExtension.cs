using EmpireCraft.Scripts.Enums;
using NeoModLoader.services;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using EpPathFinding.cs;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using NeoModLoader.General;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.TipAndLog;
using EmpireCraft.Scripts.GodPowers;
using EmpireCraft.Scripts.Layer;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;

namespace EmpireCraft.Scripts.GameClassExtensions;

public static class ActorExtension
{
    public class ActorExtraData
    {
        public long id;
        // 爵位  
        [JsonConverter(typeof(StringEnumConverter))]
        public PeeragesLevel peeragesLevel;
        public string title = "";
        public List<long> titles = new List<long>();
        public List<long> want_acuired_title = new List<long>();
        public List<long> owned_title = new List<long>();
    }
    public static void SetTitle(this Actor a, string value)
    {
        if (a == null || value == null) return;
        if (a.kingdom == null) return;
        if (a.kingdom.GetEmpire() == null) return;
        GetOrCreate(a).title = value + " " + TranslateHelper.GetPeerageTranslate(a.GetPeeragesLevel());
    }

    public static void RemoveExtraData(this Actor a)
    {
        if (a == null) return;
        ExtensionManager<Actor, ActorExtraData>.Remove(a);
    }

    public static void Clear()
    {
        ExtensionManager<Actor, ActorExtraData>.Clear();
    }

    public static bool canAcquireTitle(this Actor a)
    {
        if (a.isKing())
        {
            Kingdom k = a.kingdom;
            if (k.isInEmpire())
            {
                if (k.GetEmpire().getEmpirePeriod()!=EmpirePeriod.逐鹿群雄 && k.GetEmpire().getEmpirePeriod() != EmpirePeriod.天命丧失) return false;
            }
            foreach (City city in k.cities)
            {
                if (city.hasTitle())
                {
                    KingdomTitle title = city.GetTitle();
                    if (!a.GetOwnedTitle().Contains(title.data.id))
                    {
                        foreach(City tCity in title.city_list)
                        {
                            if (tCity.kingdom != k && tCity.kingdom.countTotalWarriors()<k.countTotalWarriors())
                            {
                                return true;
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    public static List<KingdomTitle> getAcquireTitle(this Actor a)
    {
        List<KingdomTitle> titles = new();
        if (a.isKing())
        {
            Kingdom k = a.kingdom;
            foreach (City city in k.cities)
            {
                if (city.hasTitle())
                {
                    KingdomTitle title = city.GetTitle();
                    if (!titles.Contains(title)&&!a.GetOwnedTitle().Contains(title.data.id))
                    {
                        titles.Add(title);
                    }
                }
            }
        }
        return titles;
    }

    public static string GetTitle(this Actor a)
    {
        if (!a.HasTitle()) return "";
        if (!a.isKing()) return "";
        if (a.kingdom == null) return "";
        Kingdom kingdom = a.kingdom;
        var ownedTitles = a.GetOwnedTitle();
        if (ownedTitles == null) return "";
        try
        {
            var hasCapitalTitle = ownedTitles.Exists(t => ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(t) ? ModClass.KINGDOM_TITLE_MANAGER.get(t).title_capital == kingdom.capital : false);
            if (hasCapitalTitle)
            {
                kingdom.SetMainTitle(kingdom.capital.GetTitle());
                return kingdom.capital.GetTitle().data.name;
            }
            else
            {
                KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(ownedTitles.FirstOrDefault());
                kingdom.SetMainTitle(title);
                return title.data.name;
            }
        }
        catch {
            return "";
        }
    }
    public static bool HasTitle(this Actor a)
    {
        return GetOrCreate(a).owned_title.Count>0;
    }

    public static bool HasCapitalTitle(this Actor a)
    {
        if (a.kingdom.capital.hasTitle())
        {
            return a.GetOwnedTitle().Contains(a.kingdom.capital.GetTitle().data.id);
        }
        return false;
    }

    public static bool IsCapitalTitleBelongsToEmperor(this Actor a)
    {
        if (!a.hasKingdom()) return false;
        if (a.kingdom.GetEmpire() == null) return false;
        if (a.kingdom.GetEmpire().emperor.GetOwnedTitle()==null) return false;
        if (a.kingdom.capital.GetTitle()==null) return false;
        if (a.kingdom.isInEmpire())
        {
            if (a.kingdom.capital.hasTitle())
            {
                return a.kingdom.GetEmpire().emperor.GetOwnedTitle().Contains(a.kingdom.capital.GetTitle().data.id);
            }
        }
        return false;
    }

    public static string GetTitleName(this Actor a)
    {
        string title = a.GetTitle();
        string[] titleParts = title.Split(' ');
        if (titleParts.Length <= 0) return null;
        if (titleParts.Length <= 1) return title;
        if (titleParts.Length <= 2) return titleParts[0];
        return titleParts[titleParts.Length - 2];
    }
    public static bool syncData(this Actor a, ActorExtraData actorExtraData)
    {
        var ed = GetOrCreate(a);
        ed.id = actorExtraData.id;
        ed.peeragesLevel = actorExtraData.peeragesLevel;
        ed.title = actorExtraData.title;
        ed.owned_title = actorExtraData.owned_title;
        ed.want_acuired_title = actorExtraData.want_acuired_title;
        return true;
    }

    public static ActorExtraData getExtraData(this Actor a, bool isSave=false)
    {
        if (GetOrCreate(a, isSave) == null) return null;
        ActorExtraData data = new ActorExtraData();
        data.id = a.getID();
        data.peeragesLevel = a.GetPeeragesLevel();
        data.title = a.GetTitle();
        data.want_acuired_title = a.GetAcquireTitle();
        data.owned_title = a.GetOwnedTitle();
        return data;
    }

    public static bool canTakeTitle(this Actor a)
    {
        if (!a.isKing()) return false;
        Kingdom kingdom = a.kingdom;
        if (kingdom == null) return false;
        List<long> controledTitles = kingdom.getControledTitle().FindAll(t=>!t.owner.isEmperor()).Select(t=>t.data.id).ToList();
        var commonTitles = controledTitles.Intersect(a.GetOwnedTitle());
        return commonTitles.Count() < controledTitles.Count();
    }

    public static List<KingdomTitle> titleCanBeDestroy(this Actor a)
    {
        List<KingdomTitle> titles = new List<KingdomTitle>();
        foreach(long id in a.GetOwnedTitle())
        {
            if (ModClass.KINGDOM_TITLE_MANAGER.checkTitleExist(id))
            {
                KingdomTitle kt = ModClass.KINGDOM_TITLE_MANAGER.get(id);
                if (kt == null) continue;
                if (Date.getYearsSince(kt.data.timestamp_been_controled) >= ModClass.TITLE_BEEN_DESTROY_TIME && kt.title_capital != a.kingdom.capital)
                {
                    titles.Add(kt);
                }
            }
        }
        return titles;
    }

    public static List<KingdomTitle> takeTitle(this Actor a)
    {
        if (!a.isKing()) return null;
        List<KingdomTitle> takedTitles = new List<KingdomTitle>();
        Kingdom kingdom = a.kingdom;
        List<KingdomTitle> titles = kingdom.getControledTitle();
        foreach(KingdomTitle t in titles)
        {
            if(t.HasOwner()&&t.owner.isEmperor())
            {
                if (!a.GetAcquireTitle().Contains(t.id)&&t.owner.getID()!=a.getID()) 
                {
                    a.AddAcquireTitle(t);
                }
            }
            else
            {
                if (!a.GetOwnedTitle().Contains(t.id)) 
                {
                    takedTitles.Add(t);
                    if (t.HasOwner()) 
                    {
                        t.owner.removeTitle(t);
                    }
                    a.AddOwnedTitle(t);
                    t.data.timestamp_been_controled = World.world.getCurWorldTime();
                }
            }
        }
        return takedTitles;
    }

    public static void AddAcquireTitle(this Actor a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        ed.want_acuired_title.Add(title.data.id);
    }

    public static void AddOwnedTitle(this Actor a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        if (ed == null) return;
        if (title == null) return;
        if (ed.owned_title==null) ed.owned_title = new List<long> { 0 };
        if (!ed.owned_title.Contains(title.data.id))
            ed.owned_title.Add(title.data.id);
            title.owner = a;
    }

    public static void removeTitle(this Actor a, KingdomTitle title)
    {
        var ed = GetOrCreate(a);
        if (a.GetOwnedTitle().Contains(title.data.id))
        {
            if (!a.isEmperor()&&a.isKing())
            {
                if (a.kingdom.GetKingdomName()==title.data.name)
                {
                    a.kingdom.data.name = a.kingdom.capital.name;
                    a.kingdom.becomeKingdom();
                }
                if (a.kingdom.GetMainTitle() == title)
                {
                    a.kingdom.RemoveMainTitle();
                }
            }
            ed.owned_title.Remove(title.data.id);
            title.owner = null;
        }
    }

    public static List<long> GetAcquireTitle(this Actor a)
    {
        var ed = GetOrCreate(a);
        return ed.want_acuired_title;
    }

    public static List<long> GetOwnedTitle(this Actor a)
    {
        if (a == null) return null;
        var ed = GetOrCreate(a);
        return ed.owned_title;
    }

    public static void ClearTitle(this Actor a)
    {
        var ed = GetOrCreate(a);
        ed.owned_title.Select(t=>ModClass.KINGDOM_TITLE_MANAGER.get(t).owner=null);
        ed.owned_title.Clear();
        ed.want_acuired_title.Clear();
    }

    public static bool isEmperor(this Actor a)
    {
        if (a==null) return false;
        if (!a.isKing()) return false;
        if (a.kingdom == null) return false;
        else return a.kingdom.isEmpire();
    }

    public static ActorExtraData GetOrCreate(this Actor a, bool isSave=false)
    {
        var ed = ExtensionManager < Actor, ActorExtraData>.GetOrCreate(a, isSave);
        return ed; 
    }
    public static PeeragesLevel GetPeeragesLevel(this Actor a)
        => GetOrCreate(a).peeragesLevel;
    public static void SetPeeragesLevel(this Actor a, PeeragesLevel lvl)
    {
        
        var data = GetOrCreate(a);
        data.id = a.getID();
        data.peeragesLevel = lvl;
    }
}