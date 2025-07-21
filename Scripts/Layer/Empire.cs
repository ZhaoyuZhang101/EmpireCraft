using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
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
    public Actor Heir;
    public Vector3 capital_center;
    public City original_capital;
    public EmpireCraftMapMode map_mode = EmpireCraftMapMode.Empire;
    public List<Province> province_list = new List<Province>();

    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }

    public bool isNeedToExam()
    {
        if (data.last_exam_timestamp == -1L) 
        {
            return true;
        } else
        {
            if (Date.getYearsSince(data.last_exam_timestamp)>=4)
            {
                return true;
            }
        }
        return false;
    }

    public new void addRenown(int value)
    {
        this.empire.data.renown += value;
        if (this.empire.data.renown<=0)
        {
            this.empire.data.renown = 0;
        }
    }

    public bool isNeedToOfficeExam()
    {
        if (data.last_exam_timestamp == -1L) return true;
        if (Date.getYearsSince(data.last_office_exam_timestamp)>=1)
        {
            return true;
        }
        return false;
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

    public void StartCalcOfficePerformance()
    {
        if (isNeedToOfficeExam())
        {
            addRenown(-(int)(empire.getRenown() * 0.07));

            Dictionary<Actor, double> pData = new Dictionary<Actor, double>();
            List<Actor> officers = data.centerOffice.GetAllOfficers(this);
            if (officers.Count > 0)
            {
                foreach (Actor actor in officers)
                {
                    OfficeIdentity identity = actor.GetIdentity(this);
                    if (identity == null) continue;
                    if (identity.performanceEvents == null) continue;
                    (PerformanceEvent pEvent, double pValue) performance = identity.performanceEvents.TriggerEvent();
                    actor.editRenown((int)(performance.pValue*0.4));
                    //记录事件
                    pData[actor] = performance.pValue;
                    actor.ResetPerformance();
                }
                if (pData.Values.Count > 0)
                {
                    double averagePerformance = pData.Values.Average();
                    double variancerformance = pData.Values.Select(x => Math.Pow(x - averagePerformance, 2)).Average(); // 计算方差
                    double standardDeviationrformance = Math.Sqrt(variancerformance); // 计算标准方差
                    foreach (var item in pData)
                    {
                        Actor actor = item.Key;
                        double mark = item.Value;
                        if (mark >= averagePerformance + standardDeviationrformance)
                        {
                            actor.AddOfficeExamLevel(Enums.EmpireExamLevel.HD);
                        }
                        else if (mark >= averagePerformance)
                        {
                            actor.AddOfficeExamLevel(Enums.EmpireExamLevel.CR);
                        }
                        else if (mark >= averagePerformance - standardDeviationrformance)
                        {
                            actor.AddOfficeExamLevel(Enums.EmpireExamLevel.P);
                        }
                        else
                        {
                            actor.AddOfficeExamLevel(Enums.EmpireExamLevel.F);
                        }
                    }
                }
            }
            data.last_office_exam_timestamp = World.world.getCurWorldTime();
        }
    }

    public bool canTakeArmedProvince()
    {
        bool flag = false;
        foreach(Kingdom kingdom in kingdoms_list)
        {
            if (kingdom == null) continue;
            if (!kingdom.isAlive()) continue;
            if (kingdom.isRekt()) continue;
            try
            {
                if (!kingdom.isBorder() && emperor.renown >= kingdom.king.renown * 2&&kingdom.countTotalWarriors()>=this.countWarriors()/5)
                {
                    return true;
                }
            } catch
            {
                LogService.LogInfo("国家实体已被销毁");
                flag = true;
                continue;
            }

        }
        if (flag)
        {
            ModClass.EMPIRE_MANAGER.update(-1L);
        }
        return false;
    }

    public EmpirePeriod getEmpirePeriod()
    {
        int renown = this.empire.getRenown();
        if (renown >= 300)
            this.data.empirePeriod = EmpirePeriod.平和;
        else if (renown >= 500)
            this.data.empirePeriod = EmpirePeriod.拓土扩业;
        else if (renown <= 200)
            this.data.empirePeriod = EmpirePeriod.下降;
        else if (renown <= 150)
            this.data.empirePeriod = EmpirePeriod.逐鹿群雄;
        else
            this.data.empirePeriod = EmpirePeriod.天命丧失;
        return this.data.empirePeriod;
    }

    public void StartEmpireExam()
    {
        this.data.last_exam_timestamp = World.world.getCurWorldTime();
    }

    public int GetLastExamYear()
    {
        return Date.getYearsSince(this.data.last_exam_timestamp);
    }

    public string getCulture()
    {
        return ConfigData.speciesCulturePair.TryGetValue(empire.getSpecies(), out var culture) ? culture : "";
    }
    public bool isAllowToMakeWar()
    {
        if (this.data.empirePeriod == EmpirePeriod.逐鹿群雄 || this.data.empirePeriod == EmpirePeriod.天命丧失)
        {
            return true;
        }
        return false;
    }

    public string GetEmpireName()
    {
        string[] nameParts = this.data.name.Split('\u200A');
        if (nameParts.Length == 1)
        {
            return nameParts[0].Split(' ').Last();
        } 
        else if (nameParts.Length > 1)
        {
            return nameParts[nameParts.Length - 2];

        } else
        {
            return "";
        }
    }

    //内阁官员选拔机制
    public void InerOfficeSet()
    {
        SelectMinister();
        SelectGeneral();
        SelectDivisions();
        SelectCoreOffices();
        if (data.centerOffice.Minister==data.centerOffice.General&& data.centerOffice.General.actor_id!= -1L)
        {
            data.centerOffice.GreaterGeneral = data.centerOffice.Minister;
            Actor actor = World.world.units.get(data.centerOffice.GreaterGeneral.actor_id);
            if (actor != null) 
            {
                actor.UpgradeOfficial(true, 0);
                actor.UpgradeOfficial(false, 0);
                actor.ChangeOfficialLevel(OfficialLevel.officiallevel_1);
                actor.SetPeerageType(PeerageType.Both);
                TranslateHelper.LogBecomeGreaterGeneral(actor, this);
                data.is_been_controled = true;
            }
        }
    }
    public void SetOfficeBase(OfficeObject obj, PeerageType peerageType, string require_trait_id, int meritLevel, int honoraryOfficial, OfficialLevel officialLevel)
    {
        long id = obj.actor_id;
        Actor actor = World.world.units.get(id);
        if (actor == null || id == -1L)
        {
            ListPool<Actor> pool = new ListPool<Actor>();
            ListPool<Actor> pool2 = new ListPool<Actor>();
            ListPool<Actor> pool3 = new ListPool<Actor>();
            foreach (Kingdom kingdom in kingdoms_list)
            {
                foreach (Actor potential in kingdom.units)
                {
                    if (potential != null)
                    {
                        if (potential.isEmperor()) continue;
                        if (potential.isUnitFitToRule() && potential.hasTrait("officer"))
                        {
                            OfficeIdentity identity = potential.GetIdentity(this);
                            if (identity == null) continue;
                            if (identity.honoraryOfficial <= 2 && identity.peerageType == peerageType)
                            {
                                pool.Add(potential);
                            }
                        }
                        if (potential.hasClan() && !potential.isOfficer())
                        {
                            if (potential.clan == empire.getKingClan())
                            {
                                pool2.Add(potential);
                            }
                        }
                        if (potential.hasTrait(require_trait_id) && !potential.isOfficer())
                        {
                            pool3.Add(potential);
                        }
                    }
                }
            }
            bool flag = false;
            Actor final = null;
            if (pool.Count > 0)
            {
                final = pool.First();
                flag = true;
            }
            else if (pool2.Count > 0)
            {
                if (empire.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool2, empire.culture);
                }
                else
                {
                    pool2.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool2.First();
                }
                OfficeIdentity identity = new OfficeIdentity();
                identity.init(this, final);
                identity.peerageType = peerageType;
                identity.meritLevel = meritLevel;
                identity.honoraryOfficial = honoraryOfficial;
                final.SetIdentity(identity, false);
                flag = true;
            }
            else if (pool3.Count > 0)
            {
                if (empire.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool3, empire.culture);
                } else
                {
                    pool3.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool3.First();
                }
                OfficeIdentity identity = new OfficeIdentity();
                identity.init(this, final);
                identity.peerageType = peerageType;
                identity.meritLevel = meritLevel;
                identity.honoraryOfficial = honoraryOfficial;
                final.SetIdentity(identity, false);
                flag = true;
            }
            if (flag)
            {
                obj.SetActor(final);
                final.joinCity(this.empire.capital);
                final.goTo(this.empire.capital._city_tile);
                obj.timestamp = World.world.getCurWorldTime();
                final.ChangeOfficialLevel(officialLevel);
            }
        } else if (actor!=null)
        {
            if (obj.GetOnTime()>=16)
            {
                obj.RemoveActor();
            }
        }
    }
    //设置宰相
    public void SelectMinister()
    {
        SetOfficeBase(this.data.centerOffice.Minister, PeerageType.Civil, "jingshi", 3, 2, OfficialLevel.officiallevel_2);
    }
    //设置将军
    public void SelectGeneral()
    {
        SetOfficeBase(this.data.centerOffice.General, PeerageType.Military, "jingshi", 3, 2, OfficialLevel.officiallevel_3);
    }
    //设置三省
    public void SelectCoreOffices()
    {
        foreach(var office in this.data.centerOffice.CoreOffices)
        {
            SetOfficeBase(office.Value, PeerageType.Civil, "jingshi", 5, 4, office.Value.level);
        }
    }
    //设置六部
    public void SelectDivisions()
    {
        foreach (var office in this.data.centerOffice.Divisions)
        {
            SetOfficeBase(office.Value, PeerageType.Civil, "gongshi",7, 6, office.Value.level);
        }
    }

    public bool isNeedToEducateHeir()
    {
        if (Heir!=null)
        {
            if (Heir.data!=null)
            {
                if (data.last_educate_timestamp == -1L)
                {
                    return true;
                }
                else
                {
                    return Date.getYearsSince(data.last_educate_timestamp) > 1;
                }
            }
        }
        return false;
    }


    public void setEmperor(Actor actor, bool isNew=false)
    {
        bool is_new = false;
        this.emperor = actor;
        int renown = this.empire.getRenown();
        Actor valid_emperor = null;
        if (!emperor.hasClan()||emperor.clan == null||(emperor.clan != this.empire_clan&&!isNew))
        {
            if(this.empire_clan!=null)
            {
                if(this.empire_clan.isAlive())
                {
                    if(this.empire_clan.units.Count>0)
                    {
                        valid_emperor = this.empire_clan.units.First();
                    }
                }
            }
            is_new = true;
            bool has_new_empire = false;
            Empire newEmpire = null;
            Province start_province = null;
            if (province_list.Count > 1&&empire.cities.Count>1)
            {
                foreach (Province province in province_list.ToList())
                {
                    if (province == null) continue;
                    if (province.data.isDirectRule) continue;
                    if (!province.isAlive()) continue;
                    if (province.HasOfficer() && !province.data.isDirectRule)
                    {
                        if (valid_emperor != null && !has_new_empire)
                        {
                            if (valid_emperor.isAlive())
                            {
                                Kingdom kingdom = province.becomeKingdom(true, valid_emperor);
                                kingdom.setCapital(province.province_capital);
                                newEmpire = ModClass.EMPIRE_MANAGER.newEmpire(kingdom);
                                province.SetProvinceLevel(provinceLevel.provincelevel_3);
                                newEmpire.updateCapital(this.original_capital);
                                newEmpire.data.history.InsertRange(0, this.data.history);
                                newEmpire.province_list = new List<Province>();
                                newEmpire.province_list.AddRange(this.province_list);
                                newEmpire.province_list.ForEach(p =>
                                {
                                    p.empire = newEmpire;
                                    p.updateOccupied();
                                });
                                this.province_list.Clear();
                                start_province = province;
                                has_new_empire = true;
                                break;
                            }
                        }
                    }
                }
            }
            if (has_new_empire) 
            {
                List<Province> provinces = new List<Province>();
                startSplit(newEmpire, start_province, ref provinces, 0.7f);
            }
            this.addRenown(-(int)(renown * 0.5));
            if (this.emperor.hasClan())
            {
                try
                {
                    if (this.emperor.clan.HasHistoryEmpire())
                    {
                        string empireName = String.Join("\u200A", newEmpire.GetDir(this.emperor.clan.GetHistoryEmpirePos()), this.emperor.clan.GetHistoryEmpireName());
                        newEmpire.SetEmpireName(empireName);
                    } else
                    {
                        string name = actor.generateName(MetaType.Kingdom, empire.getID());
                        this.empire.SetKingdomName(name.Split('\u200A')[0]);
                        this.empire.generateNewMetaObject();
                        this.SetEmpireName(name.Split('\u200A')[0]);
                    }
                } catch
                {
                    string name = actor.generateName(MetaType.Kingdom, empire.getID());
                    this.empire.SetKingdomName(name.Split('\u200A')[0]);
                    this.empire.generateNewMetaObject();
                    this.SetEmpireName(name.Split('\u200A')[0]);
                }

            }else
            {
                string name = actor.generateName(MetaType.Kingdom, empire.getID());
                this.empire.SetKingdomName(name.Split('\u200A')[0]);
                this.empire.generateNewMetaObject();
                this.SetEmpireName(name.Split('\u200A')[0]);
            }
            this.empire_clan = this.emperor.clan;
        }
        if (emperor.isOfficer())
        {
            emperor.RemoveIdentity();
            emperor.SetPeeragesLevel(PeeragesLevel.peerages_0);
        }
        this.emperor.data.renown += 20;
        this.emperor.SetEmpire(this);
        try
        {
            this.emperor.joinCity(this.empire.capital);
            this.emperor.goTo(this.empire.capital._city_tile);
        } catch
        {
            LogService.LogInfo("找不到首都");
        }
        this.emperor.joinKingdom(this.empire);
        this.Heir = null;
        create_year_name();
        TranslateHelper.LogNewEmperor(emperor, empire.capital, data.year_name);
        if (!isNew)
        {
            if (this.emperor.clan != this.empire_clan)
            {
                this.data.original_royal_been_changed = true;
                this.data.original_royal_been_changed_timestamp = World.world.getCurWorldTime();
            }

            this.data.currentHistory = new EmpireCraftHistory
            {
                id = actor.data.id,
                empire_name = this.GetEmpireName(),
                year_name = data.year_name,
                emperor = actor.getName(),
                miaohao_name = "",
                shihao_name = "",
                descriptions = new List<string>(),
                cities = new List<string>(),
                is_first = is_new
            };
        }
        this.RecordHistory(EmpireHistoryType.new_emperor_history, new Dictionary<string, string>()
        {
            ["actor"] = actor.getName(),
            ["place"] = this.empire.capital.GetCityName(),
            ["year_name"] = this.data.year_name
        });
    }

    public void startSplit(Empire empire, Province start, ref List<Province> joined_province_list, double possibility=0.8f)
    {
        if (start.data.isDirectRule) return;
        if (joined_province_list.Contains(start)) return;
        if (start.isKingdom()) return;
        System.Random rand = new System.Random();
        double randomValue = rand.NextDouble(); // [0.0, 1.0)
        LogService.LogInfo("当前随机数: "+randomValue.ToString());
        LogService.LogInfo("当前概率: "+ possibility.ToString());
        if (randomValue >= possibility) return;
        if (empire == null) return;
        foreach(City city in start.city_list)
        {
            if (city.isCapitalCity()) return;
            if (city.hasKingdom())
            {
                if (city.kingdom.isEmpire())
                {
                    if (city.kingdom.GetEmpire() == empire) return;
                }
            }
        }
        LogService.LogInfo("检测到帝国");
        if (start == null) return;
        LogService.LogInfo("存在省份");
        if (joined_province_list.Count >= empire.province_list.Count) return;
        LogService.LogInfo("存在差集");
        foreach (Province province in empire.province_list.ToList())
        {
            if (province.isNeighbourWith(start))
            {
                try
                {
                    province.becomeKingdom();
                    startSplit(empire, province, ref joined_province_list, possibility);
                }
                catch (Exception e) 
                {
                    LogService.LogInfo("转化失败，并入土地帝国");
                    foreach (City city in province.city_list) 
                    {
                        city.joinAnotherKingdom(empire.empire);
                        joined_province_list.Add(province);
                    }
                    province.updateOccupied();
                }

            }
        }
        joined_province_list.Add(start);
    }

    public void updateCapital(City capital)
    {
        this.original_capital = capital;
        this.capital_center = capital.city_center;
    }

    public bool isNeighbourWith(Kingdom kingdom)
    {
        foreach (Kingdom kingdom1 in this.kingdoms_list)
        {
            foreach(City city in kingdom1.cities)
            {
                if (city.neighbours_kingdoms.Count > 0)
                {
                    foreach(Kingdom kingdom2 in city.neighbours_kingdoms)
                    {
                        if (kingdom2 == kingdom) 
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    public bool isNeedToSetArmedProvince()
    {
        foreach(City city in this.AllCities())
        {
            if (city.neighbours_kingdoms.Count>0)
            {
                if(!city.hasProvince()) return false;
                Province province = city.GetProvince();
                if(province == null) continue;
                if (province.data.isDirectRule) continue;
                if (province.officer == null) continue;
                if (!province.officer.isUnitFitToRule()) continue;
                foreach(Kingdom kingdom in city.neighbours_kingdoms)
                {
                    if (!kingdom.isInEmpire())
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    public void EmperorLeft(Kingdom kingdom)
    {
        if (this.emperor == null) return;
        if (this.emperor.data == null) return;
        if (data.currentHistory == null)
        {
            data.currentHistory = new EmpireCraftHistory
            {
                id = this.emperor.data.id,
                year_name = data.year_name,
                emperor = this.emperor.data.name,
                empire_name = this.GetEmpireName(),
                miaohao_name = "",
                shihao_name = "",
                descriptions = new List<string>(),
                cities = new List<string>()
            };
        }
        if (this.emperor.isAlive())
        {
            this.RecordHistory(EmpireHistoryType.emperor_left_history, new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.emperor.data.name
            });
        }
        else
        {
            this.RecordHistory(EmpireHistoryType.emperor_die_history, new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.emperor.data.name
            });
        }
        this.emperor.RemoveEmpire();
        data.currentHistory.total_time = Date.getYearsSince(data.newEmperor_timestamp);
        data.history.Add(data.currentHistory);
        data.currentHistory = null;
        emperor = null;
    }
    public bool isNeedToSetPosthumous()
    {
        if (this.data.history.Count > 0)
        {
            foreach (EmpireCraftHistory cHistory in this.data.history)
            {
                Actor actor = World.world.units.get(cHistory.id);
                if (cHistory != null && cHistory.emperor != null && cHistory.emperor != "")
                {
                    if (cHistory.miaohao_name==null||cHistory.miaohao_name == "")
                    {
                        if (actor != null)
                        {
                            if (!actor.isAlive())
                            {
                                return true;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    public int getEmperorYear()
    {
        return Date.getYearsSince(this.data.newEmperor_timestamp) + 1;
    }

    public string getYearNameWithTime()
    {
        if (this.data.has_year_name)
        {
            if (this.emperor!=null)
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return this.data.year_name + "\u200A" + getEmperorYear() + LM.Get("Year");
                }
            }
        }
        return "";
    }

    public ArmySystemType getArmyType()
    {
        return this.data.armySystemType;
    }

    public bool canJoinWar()
    {
        return Date.getMonthsSince(data.timestamp_invite_war_cool_down)>=3;
    }

    public void setArmyType(ArmySystemType type)
    {
        this.data.armySystemType = type;
    }

    public void createNewEmpire(Kingdom kingdom)
    {
        if (kingdom == null) return;
        if (kingdom.data == null) return;
        if (!kingdom.isAlive()) return;
        this.data.heir_type = EmpireHeirLawType.eldest_son;
        this.data.last_exam_timestamp = World.world.getCurWorldTime();
        this.data.armySystemType = ArmySystemType.募兵制;
        this.StartEmpireExam();
        if (ConfigData.speciesCulturePair != null) 
        {
            if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture)) {
                if (culture == "Huaxia")
                {
                    this.data.has_year_name = true;
                }
            }
        }
        this.data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
        this.empire = kingdom;
        kingdom.SetCountryLevel(countryLevel.countrylevel_0);
        if (this.empire.getKingClan() != null) this.empire_clan = this.empire.getKingClan();
        else 
        {
            this.empire_clan = null;
        }
        this.original_capital = kingdom.capital;
        this.data.banner_icon_id = kingdom.data.banner_icon_id;
        this.data.banner_background_id = kingdom.data.banner_background_id;
        this.data.timestamp_established_time = World.world.getCurWorldTime();
        try
        {
            this.capital_center = kingdom.capital.city_center;
        } catch
        {
            LogService.LogInfo("找不到帝国首都");
        }
        this.generateNewMetaObject();
        string empireName = kingdom.GetKingdomName();
        if (kingdom.king.HasTitle())
        {
            empireName = kingdom.king.GetTitle();
        }
        try
        {
            if (kingdom.getKingClan() != null)
            {
                if (kingdom.getKingClan().HasHistoryEmpire())
                {
                    empireName = String.Join("\u200A", GetDir(kingdom.getKingClan().GetHistoryEmpirePos()), kingdom.getKingClan().GetHistoryEmpireName());
                }
            }

        } catch
        {
            LogService.LogInfo("读取氏族历史帝国名称失败");
        }
        SetEmpireName(empireName);
        try
        {
            this.data.currentHistory = new EmpireCraftHistory
            {
                id = kingdom.king.data.id,
                year_name = data.year_name,
                emperor = kingdom.king.getName(),
                empire_name = this.GetEmpireName(),
                is_first = true,
                miaohao_name = "",
                shihao_name = "",
                descriptions = new List<string>(),
                cities = new List<string>()
            };
            this.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
            {
                ["actor"] = kingdom.king.getName(),
                ["place"] = kingdom.capital.GetCityName(),
                ["name"] = GetEmpireName(),
                ["place"] = kingdom.capital.GetCityName()
            });
            setEmperor(kingdom.king, true);
            kingdom.getKingClan().RecordHistoryEmpire(this);

        } catch
        {
            LogService.LogInfo("继承帝国信息失败");
        }

        kingdom.data.name = this.data.name;

    }
    public bool canSetTitleToPreviousEmperor()
    {
        if (this.data.history.Count > 0)
        {
            foreach(EmpireCraftHistory cHistory in this.data.history)
            {
                if (cHistory != null && cHistory.emperor != null && cHistory.emperor != "" && !World.world.units.get(cHistory.id).isAlive())
                {
                    return true;
                }
            }
        }
        return false;
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
        this.data.year_name = YearNameHelper.generateName();
        this.data.newEmperor_timestamp = World.world.getCurWorldTime();
    }

    public string GetDir(Vector2 v)
    {
        float ax = Math.Abs(v.x- capital_center.x);
        float ay = Math.Abs(v.y- capital_center.y);
        if (ax > ay)
        {
            return LM.Get(capital_center.x > v.x ?"Eastern" : "Western");
        }
        else if (ay > ax)
        {
            return LM.Get(capital_center.y > v.y ? "Northern" : "Southern");
        }
        else
        {
            return LM.Get("Later");
        }
    }

    public bool isRoyalBeenChanged()
    {
        return data.original_royal_been_changed;
    }

    public void SetEmpireName(string name)
    {
        string culture = ConfigData.speciesCulturePair.TryGetValue(empire.getSpecies(), out var a) ? a : "default";
        this.data.name = name + "\u200A" + LM.Get($"{culture}_" + countryLevel.countrylevel_0.ToString());
        this.empire.data.name = this.data.name;
    }

    public void checkDisolve(Kingdom mainKingdom)
    {
        this.kingdoms_hashset.Remove(mainKingdom);
        mainKingdom.empireLeave(false);
        Kingdom heirEmpire = null;
        if (empire_clan != null)
        {
            if (empire_clan.isAlive())
            {
                foreach (Kingdom kingdom in kingdoms_hashset)
                {
                    if (kingdom.getKingClan() != null)
                        if (kingdom.getKingClan().getID() == empire_clan.getID())
                        {
                            if (heirEmpire == null || kingdom.countTotalWarriors() > heirEmpire.countTotalWarriors())
                            {
                                heirEmpire = kingdom;
                            }
                        }
                }
            }
        }
        if (heirEmpire == null)
        {
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
            return;
        }
        recalculate();
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
        newEmpire.data.history.InsertRange(0, this.data.history);
        newEmpire.SetEmpireName(newKingdom.GetKingdomName());
        if (newKingdom.capital.HasKingdomName()) 
        {
            SetEmpireName(newKingdom.capital.SelectKingdomName());
        }
        if (newKingdom.getKingClan().HasHistoryEmpire())
        {
            string empireName = String.Join("\u200A", newEmpire.GetDir(newKingdom.getKingClan().GetHistoryEmpirePos()), newKingdom.getKingClan().GetHistoryEmpireName());
            newEmpire.SetEmpireName(empireName);
        }
        if (newKingdom.king.HasTitle())
        {
            newEmpire.SetEmpireName(newKingdom.king.GetTitle());
        }
        if (newKingdom.getKingClan() == empire_clan)
        {
            newEmpire.SetEmpireName(newEmpire.GetDir(this.empire_center) + "\u200A" + GetEmpireName());
        }
        if (newKingdom.king.hasClan())
        {
            newKingdom.getKingClan().RecordHistoryEmpire(newEmpire);
            newEmpire.empire_clan = newKingdom.getKingClan();
        }
        else
        {
            Clan clan = World.world.clans.newClan(newKingdom.king, true);
            newEmpire.empire_clan = clan;
            clan.RecordHistoryEmpire(newEmpire);
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
        foreach (Province province in this.province_list)
        {
            province.empire = newEmpire;
            province.asset = AssetManager.kingdoms.get(newKingdom.king.asset.kingdom_id_civilization);
            province.updateColor(newEmpire.getColorLibrary().getNextColor());
            province.data.banner_icon_id = newKingdom.data.banner_icon_id;
            province.data.banner_background_id = newKingdom.data.banner_background_id;
            province.data.color_id = newKingdom.data.color_id;
        }
        newEmpire.create_year_name();
        newEmpire.recalculate();
        TranslateHelper.LogNewEmperor(newKingdom.king, newKingdom.capital, newEmpire.data.year_name);
        newKingdom.data.name = newEmpire.data.name;
        ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
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
            if (k.isRekt())
            {
                remove_tKingdoms.Add(k);
                tChanged = true;
            }
        }
        foreach (Kingdom k in remove_tKingdoms)
        {
            if (!k.isRekt())
            {
                this.leave(k, false);
            }
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
        ListPool<Province> listPool = new ListPool<Province>();
        foreach(Province province in ModClass.PROVINCE_MANAGER)
        {
            if (province.empire==this)
            {
                listPool.Add(province);
            }
        }
        foreach(Province province in listPool)
        {
            ModClass.PROVINCE_MANAGER.dissolveProvince(province);
        }
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
        if (this.empire == null) return;
        if (this.empire.data == null) return;
        this.data.kingdoms = new List<long>();
        foreach (Kingdom tKingdom in this.kingdoms_hashset)
        {
            if (tKingdom!=null)
            {
                this.data.kingdoms.Add(tKingdom.id);
            }
        }
        foreach(Province province in this.province_list)
        {
            if (province!=null)
            {
                this.data.province_list.Add(province.id);
            }
        }

        if (this.emperor != null)
            this.data.emperor = this.emperor.data.id;
        else
            this.data.emperor = -1L;
        this.data.empire = this.empire.data.id;
        if (this.Heir!=null)
        {
            if (this.Heir.isUnitFitToRule())
            {
                this.data.Heir = this.Heir.getID();
            }
        }
        else
        {
            this.data.Heir = -1L;
        }
        this.data.original_capital = this.original_capital.isAlive() ? this.original_capital.data.id : -1L;
        try
        {
            this.data.empire_clan = this.empire_clan == null ? -1L : this.empire_clan.data.id;
        }
        catch
        {
            this.data.empire_clan = -1L;
        }

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
        this.Heir = World.world.units.get(pData.Heir);
        this.recalculate();
    }

    public void syncProvince()
    {
        this.province_list = new List<Province>();
        foreach (long provinceID in this.data.province_list)
        {
            Province p = ModClass.PROVINCE_MANAGER.get(provinceID);
            if (p != null)
            {
                this.province_list.Add(p);
            }
        }
    }

    public bool isNeedToGetBackProvince()
    {
        UpdateProvinceStatus();
        foreach (Province province in province_list)
        {
            if (province.isRekt()) continue;
            if(province.occupied_cities.Count > 0)
            {
                LogService.LogInfo(province.data.name);
                return true;
            }
        }
        return false;
    }

    public void UpdateProvinceStatus()
    {
        if (this.province_list == null) return;
        ListPool<Province> invalid_province = new ListPool<Province> { };
        foreach (Province province in province_list)
        {
            if (province.isRekt())
            {
                invalid_province.Add(province);
            }
            else
            {
                if (province.city_list.Count <= 0)
                {
                    invalid_province.Add(province);
                }else
                {
                    foreach(City city in province.city_list)
                    {
                        if (city.isRekt())
                        {
                            invalid_province.Add(province);
                            break;
                        } else
                        {
                            if (city.hasProvince())
                            {
                                if (city.GetProvince()!=province)
                                {
                                    invalid_province.Add(province);
                                }
                            }
                        }
                    }
                }
            }
            province.updateOccupied();
        }
        foreach (Province province2 in invalid_province)
        {
            if (province2 == null) { this.province_list.Remove(province2); }
            else
            {
                ModClass.PROVINCE_MANAGER.dissolveProvince(province2);
            }
        }
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
        pKingdom.empireJoin(this);
        if (pRecalc)
        {
            this.recalculate();
        }
        this.data.timestamp_member_joined = World.world.getCurWorldTime();
        pKingdom.SetLoyalty(999);
        return true;
    }

    public void leave(Kingdom pKingdom, bool pRecalc = true)
    {
        this.kingdoms_hashset.Remove(pKingdom);
        pKingdom.empireLeave(false);
        if (pKingdom.isEmpire())
        {
            checkDisolve(pKingdom);
        } else
        {
            if (ShouldDissolveEmpire())
            {
                ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
                LogService.LogInfo("帝国内部国家数量为0解散");
            }
        }
        if (pRecalc)
        {
            this.recalculate();
        }
    }

    private bool ShouldDissolveEmpire()
    {
        // 如果没有王国剩余
        if (countKingdoms() <= 0)
        {
            return true;
        }

        return false;
    }

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

    public List<City> AllCities()
    {
        List<City> tResult = new List<City>();
        List<Kingdom> tKingdoms = this.kingdoms_list;
        for (int i = 0; i < tKingdoms.Count; i++)
        {
            Kingdom tKingdom = tKingdoms[i];
            tResult.AddRange(tKingdom.cities);
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

    public void StartExam()
    {

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
            var seed = unassigned.First();
            var region = new List<City> { seed };
            unassigned.Remove(seed);

            var queue = new Queue<City>();
            queue.Enqueue(seed);

            while (queue.Count > 0 && region.Count < avgCitiesPerKingdom)
            {
                var curr = queue.Dequeue();
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
            empire.getMaxCities();
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
                Province province;
                Actor king;
                bool flag = true;
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
                    cl = countryLevel.countrylevel_3;
                    pl = PeeragesLevel.peerages_3;
                }
                if (flag)
                {
                    newKingdom = setEnfeoff(capital, king);
                    foreach (var city in region)
                    {
                        city.joinAnotherKingdom(newKingdom);
                    }
                    newKingdom.setCapital(capital);
                    newKingdom.data.name = capital.data.name;
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
                } else
                {
                    province = ModClass.PROVINCE_MANAGER.newProvince(capital);
                    foreach (var city in region)
                    {
                        if (city!=capital)
                        {
                            province.addCity(city);
                        }
                    }
                    LogService.LogInfo($"Province {province.data.name} has been build, include {province.city_list.Count()} cities");
                }
            }
        }
        
    }

    public bool isNeedToSetProvince()
    {
        foreach(City city in empire.cities)
        {
            if (!city.hasProvince())
            {
                return true;
            }
        }
        return false;
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

    public override Actor getRandomUnit()
    {
        return this.kingdoms_list.GetRandom<Kingdom>().getRandomUnit();
    }

    public Sprite getBackgroundSprite()
    {
        return World.world.alliances.getBackgroundsList()[this.data.banner_background_id];
    }

    public Sprite getIconSprite()
    {
        return empire.getSpriteIcon();
    }

    public override void Dispose()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_hashset.Clear();
        this.empire = null;
        province_list.Clear();
        if (!ModClass.ALL_HISTORY_DATA.ContainsKey(this.data.id))
        {
            ModClass.ALL_HISTORY_DATA.Add(this.data.id, this.data.history);
        }
    }


    public List<Kingdom> kingdoms_list = new List<Kingdom>();
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();

    public Kingdom empire;

    public int power;
}

public static class ProvinceDivider
{
    public static List<Province> DivideIntoProvince(this Empire empire)
    {
        List<City> cities = empire.empire.cities.FindAll(a=>!a.hasProvince());
        var result = new List<Province>();
        if (cities == null || cities.Count == 0) return result;
        var remaining = new List<City>(cities);

        Dictionary<KingdomTitle, Province> kpPair = new Dictionary<KingdomTitle, Province>();
        foreach (City city in cities) 
        {
            if (city.isRekt()) continue;
            if (city.hasTitle())
            {
                KingdomTitle title = city.GetTitle();
                if (kpPair.ContainsKey(title))
                {
                    Province province = kpPair[title];
                    province.addCity(city);
                }
                else
                {
                    Province province = ModClass.PROVINCE_MANAGER.newProvince(city, title.data.province_name);
                    result.Add(province);
                    kpPair.Add(title, province);
                }
            } else
            {
                Province province = ModClass.PROVINCE_MANAGER.newProvince(city);
                result.Add(province);
            }
        }
        return result;
    }
}