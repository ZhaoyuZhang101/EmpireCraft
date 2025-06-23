using db;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.TipAndLog;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using UnityEngine;
namespace EmpireCraft.Scripts.Layer;
// Token: 0x0200023B RID: 571
public class Empire : MetaObject<EmpireData>
{
    public BannerAsset BannerAsset;
    public Vector3 last_empire_center;
    public Vector3 empire_center;
    private readonly List<TileZone> _zoneScratch = new();
    private readonly int avgCitiesPerKingdom = 3;
    public Clan empire_clan;
    public Actor emperor;
    public Vector3 capital_center;
    public City original_capital;
    public EmpireCraftMapMode map_mode = EmpireCraftMapMode.Empire;

    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }

    public override long getTotalDeaths()
    {
        long deaths = 0;
        foreach(Kingdom kingdom in kingdoms_hashset)
        {
            deaths += kingdom.getTotalDeaths();
        }
        return deaths;
    }

    public EmpirePeriod getEmpirePeriod()
    {
        int age = getAge();
        if (age <= 50)
            this.data.empirePeriod = EmpirePeriod.平和;
        else if (age <= 100)
            this.data.empirePeriod = EmpirePeriod.拓土扩业;
        else if (age <= 150)
            this.data.empirePeriod = EmpirePeriod.下降;
        else if (age <= 200)
            this.data.empirePeriod = EmpirePeriod.逐鹿群雄;
        else
            this.data.empirePeriod = EmpirePeriod.天命丧失;
        return this.data.empirePeriod;
    }

    public string GetEmpireName()
    {
        string[] nameParts = this.data.name.Split(' ');
        if (nameParts.Length <= 2) return nameParts[0];
        return nameParts[nameParts.Length-2];
    }

    public void setEmperor(Actor actor)
    {
        this.emperor = actor;
        create_year_name();
        TranslateHelper.LogNewEmperor(emperor, empire.capital, data.year_name);
    }

    public void EmperorLeft(Actor actor)
    {
        this.emperor = null;
    }

    public int getEmperorYear()
    {
        return Date.getYearsSince(this.data.newEmperor_timestamp) + 1;
    }

    public string getYearNameWithTime()
    {
        if (this.data.has_year_name)
        {
            if (this.empire.hasKing())
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return this.data.year_name + " " + getEmperorYear() + LM.Get("Year");
                }
            }
        }
        return "";
    }

    public void createNewEmpire(Kingdom empire)
    {
        if (ConfigData.speciesCulturePair != null) 
        {
            if (ConfigData.speciesCulturePair.TryGetValue(empire.getSpecies(), out string culture)) {
                if (culture == "Huaxia")
                {
                    LogService.LogInfo("可以显示年号");
                    this.data.has_year_name = true;
                }
            }
        }
        this.empire = empire;
        if (this.empire.getKingClan() != null) this.empire_clan = this.empire.getKingClan();
        else 
        { 
            Clan clan = new Clan();
            clan.newClan(this.empire.king, true);
            this.empire_clan = clan;
        }
        this.original_capital = empire.capital;
        this.data.banner_icon_id = empire.data.banner_icon_id;
        this.data.banner_background_id = empire.data.banner_background_id;
        empire.SetCountryLevel(countryLevel.countrylevel_0);
        this.data.timestamp_established_time = World.world.getCurWorldTime();
        this.capital_center = empire.capital.city_center;
        this.generateNewMetaObject();
        string empireName = empire.GetKingdomName();
        if (empire.king.HasTitle())
        {
            empireName = empire.king.GetTitle();
        }
        if (empire.getKingClan() != null)
        {
            if (empire.getKingClan().HasHistoryEmpire())
            {
                empireName = String.Join(" ", GetDir(empire.getKingClan().GetHistoryEmpirePos()), empire.getKingClan().GetHistoryEmpireName());
            }
        }
        SetEmpireName(empireName);
        create_year_name();
        TranslateHelper.LogNewEmperor(empire.king, empire.capital, data.year_name);
        empire.getKingClan().RecordHistoryEmpire(this);
    }
    public bool isAllowToMakeYearName ()
    {
        return this.data.has_year_name;
    }
    public bool hasYearName()
    {
        return this.data.year_name != null && this.data.year_name != "";
    }

    public void create_year_name()
    {
        this.data.year_name = YearName.generateName();
        this.data.newEmperor_timestamp = World.world.getCurWorldTime();
    }

    public string GetDir(Vector2 v)
    {
        float ax = Math.Abs(v.x);
        float ay = Math.Abs(v.y);
        if (ax > ay)
        {
            // 水平分量更大，向东或西
            return LM.Get(v.x < capital_center.x-5 ?"Eastern" : "Western");
        }
        else if (ay > ax)
        {
            // 垂直分量更大，向北或南
            return LM.Get(v.y > capital_center.y+5 ? "Northern" : "Southern");
        }
        else
        {
            // 两者相等或都为 0，判定为“中间”
            return LM.Get("Later");
        }
    }

    public void SetEmpireName(string name)
    {
        LogService.LogInfo($"当前成为帝国的种族{empire.species_id}");
        string culture = ConfigData.speciesCulturePair.TryGetValue(empire.getSpecies(), out var a) ? a : "default";
        this.data.name = name + " " + LM.Get($"{culture}_" + countryLevel.countrylevel_0.ToString());
    }

    public void checkDisolve(Kingdom mainKingdom)
    {
        LogService.LogInfo("开始检测是否解散帝国");
        this.kingdoms_hashset.Remove(mainKingdom);
        mainKingdom.empireLeave(false);
        Kingdom heirEmpire = null;
        if (empire_clan.isAlive())
        {
            foreach (Kingdom kingdom in kingdoms_hashset)
            {
                if (kingdom.getKingClan()!= null)
                    if (kingdom.getKingClan().getID() == empire_clan.getID())
                    {
                        if (heirEmpire == null || kingdom.countTotalWarriors() > heirEmpire.countTotalWarriors())
                        {
                            heirEmpire = kingdom;
                        }
                    }
            }
        }
        if (heirEmpire == null)
        {
            LogService.LogInfo("解散帝国");
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
            return;
        }
        recalculate();
        LogService.LogInfo("继承帝国");
        replaceEmpire(heirEmpire);
        return;
    }

    public Kingdom GetMostPowerfulKingdom()
    {
        Kingdom kingdom = null;
        foreach(Kingdom k in kingdoms_hashset)
        {
            if (kingdom == null)
            {
                kingdom = k;
            }
            if (k.countTotalWarriors() >= kingdom.countTotalWarriors())
            {
                kingdom = k;
            }
        }
        return kingdom;
    }

    public void replaceEmpire(Kingdom newKingdom)
    {
        Empire newEmpire = ModClass.EMPIRE_MANAGER.newEmpire(newKingdom);
        newEmpire.SetEmpireName(newKingdom.GetKingdomName());
        if (newKingdom.capital.HasKingdomName()) 
        {
            SetEmpireName(newKingdom.capital.SelectKingdomName());
        }
        if (newKingdom.getKingClan().HasHistoryEmpire())
        {
            string empireName = String.Join(" ", newEmpire.GetDir(newKingdom.getKingClan().GetHistoryEmpirePos()), newKingdom.getKingClan().GetHistoryEmpireName());
            newEmpire.SetEmpireName(empireName);
        }
        if (newKingdom.king.HasTitle())
        {
            newEmpire.SetEmpireName(newKingdom.king.GetTitle());
        }
        if (newKingdom.getKingClan() == empire_clan)
        {
            newEmpire.SetEmpireName(newEmpire.GetDir(newKingdom.capital.city_center) + " " + GetEmpireName());
        }
        if (newKingdom.king.hasClan())
        {
            newKingdom.getKingClan().RecordHistoryEmpire(this);
            newEmpire.empire_clan = newKingdom.getKingClan();
        } 
        else
        {
            Clan clan = World.world.clans.newClan(newKingdom.king, true);
            newEmpire.empire_clan = clan;

        }
        TranslateHelper.LogMinistorAqcuireEmpire(newKingdom.king, newEmpire);
        foreach (Kingdom kingdom in kingdoms_hashset)
        {
            if (kingdom!=newKingdom&&(kingdom.GetCountryLevel()==countryLevel.countrylevel_0|| kingdom.GetCountryLevel() == countryLevel.countrylevel_1))
            {
                kingdom.SetCountryLevel(countryLevel.countrylevel_4);
            }
            newEmpire.kingdoms_hashset.Add(kingdom);
            kingdom.empireJoin(newEmpire);
            newEmpire.data.timestamp_member_joined = World.world.getCurWorldTime();
            
        }
        newEmpire.create_year_name();
        newEmpire.recalculate();
        TranslateHelper.LogNewEmperor(newKingdom.king, newKingdom.capital, newEmpire.data.year_name);

        this.kingdoms_hashset.Clear();
        Dispose();
    }
    public sealed override void setDefaultValues()
    {
        base.setDefaultValues();
        this.power = 0;
    }
    public override int countTotalMoney()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countTotalMoney();
        }
        return tResult;
    }
    public override int countHappyUnits()
    {
        if (this.kingdoms_list.Count == 0)
        {
            return 0;
        }
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countHappyUnits();
        }
        return tResult;
    }
    public override int countSick()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countSick();
        }
        return tResult;
    }
    public override int countHungry()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countHungry();
        }
        return tResult;
    }
    public override int countStarving()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countStarving();
        }
        return tResult;
    }
    public override int countChildren()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countChildren();
        }
        return tResult;
    }
    public override int countAdults()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countAdults();
        }
        return tResult;
    }
    public override int countHomeless()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countHomeless();
        }
        return tResult;
    }
    public override IEnumerable<Family> getFamilies()
    {
        List<Kingdom> tKingdoms = this.kingdoms_list;
        int num;
        for (int i = 0; i < tKingdoms.Count; i = num + 1)
        {
            Kingdom tKingdom = tKingdoms[i];
            foreach (Family tFamily in tKingdom.getFamilies())
            {
                yield return tFamily;
            }
            IEnumerator<Family> enumerator = null;
            num = i;
        }
        yield break;
    }

    public override bool hasFamilies()
    {
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            if (tKingdoms[i].hasFamilies())
            {
                return true;
            }
        }
        return false;
    }

    // Token: 0x0600111A RID: 4378 RVA: 0x000C753C File Offset: 0x000C573C
    public override int countMales()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countMales();
        }
        return tResult;
    }

    // Token: 0x0600111B RID: 4379 RVA: 0x000C7578 File Offset: 0x000C5778
    public override int countFemales()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countFemales();
        }
        return tResult;
    }

    // Token: 0x0600111C RID: 4380 RVA: 0x000C75B4 File Offset: 0x000C57B4
    public override int countHoused()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countHoused();
        }
        return tResult;
    }
    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }

    public override void generateBanner()
    {
        Sprite[] tBgs = World.world.alliances.getBackgroundsList();
        this.data.banner_background_id = Randy.randomInt(0, tBgs.Length);
        Sprite[] tIcons = World.world.alliances.getIconsList();
        this.data.banner_icon_id = Randy.randomInt(0, tIcons.Length);
    }

    public void addFounder(Kingdom pKingdom)
    {
        this.data.founder_kingdom_name = pKingdom.data.name;
        this.data.founder_kingdom_id = pKingdom.getID();
        EmpireData data = this.data;
        Actor king = pKingdom.king;
        data.founder_actor_name = ((king != null) ? king.getName() : null);
        data.founder_actor_id = ((king != null) ? king.getID() : -1L);
        this.join(pKingdom, true, true);
    }

    public void update()
    {
        this.power = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            this.power += tKingdom.power;
        }
    }

    public bool checkActive()
    {
        bool tChanged = false;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        if (tKingdoms.Count<=0)
        {
            return false;
        }
        List<Kingdom> remove_tKingdoms = new List<Kingdom>();
        foreach( Kingdom k in tKingdoms )
        {
            if (!k.isAlive()||k==null)
            {
                remove_tKingdoms.Add(k);
                tChanged = true;
            }
        }
        foreach (Kingdom k in remove_tKingdoms)
        {
            this.leave(k, false);
            this.kingdoms_list.Remove(k);
        }
        if (tChanged)
        {
            this.recalculate();
        }
        return this.kingdoms_list.Count >= 1;
    }

    // Token: 0x06001125 RID: 4389 RVA: 0x000C77B4 File Offset: 0x000C59B4
    public void dissolve()
    {
        foreach (Kingdom kingdom in this.kingdoms_hashset)
        {
            kingdom.empireLeave();
        }
        this.kingdoms_hashset.Clear();

    }

    // Token: 0x06001126 RID: 4390 RVA: 0x000C7810 File Offset: 0x000C5A10
    public void recalculate()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_list.AddRange(this.kingdoms_hashset);
        this.mergeWars();
    }

    // Token: 0x06001127 RID: 4391 RVA: 0x000C7834 File Offset: 0x000C5A34
    public bool canJoin(Kingdom pKingdom)
    {
        if (!pKingdom.isOpinionTowardsKingdomGood(empire))
        {
            return false;
        }
        return true;
    }
    public override void save()
    {
        if (this.data == null)
        {
            return;
        }
        LogService.LogInfo("存储数据中" + this.name + this.id);
        this.data.kingdoms = new List<long>();
        foreach (Kingdom tKingdom in this.kingdoms_hashset)
        {
            if (tKingdom!=null)
            {
                this.data.kingdoms.Add(tKingdom.id);
            }
        }
        this.data.emperor = this.emperor.data.id;
        this.data.empire = this.empire.data.id;
        this.data.original_capital = this.original_capital.isAlive() ? this.original_capital.data.id : -1L;
        this.data.empire_clan = this.empire_clan==null?-1L:this.empire_clan.data.id;
    }

    // Token: 0x0600112B RID: 4395 RVA: 0x000C7CCC File Offset: 0x000C5ECC
    public override void loadData(EmpireData pData)
    {
        base.loadData(pData);
        foreach (long tKingdomID in this.data.kingdoms)
        {
            Kingdom tKingdom = World.world.kingdoms.get(tKingdomID);
            if (tKingdom != null)
            {
                this.kingdoms_hashset.Add(tKingdom);
            }
        }
        this.emperor = World.world.units.get(pData.emperor);
        this.empire = World.world.kingdoms.get(pData.empire);
        this.empire_clan = World.world.clans.get(pData.empire_clan);
        this.original_capital = World.world.cities.get(pData.original_capital);
        this.recalculate();
    }

    // Token: 0x06001128 RID: 4392 RVA: 0x000C7890 File Offset: 0x000C5A90
    public bool join(Kingdom pKingdom, bool pRecalc = true, bool pForce = false)
    {
        if (this.hasKingdom(pKingdom))
        {
            return false;
        }
        if (!pForce && !this.canJoin(pKingdom))
        {
            return false;
        }
        this.kingdoms_hashset.Add(pKingdom);
        if (this.hasWars())
        {
            if (this.hasWarsWith(pKingdom))
            {
                foreach (War tWar in this.getAttackerWars())
                {
                    if (tWar.isDefender(pKingdom))
                    {
                        tWar.leaveWar(pKingdom);
                    }
                }
                foreach (War tWar2 in this.getDefenderWars())
                {
                    if (tWar2.isAttacker(pKingdom))
                    {
                        tWar2.leaveWar(pKingdom);
                    }
                }
            }
            foreach (War war in this.getAttackerWars())
            {
                war.joinAttackers(pKingdom);
            }
            foreach (War tWar3 in this.getDefenderWars())
            {
                if (!tWar3.isTotalWar())
                {
                    tWar3.joinDefenders(pKingdom);
                }
            }
        }
        if (pKingdom.hasEnemies())
        {
            foreach (War tWar4 in pKingdom.getWars(false))
            {
                if (!tWar4.isTotalWar())
                {
                    if (tWar4.main_attacker == pKingdom)
                    {
                        foreach (Kingdom tKingdom in this.kingdoms_list)
                        {
                            tWar4.joinAttackers(tKingdom);
                        }
                    }
                    if (tWar4.main_defender == pKingdom)
                    {
                        foreach (Kingdom tKingdom2 in this.kingdoms_list)
                        {
                            tWar4.joinDefenders(tKingdom2);
                        }
                    }
                }
            }
        }
        pKingdom.empireJoin(this);
        if (pRecalc)
        {
            this.recalculate();
        }
        this.data.timestamp_member_joined = World.world.getCurWorldTime();
        pKingdom.SetLoyalty(999);
        return true;
    }

    // Token: 0x06001129 RID: 4393 RVA: 0x000C7B00 File Offset: 0x000C5D00
    public void leave(Kingdom pKingdom, bool pRecalc = true)
    {
        this.kingdoms_hashset.Remove(pKingdom);
        
        if (this.hasWars())
        {
            foreach (War tWar in this.getAttackerWars())
            {
                if (tWar.main_attacker != pKingdom)
                {
                    tWar.leaveWar(pKingdom);
                }
                else
                {
                    foreach (Kingdom tKingdom in this.kingdoms_hashset)
                    {
                        tWar.leaveWar(tKingdom);
                    }
                }
            }
            foreach (War tWar2 in this.getDefenderWars())
            {
                if (tWar2.main_defender != pKingdom)
                {
                    tWar2.leaveWar(pKingdom);
                }
                else
                {
                    foreach (Kingdom tKingdom2 in this.kingdoms_hashset)
                    {
                        tWar2.leaveWar(tKingdom2);
                    }
                }
            }
        }

        pKingdom.empireLeave();
        if (countKingdoms()<=0)
        {
            dissolve();
            Dispose();
        }
        if (pRecalc)
        {
            this.recalculate();
        }
    }

    // Token: 0x0600112C RID: 4396 RVA: 0x000C7D4C File Offset: 0x000C5F4C
    public int countBuildings()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countBuildings();
        }
        return tResult;
    }


    public int countZones()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countZones();
        }
        return tResult;
    }

    public List<TileZone> allZones()
    {
        _zoneScratch.Clear();
        foreach (var k in kingdoms_list)
            if (k.cities.Count>0)
                foreach (var city in k.cities)
                    _zoneScratch.AddRange(city.zones);
        return _zoneScratch;
    }

    public override int countUnits()
    {
        return this.countPopulation();
    }


    public int countPopulation()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.getPopulationPeople();
        }
        return tResult;
    }


    public int countCities()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countCities();
        }
        return tResult;
    }

    // Token: 0x06001131 RID: 4401 RVA: 0x000C7E41 File Offset: 0x000C6041
    public int countKingdoms()
    {
        return this.kingdoms_hashset.Count;
    }

    // Token: 0x06001132 RID: 4402 RVA: 0x000C7E50 File Offset: 0x000C6050
    public string getMotto()
    {
        if (string.IsNullOrEmpty(this.data.motto))
        {
            this.data.motto = NameGenerator.getName("alliance_mottos", ActorSex.Male, false, null, null, false);
        }
        return this.data.motto;
    }

    // Token: 0x06001133 RID: 4403 RVA: 0x000C7E9C File Offset: 0x000C609C
    public int countWarriors()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult += tKingdom.countTotalWarriors();
        }
        return tResult;
    }
    public void calculateDoesHaveTitle ()
    {
        foreach(KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER)
        {
            var title_cities = title.city_list;
        }
    }
    // Token: 0x06001134 RID: 4404 RVA: 0x000C7ED5 File Offset: 0x000C60D5
    public static bool isSame(Alliance pAlliance1, Alliance pAlliance2)
    {
        return pAlliance1 != null && pAlliance2 != null && pAlliance1 == pAlliance2;
    }

    // Token: 0x06001135 RID: 4405 RVA: 0x000C7EE4 File Offset: 0x000C60E4
    public bool hasWarsWith(Kingdom pKingdom)
    {
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tAllianceKingdom = tKingdoms[i];
            if (pKingdom.isInWarWith(tAllianceKingdom))
            {
                return true;
            }
        }
        return false;
    }

    // Token: 0x06001136 RID: 4406 RVA: 0x000C7F1D File Offset: 0x000C611D
    public bool hasSupremeKingdom()
    {
        return DiplomacyManager.kingdom_supreme != null && this.hasKingdom(DiplomacyManager.kingdom_supreme);
    }

    // Token: 0x06001137 RID: 4407 RVA: 0x000C7F33 File Offset: 0x000C6133
    public bool hasKingdom(Kingdom pKingdom)
    {
        return this.kingdoms_hashset.Contains(pKingdom);
    }

    // Token: 0x06001138 RID: 4408 RVA: 0x000C7F44 File Offset: 0x000C6144
    public bool hasSharedBordersWithKingdom(Kingdom pKingdom)
    {
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            if (DiplomacyHelpers.areKingdomsClose(pKingdom, tKingdom))
            {
                return true;
            }
        }
        return false;
    }

    // Token: 0x06001139 RID: 4409 RVA: 0x000C7F7D File Offset: 0x000C617D
    public bool hasWars()
    {
        return World.world.wars.hasWars(this.empire);
    }

    // Token: 0x0600113A RID: 4410 RVA: 0x000C7F8F File Offset: 0x000C618F
    public IEnumerable<War> getWars(bool pRandom = false)
    {
        return World.world.wars.getWars(this.empire, pRandom);
    }

    // Token: 0x0600113B RID: 4411 RVA: 0x000C7FA4 File Offset: 0x000C61A4
    public void mergeWars()
    {
        if (!this.hasWars())
        {
            return;
        }
        using (ListPool<War> tWars = new ListPool<War>(this.getWars(false)))
        {
            for (int i = 0; i < tWars.Count; i++)
            {
                War tWar = tWars[i];
                if (!tWar.hasEnded())
                {
                    for (int j = i + 1; j < tWars.Count; j++)
                    {
                        War tWar2 = tWars[j];
                        if (!tWar2.hasEnded() && tWar.isSameAs(tWar2))
                        {
                            if (tWar.data.created_time < tWar2.data.created_time)
                            {
                                World.world.wars.endWar(tWar2, WarWinner.Merged);
                            }
                            else
                            {
                                World.world.wars.endWar(tWar, WarWinner.Merged);
                            }
                            this.mergeWars();
                            return;
                        }
                    }
                }
            }
        }
    }

    public Vector3 GetEmpireCenter()
    {
        if (!this._units_dirty)
            return this.last_empire_center;

        if (this.countZones()<=0)
        {
            this.empire_center = Globals.POINT_IN_VOID_2;
            return this.empire_center;
        }
        float num = 0f;
        float num2 = 0f;
        float num3 = float.MaxValue;
        TileZone tileZone = null;
        var zones = this.allZones();
        for (int i = 0; i < zones.Count; i++)
        {
            TileZone tileZone2 = zones[i];
            num += tileZone2.centerTile.posV3.x;
            num2 += tileZone2.centerTile.posV3.y;
        }
        this.empire_center.x = num / (float)zones.Count;
        this.empire_center.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            TileZone tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.centerTile.x, (float)tileZone3.centerTile.y, this.empire_center.x, this.empire_center.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this.empire_center.x = tileZone.centerTile.posV3.x;
        this.empire_center.y = tileZone.centerTile.posV3.y + 2f;
        this.last_empire_center = this.empire_center;
        this._units_dirty = false;
        return this.last_empire_center;
    }

    // Token: 0x0600113C RID: 4412 RVA: 0x000C8080 File Offset: 0x000C6280
    public IEnumerable<War> getAttackerWars()
    {
        foreach (War tWar in this.getWars(false))
        {
            foreach (Kingdom tKingdom in this.kingdoms_list)
            {
                if (tWar.isAttacker(tKingdom))
                {
                    yield return tWar;
                    break;
                }
            }
            List<Kingdom>.Enumerator enumerator2 = default(List<Kingdom>.Enumerator);
        }
        IEnumerator<War> enumerator = null;
        yield break;
    }

    // Token: 0x0600113D RID: 4413 RVA: 0x000C8090 File Offset: 0x000C6290
    public IEnumerable<War> getDefenderWars()
    {
        foreach (War tWar in this.getWars(false))
        {
            foreach (Kingdom tKingdom in this.kingdoms_list)
            {
                if (tWar.isDefender(tKingdom))
                {
                    yield return tWar;
                    break;
                }
            }
            List<Kingdom>.Enumerator enumerator2 = default(List<Kingdom>.Enumerator);
        }
        IEnumerator<War> enumerator = null;
        yield break;
    }

    // Token: 0x0600113E RID: 4414 RVA: 0x000C80A0 File Offset: 0x000C62A0
    public override IEnumerable<Actor> getUnits()
    {
        List<Kingdom> tKingdoms = this.kingdoms_list;
        int num;
        for (int i = 0; i < tKingdoms.Count; i = num + 1)
        {
            Kingdom tKingdom = tKingdoms[i];
            foreach (Actor tActor in tKingdom.getUnits())
            {
                yield return tActor;
            }
            IEnumerator<Actor> enumerator = null;
            num = i;
        }
        yield break;
    }

    public void AutoEnfeoff()
    {
        var allCities = this.empire.cities;
        if (allCities == null)
        {
            return;
        }
        if (allCities.Count == 0) return;
        var unassigned = new HashSet<City>(allCities);
        while (unassigned.Count > 0)
        {
            // 3.1 随机取一个种子城市
            var seed = unassigned.First();
            var region = new List<City> { seed };
            unassigned.Remove(seed);

            // 3.2 用队列做 BFS，一直扩张到 avgSize 或者没有相邻新城
            var queue = new Queue<City>();
            queue.Enqueue(seed);

            while (queue.Count > 0 && region.Count < avgCitiesPerKingdom)
            {
                var curr = queue.Dequeue();
                // neighbours_cities(curr) 返回所有与 curr 直接相邻的城市
                foreach (var nei in curr.neighbours_cities)
                {
                    if (unassigned.Contains(nei))
                    {
                        region.Add(nei);
                        unassigned.Remove(nei);
                        queue.Enqueue(nei);
                        if (region.Count >= avgCitiesPerKingdom) break;
                    }
                }
            }
            region = region.FindAll(c => c.getID() != empire.capital.getID());
            if (region.Count > 0)
            {
                City capital = region.GetRandom();
                List<Actor> SatisfiedCandidates = new List<Actor>();
                if (empire.getKingClan()!=null)
                {
                    var RoyalCandidates = empire.getKingClan().getUnits();
                    SatisfiedCandidates = RoyalCandidates.TakeWhile(c => c.isActor() && c.isAlive() && c.isAdult() && c.getID() != empire.getID() && !c.isKing()).ToList();
                }
                else
                {
                    SatisfiedCandidates = new List<Actor>();
                }

                Kingdom newKingdom;
                Actor king;
                countryLevel cl = countryLevel.countrylevel_1;
                PeeragesLevel pl = PeeragesLevel.peerages_1;
                if (SatisfiedCandidates.Count() > 0)
                {
                    king = SatisfiedCandidates.First();
                    if (empire.king.getChildren().Contains(king))
                    {
                        cl = countryLevel.countrylevel_1;
                        pl = PeeragesLevel.peerages_1;
                    }
                    else
                    {
                        cl = countryLevel.countrylevel_2;
                        pl = PeeragesLevel.peerages_2;
                    }
                }
                else
                {
                    king = capital.hasLeader()?capital.leader:capital.getUnits().FirstOrDefault();
                    cl = countryLevel.countrylevel_4;
                    pl = PeeragesLevel.peerages_4;
                }
                newKingdom = setEnfeoff(capital, king);
                foreach (var city in region)
                {
                    city.joinAnotherKingdom(newKingdom);
                }
                newKingdom.setCapital(capital);
                newKingdom.data.name = capital.data.name;
                LogService.LogInfo($"{capital.GetCityName()}");
                newKingdom.SetCountryLevel(cl);
                newKingdom.SetFiedTimestamp(World.world.getCurWorldTime());
                king.SetPeeragesLevel(pl);
                new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_enfeoff_log, this.name)
                {
                    location = this.empire.location,
                    color_special1 = this.empire.kingdomColor.getColorText()
                }.add();
                this.join(newKingdom, true, false);
                WorldLog.logNewKingdom(newKingdom);
            }
        }
        
    }



    public Kingdom setEnfeoff(City capital, Actor king)
    {
        Kingdom pKingdom = capital.kingdom;
        capital.removeFromCurrentKingdom();
        capital.removeLeader();
        Kingdom kingdom = World.world.kingdoms.makeNewCivKingdom(king, pLog:false);
        capital.newForceKingdomEvent(base.units, capital._boats, kingdom, null);
        capital.setKingdom(kingdom);
        capital.switchedKingdom();
        kingdom.copyMetasFromOtherKingdom(pKingdom);
        kingdom.setCityMetas(capital);
        return kingdom;
    }

    public void SelectAndInspect()
    {
        ConfigData.CURRENT_SELECTED_EMPIRE = this;
        ScrollWindow.showWindow(nameof(EmpireWindow));
    }

    // Token: 0x0600113F RID: 4415 RVA: 0x000C80B0 File Offset: 0x000C62B0
    public override Actor getRandomUnit()
    {
        return this.kingdoms_list.GetRandom<Kingdom>().getRandomUnit();
    }

    // Token: 0x06001140 RID: 4416 RVA: 0x000C80C2 File Offset: 0x000C62C2
    public Sprite getBackgroundSprite()
    {
        return World.world.alliances.getBackgroundsList()[this.data.banner_background_id];
    }

    // Token: 0x06001141 RID: 4417 RVA: 0x000C80DF File Offset: 0x000C62DF
    public Sprite getIconSprite()
    {
        return empire.getSpriteIcon();
    }

    // Token: 0x06001142 RID: 4418 RVA: 0x000C80FC File Offset: 0x000C62FC
    public override void Dispose()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_hashset.Clear();
        this.empire = null;
    }

    // Token: 0x040022FF RID: 8959
    public List<Kingdom> kingdoms_list = new List<Kingdom>();

    // Token: 0x04002300 RID: 8960
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();

    public Kingdom empire;

    // Token: 0x04002301 RID: 8961
    public int power;
}