using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using System;
using System.Collections.Generic;
using UnityEngine;

// Token: 0x0200023B RID: 571
public class Province : MetaObject<ProvinceData>
{
    // Fix for CS0507: Change the access modifier of the overridden property to match the base class.  
    public override MetaType meta_type => MetaType.Alliance;

    // Token: 0x0600110E RID: 4366 RVA: 0x000C72C4 File Offset: 0x000C54C4
    public void createNewProvince(City pFoundCity)
    {

        string cityOriginalName = pFoundCity.name.Split(' ')[0];
        this.generateNewMetaObject();
    }


    // Token: 0x06001110 RID: 4368 RVA: 0x000C7308 File Offset: 0x000C5508
    public override int countTotalMoney()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countTotalMoney();
        }
        return tResult;
    }

    // Token: 0x06001111 RID: 4369 RVA: 0x000C7344 File Offset: 0x000C5544
    public override int countHappyUnits()
    {
        if (this.cities_list.Count == 0)
        {
            return 0;
        }
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countHappyUnits();
        }
        return tResult;
    }

    // Token: 0x06001112 RID: 4370 RVA: 0x000C738C File Offset: 0x000C558C
    public override int countSick()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countSick();
        }
        return tResult;
    }

    // Token: 0x06001113 RID: 4371 RVA: 0x000C73C8 File Offset: 0x000C55C8
    public override int countHungry()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countHungry();
        }
        return tResult;
    }

    // Token: 0x06001114 RID: 4372 RVA: 0x000C7404 File Offset: 0x000C5604
    public override int countStarving()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countStarving();
        }
        return tResult;
    }

    // Token: 0x06001115 RID: 4373 RVA: 0x000C7440 File Offset: 0x000C5640
    public override int countChildren()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countChildren();
        }
        return tResult;
    }

    // Token: 0x06001116 RID: 4374 RVA: 0x000C747C File Offset: 0x000C567C
    public override int countAdults()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countAdults();
        }
        return tResult;
    }

    // Token: 0x06001117 RID: 4375 RVA: 0x000C74B8 File Offset: 0x000C56B8
    public override int countHomeless()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countHomeless();
        }
        return tResult;
    }

    // Token: 0x06001118 RID: 4376 RVA: 0x000C74F1 File Offset: 0x000C56F1
    public override IEnumerable<Family> getFamilies()
    {
        List<City> tCities = this.cities_list;
        int num;
        for (int i = 0; i < tCities.Count; i = num + 1)
        {
            City tCity = tCities[i];
            foreach (Family tFamily in tCity.getFamilies())
            {
                yield return tFamily;
            }
            IEnumerator<Family> enumerator = null;
            num = i;
        }
        yield break;
        yield break;
    }

    // Token: 0x06001119 RID: 4377 RVA: 0x000C7504 File Offset: 0x000C5704
    public override bool hasFamilies()
    {
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            if (tCities[i].hasFamilies())
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
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countMales();
        }
        return tResult;
    }

    // Token: 0x0600111B RID: 4379 RVA: 0x000C7578 File Offset: 0x000C5778
    public override int countFemales()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countFemales();
        }
        return tResult;
    }

    // Token: 0x0600111C RID: 4380 RVA: 0x000C75B4 File Offset: 0x000C57B4
    public override int countHoused()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countHoused();
        }
        return tResult;
    }

    // Token: 0x0600111D RID: 4381 RVA: 0x000C75ED File Offset: 0x000C57ED
    public void setLevel(countryLevel pLevel)
    {
        this.data.province_level = pLevel;
    }

    // Token: 0x0600111E RID: 4382 RVA: 0x000C75FB File Offset: 0x000C57FB
    public bool isCountLevel()
    {
        return this.data.province_level == countryLevel.countrylevel_count;
    }

    // Token: 0x0600111F RID: 4383 RVA: 0x000C760B File Offset: 0x000C580B
    public bool isPrinceLevel()
    {
        return this.data.province_level == countryLevel.countrylevel_prince;
    }

    public bool isKingLevel()
    {
        return this.data.province_level == countryLevel.countrylevel_king;
    }
    public bool isDukeLevel()
    {
        return this.data.province_level == countryLevel.countrylevel_duke;
    }
    public bool isMarquisLevel()
    {
        return this.data.province_level == countryLevel.countrylevel_marquis;
    }

    // Token: 0x06001120 RID: 4384 RVA: 0x000C761B File Offset: 0x000C581B
    public override ColorLibrary getColorLibrary()
    {
        return AssetManager.kingdom_colors_library;
    }

    // Token: 0x06001121 RID: 4385 RVA: 0x000C7624 File Offset: 0x000C5824
    public override void generateBanner()
    {
        Sprite[] tBgs = World.world.alliances.getBackgroundsList();
        this.data.banner_background_id = Randy.randomInt(0, tBgs.Length);
        Sprite[] tIcons = World.world.alliances.getIconsList();
        this.data.banner_icon_id = Randy.randomInt(0, tIcons.Length);
    }

    // Token: 0x06001122 RID: 4386 RVA: 0x000C767C File Offset: 0x000C587C
    public void addFounder(Kingdom pKingdom)
    {
        this.data.founder_kingdom_name = pKingdom.data.name;
        this.data.founder_kingdom_id = pKingdom.getID();
        ProvinceData data = this.data;
        Actor king = pKingdom.king;
        data.founder_actor_name = ((king != null) ? king.getName() : null);
    }

    // Token: 0x06001123 RID: 4387 RVA: 0x000C7700 File Offset: 0x000C5900
    public void update()
    {
        this.money = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            this.money += tCity.countTotalMoney();
        }
    }

    // Token: 0x06001124 RID: 4388 RVA: 0x000C7748 File Offset: 0x000C5948
    public bool checkActive()
    {
        bool tChanged = false;
        List<City> tCities = this.cities_list;
        for (int i = tCities.Count - 1; i >= 0; i--)
        {
            City tCity = tCities[i];
            if (!tCity.isAlive())
            {
                this.leave(tCity, false);
                this.cities_list.RemoveAt(i);
                tChanged = true;
            }
        }
        if (tChanged)
        {
            this.recalculate();
        }
        return this.cities_list.Count >= 2;
    }

    // Token: 0x06001125 RID: 4389 RVA: 0x000C77B4 File Offset: 0x000C59B4
    public void dissolve()
    {
        foreach (City city in this.cities_hashset)
        {
            city.SetProvince(null);
        }
        this.cities_hashset.Clear();
    }

    // Token: 0x06001126 RID: 4390 RVA: 0x000C7810 File Offset: 0x000C5A10
    public void recalculate()
    {
        this.cities_list.Clear();
        this.cities_list.AddRange(this.cities_hashset);
    }

    // Token: 0x06001128 RID: 4392 RVA: 0x000C7890 File Offset: 0x000C5A90
    public bool join(City pCity, bool pRecalc = true)
    {
        if (this.hasCity(pCity))
        {
            return false;
        }
        this.cities_hashset.Add(pCity);
        pCity.SetProvince(this.data);
        if (pRecalc)
        {
            this.recalculate();
        }
        this.data.timestamp_member_joined = World.world.getCurWorldTime();
        return true;
    }

    // Token: 0x06001129 RID: 4393 RVA: 0x000C7B00 File Offset: 0x000C5D00
    public void leave(City pCity, bool pRecalc = true)
    {
        this.cities_hashset.Remove(pCity);
        pCity.SetProvince(null);
    }

    // Token: 0x0600112A RID: 4394 RVA: 0x000C7C54 File Offset: 0x000C5E54
    public override void save()
    {
        base.save();
        this.data.cities = new List<long>();
        foreach (City tCity in this.cities_hashset)
        {
            this.data.cities.Add(tCity.id);
        }
    }

    // Token: 0x0600112B RID: 4395 RVA: 0x000C7CCC File Offset: 0x000C5ECC
    public override void loadData(ProvinceData pData)
    {
        base.loadData(pData);
        foreach (long tCityID in this.data.cities)
        {
            City tCity = World.world.cities.get(tCityID);
            if (tCity != null)
            {
                this.cities_hashset.Add(tCity);
            }
        }
        this.recalculate();
    }

    // Token: 0x0600112C RID: 4396 RVA: 0x000C7D4C File Offset: 0x000C5F4C
    public int countBuildings()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countBuildings();
        }
        return tResult;
    }

    // Token: 0x0600112D RID: 4397 RVA: 0x000C7D88 File Offset: 0x000C5F88
    public int countZones()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countZones();
        }
        return tResult;
    }

    // Token: 0x0600112E RID: 4398 RVA: 0x000C7DC1 File Offset: 0x000C5FC1
    public override int countUnits()
    {
        return this.countPopulation();
    }

    // Token: 0x0600112F RID: 4399 RVA: 0x000C7DCC File Offset: 0x000C5FCC
    public int countPopulation()
    {
        int tResult = 0;
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.getPopulationPeople();
        }
        return tResult;
    }
    public int countCities()
    {
        return this.cities_list.Count;
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
        List<City> tCities = this.cities_list;
        for (int i = 0; i < tCities.Count; i++)
        {
            City tCity = tCities[i];
            tResult += tCity.countWarriors();
        }
        return tResult;
    }

    // Token: 0x06001134 RID: 4404 RVA: 0x000C7ED5 File Offset: 0x000C60D5
    public static bool isSame(Province pAlliance1, Province pAlliance2)
    {
        return pAlliance1 != null && pAlliance2 != null && pAlliance1 == pAlliance2;
    }

    // Token: 0x06001137 RID: 4407 RVA: 0x000C7F33 File Offset: 0x000C6133
    public bool hasCity(City pCity)
    {
        return this.cities_hashset.Contains(pCity);
    }


    // Token: 0x0600113E RID: 4414 RVA: 0x000C80A0 File Offset: 0x000C62A0
    public override IEnumerable<Actor> getUnits()
    {
        List<City> tCities = this.cities_list;
        int num;
        for (int i = 0; i < tCities.Count; i = num + 1)
        {
            City tCity = tCities[i];
            foreach (Actor tActor in tCity.getUnits())
            {
                yield return tActor;
            }
            IEnumerator<Actor> enumerator = null;
            num = i;
        }
        yield break;
    }

    // Token: 0x0600113F RID: 4415 RVA: 0x000C80B0 File Offset: 0x000C62B0
    public override Actor getRandomUnit()
    {
        return this.cities_list.GetRandom<City>().getRandomUnit();
    }

    // Token: 0x06001140 RID: 4416 RVA: 0x000C80C2 File Offset: 0x000C62C2
    public Sprite getBackgroundSprite()
    {
        return World.world.alliances.getBackgroundsList()[this.data.banner_background_id];
    }

    // Token: 0x06001141 RID: 4417 RVA: 0x000C80DF File Offset: 0x000C62DF
    public Sprite getIconSprite()
    {
        return World.world.alliances.getIconsList()[this.data.banner_icon_id];
    }

    // Token: 0x06001142 RID: 4418 RVA: 0x000C80FC File Offset: 0x000C62FC
    public override void Dispose()
    {
        DBInserter.deleteData(this.getID(), "alliance");
        this.cities_list.Clear();
        this.cities_hashset.Clear();
        base.Dispose();
    }

    // Token: 0x040022FF RID: 8959
    public List<City> cities_list = new List<City>();

    // Token: 0x04002300 RID: 8960
    public HashSet<City> cities_hashset = new HashSet<City>();

    // Token: 0x04002301 RID: 8961
    public int money;
}
