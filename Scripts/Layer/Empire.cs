using db;
using EmpireCraft.Scripts;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace EmpireCraft.Scripts.Layer;
// Token: 0x0200023B RID: 571
public class Empire : MetaObject<EmpireData>
{
    public Vector3 last_empire_center;
    public Vector3 empire_center;
    private readonly List<TileZone> _zoneScratch = new();

    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }
    public void createNewEmpire(Kingdom empire)
    {
        //以帝国名称命名
        LogService.LogInfo("帝国名称" + empire.name.Split(' ')[0] + " " + LM.Get("default_" + countryLevel.countrylevel_0.ToString()) + LM.Get("Country"));
        this.data.name = empire.name.Split(' ')[0] + " " + LM.Get("default_"+countryLevel.countrylevel_0.ToString()) + LM.Get("Country");
        this.empire = empire;
        empire.SetCountryLevel(countryLevel.countrylevel_0);
        this.generateNewMetaObject();
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

    // Token: 0x06001124 RID: 4388 RVA: 0x000C7748 File Offset: 0x000C5948
    public bool checkActive()
    {
        bool tChanged = false;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = tKingdoms.Count - 1; i >= 0; i--)
        {
            Kingdom tKingdom = tKingdoms[i];
            if (!tKingdom.isAlive())
            {
                this.leave(tKingdom, false);
                this.kingdoms_list.RemoveAt(i);
                tChanged = true;
            }
        }
        if (tChanged)
        {
            this.recalculate();
        }
        return this.kingdoms_list.Count >= 2;
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
            this.data.kingdoms.Add(tKingdom.id);
        }
        this.data.empire = this.empire.id;
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
        this.empire = World.world.kingdoms.get(pData.empire);
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
        pKingdom.updateColor(this.empire.getColor());
        if (pRecalc)
        {
            this.recalculate();
        }
        this.data.timestamp_member_joined = World.world.getCurWorldTime();
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
        yield break;
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
        ModClass.EMPIRE_MANAGER.removeObject(this);
        base.Dispose();
    }

    // Token: 0x040022FF RID: 8959
    public List<Kingdom> kingdoms_list = new List<Kingdom>();

    // Token: 0x04002300 RID: 8960
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();

    public Kingdom empire;

    // Token: 0x04002301 RID: 8961
    public int power;
}