using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using EmpireCraft.Scripts.GeneralSystems;

namespace EmpireCraft.Scripts.Layer;
public class KingdomTitleManager : MetaSystemManager<KingdomTitle, KingdomTitleData>
{
    private readonly List<KingdomTitle> _titleUpdateBuffer = new();
    private int _scheduledUpdateCursor;

    public Sprite[] _cached_banner_backgrounds;

    public Sprite[] _cached_banner_icons;
    public KingdomTitleManager()
    {
        this.type_id = "kingdomTitle";
    }

    public override void updateDirtyUnits()
    {
    }
    public Sprite[] getBackgroundsList()
    {
        if (_cached_banner_backgrounds == null)
        {
            _cached_banner_backgrounds = SpriteTextureLoader.getSpriteList("kingdoms/backgrounds/");
        }
        return _cached_banner_backgrounds;
    }

    public Sprite[] getIconsList()
    {
        if (_cached_banner_icons == null)
        {
            _cached_banner_icons = SpriteTextureLoader.getSpriteList("kingdoms/icons/");
        }
        return _cached_banner_icons;
    }
    public override void startCollectHistoryData()
    {
    }
    // 国君空缺(老君死了新君未立)也能建：以前要求有国王，手动建法理赶上空位期就点了没反应(用户 2026-10-22 转群友"有时候无法建立法理")
    public KingdomTitle newKingdomTitle(City pCity)
    {
        if (pCity == null || pCity.isRekt() || pCity.kingdom == null || pCity.kingdom.isRekt() ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity)) return null;
        long id = OverallHelperFunc.IdGenerator.NextId();
        KingdomTitle title = base.newObjectFromID(id);
        title.newKingdomTitle(pCity);

        return title;
    }

    public bool checkTitleExist(long t) 
    {
        update(-1L);
        return get(t) != null;
    }

    // 法理扩张(未冻结法理)时，单个王国法理最多容纳的城市数；0 表示不限。只限制决议自动扩张，
    // 上帝之力手动加城不受影响。
    public static int MaxCitiesPerTitle => ModClass.KINGDOM_TITLE_FREEZE ? 0 : Math.Max(0, ModClass.TITLE_MAX_CITIES);

    public static int CountTitleCities(KingdomTitle title) =>
        title?.getCities()?.Count(city => city != null && !city.isRekt()) ?? 0;

    public static bool HasRoomFor(KingdomTitle title, int adding = 1)
    {
        int max = MaxCitiesPerTitle;
        return max <= 0 || title == null || CountTitleCities(title) + adding <= max;
    }

    // 把本国还没有法理的城市并进指定法理(受单个法理城市上限约束)，返回并入城数。
    // 新立的法理只收都城一座；称帝时按国家持有的法理登记帝国法理，漏掉其余城市，帝国法理、天下就只剩都城一块地
    public static int AddUntitledCities(KingdomTitle title, Kingdom kingdom, bool log = true)
    {
        if (title == null || title.isRekt() || kingdom == null || kingdom.isRekt() || kingdom.cities == null) return 0;
        int added = 0;
        foreach (City city in kingdom.cities.ToList())
        {
            if (city == null || city.isRekt() || city.hasTitle() ||
                EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(city)) continue;
            if (!HasRoomFor(title)) break;
            title.addCity(city);
            if (log) TranslateHelper.LogCityAddToTitle(city, title);
            added++;
        }
        return added;
    }

    public void AddCityToTitle(KingdomTitle pTitle, City pCity)
    {
        if (pTitle != null && pCity != null)
        {
            pTitle.addCity(pCity);
        }
    }
    public bool forceTitle(City pCity1, City pCity2)
    {
        if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity1) ||
            EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(pCity2)) return false;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(pCity1.GetTitleID());
        if (title == null)
        {
            title = ModClass.KINGDOM_TITLE_MANAGER.get(pCity2.GetTitleID());
        }
        bool result = false;
        if (title == null)
        {
            title = ModClass.KINGDOM_TITLE_MANAGER.newKingdomTitle(pCity1);
            if (title == null) return false;
            title.addCity(pCity2);
            result = true;
        }
        else
        {
            title.addCity(pCity1);
            title.addCity(pCity2);
        }
        return result;
    }


    public override void update(float pElapsed)
    {
        base.update(pElapsed);
        if (this.Count <= 0) return;
        using var frameWork = SimulationFrameBudget.Measure();
        _titleUpdateBuffer.Clear();
        bool forceFullUpdate = pElapsed < 0f;
        if (forceFullUpdate)
        {
            foreach (KingdomTitle title in this) _titleUpdateBuffer.Add(title);
        }
        else
        {
            checkLists();
            int population = World.world?.units?.Count ?? 0;
            int budget = EmpireCraftFrameSchedulingRules.ResolveMaximumTitles(
                ModClass.PERFORMANCE_HIGH_POPULATION_MODE,
                ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE, population,
                Math.Max(0d, Time.unscaledDeltaTime * 1000d));
            if (ModClass.PERFORMANCE_ADAPTIVE_THROUGHPUT_MODE)
            {
                int minimumThroughput = EmpireCraftFrameSchedulingRules.ResolveMinimumItemsForSweep(
                    this.list.Count, Math.Max(0d, Time.unscaledDeltaTime),
                    EmpireCraftFrameSchedulingRules.TitleSweepTargetSeconds, 96);
                budget = Math.Max(budget, minimumThroughput);
            }
            int count = Math.Min(this.list.Count, Math.Max(1, budget));
            for (int index = 0; index < count; index++)
            {
                if (_scheduledUpdateCursor >= this.list.Count) _scheduledUpdateCursor = 0;
                _titleUpdateBuffer.Add(this.list[(_scheduledUpdateCursor + index) % this.list.Count]);
            }
        }

        for (int index = 0; index < _titleUpdateBuffer.Count; index++)
        {
            if (!forceFullUpdate && !SimulationFrameBudget.HasTime) break;
            if (!forceFullUpdate) _scheduledUpdateCursor = (_scheduledUpdateCursor + 1) % this.list.Count;
            KingdomTitle title = _titleUpdateBuffer[index];
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.OwnsObject(title)) continue;
            // 法理默认文化不再由这里按年自动检查/转换：占比够高之后要靠"文治"派系发起
            // 文化转化决议来正式改变（见 TempFac_文化转化.cs），所以旧的按年计时器已移除。
            if (!title.checkActive() && !ModClass.KINGDOM_TITLE_FREEZE) this._to_dissolve.Add(title);
        }
        for (int index = 0; index < this._to_dissolve.Count; index++)
        {
            this.dissolveTitle(this._to_dissolve[index]);
        }
        this._to_dissolve.Clear();
        _titleUpdateBuffer.Clear();
    }

    public void dissolveTitle(KingdomTitle pkt)
    {
        pkt.generateColor();
        pkt.Dissolve();
        pkt.Dispose();
        this.removeObject(pkt);
    }

    public override void removeObject(KingdomTitle pObject)
    {
        base.removeObject(pObject);
    }

    private List<KingdomTitle> _to_dissolve = new List<KingdomTitle>();
}
