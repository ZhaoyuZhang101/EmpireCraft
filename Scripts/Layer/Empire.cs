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
using Random = System.Random;

namespace EmpireCraft.Scripts.Layer;
// Token: 0x0200023B RID: 571
public class Empire : MetaObject<EmpireData>
{
    public BannerAsset BannerAsset;
    private Vector3 _lastEmpireCenter;
    private Vector3 _empireCenter;
    private readonly List<TileZone> _zoneScratch = new();
    private readonly int _avgCitiesPerKingdom = 3;
    public Clan EmpireClan;
    public Actor Emperor;
    public Actor Heir;
    private Vector3 _capitalCenter;
    public City OriginalCapital;
    public EmpireCraftMapMode MapMode = EmpireCraftMapMode.Empire;
    public List<Province> ProvinceList = new List<Province>();
    public　SpecificClan EmpireSpecificClan => SpecificClanManager.Get(data.empire_specific_clan);

    public bool HasEmperor => !Emperor.isRekt();
    public bool HasHeir => !Heir.isRekt();
    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }
    public List<Actor> GetMembersWithTrait(string trait)
    {
        List<Actor>  list = new List<Actor>();
        foreach (Kingdom kingdom in kingdoms_hashset)
        {
            foreach (Actor actor in kingdom.getUnits())
            {
                if (actor.hasTrait(trait))
                {
                    list.Add(actor);
                }
            }
        }
        return list;
    }

    public bool IsNeedToExam()
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

    public new void AddRenown(int value)
    {
        this.CoreKingdom.data.renown += value;
        if (this.CoreKingdom.data.renown<=0)
        {
            this.CoreKingdom.data.renown = 0;
        }
    }

    private bool IsNeedToOfficeExam()
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
    public Actor CheckHeir(EmpireHeirLawType secondSelection=EmpireHeirLawType.none)
    {
        Actor actor = null;
        Emperor.CheckSpecificClan();
        PersonalClanIdentity pci = Emperor?.GetPersonalIdentity();
        EmpireHeirLawType type = secondSelection==EmpireHeirLawType.none?data.heir_type:secondSelection;
        List<(ClanRelation, PersonalClanIdentity)> children = new();
        children = SpecificClanManager.getChildren(pci).FindAll(a=>a.Item2.CanHeir(pci));
        children.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>.Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
        switch (type)
        {
            case EmpireHeirLawType.eldest_child:
                if (children.Any())
                {
                    actor = children.Last().Item2._actor; // Assuming eldest is the last after sorting by age
                    LogService.LogInfo("长嗣");
                }
                break;
            case EmpireHeirLawType.smallest_child:
                if (children.Any())
                {
                    actor = children.First().Item2._actor; // Assuming youngest is the first after sorting by age
                    LogService.LogInfo("幼子");
                }
                break;
            case EmpireHeirLawType.siblings:
                // Logic for selecting a brother heir can be added here
                List<(ClanRelation, PersonalClanIdentity)> brothers = SpecificClanManager.GetSiblingsWithRelation(pci).FindAll(a=>a.Item2.CanHeir(pci));
                brothers.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (brothers.Any())
                {
                    actor = brothers.Last().Item2._actor;
                    LogService.LogInfo("兄弟");
                }
                break;
            case EmpireHeirLawType.grand_child_generation:
                List<(ClanRelation, PersonalClanIdentity)> grandChildren = SpecificClanManager.GetGrandChildren(pci);
                grandChildren = grandChildren.FindAll(c=>c.Item2.CanHeir(pci));
                grandChildren.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (grandChildren.Any())
                {
                    actor = grandChildren.Last().Item2._actor;
                    LogService.LogInfo("长孙");
                }
                break;
            case EmpireHeirLawType.random:
                List<(ClanRelation, PersonalClanIdentity)> randomClanMember = SpecificClanManager.FindAllRelations(pci);
                randomClanMember = randomClanMember.FindAll(c=>c.Item2.CanHeir(pci));
                randomClanMember.Sort(Comparer<(ClanRelation, PersonalClanIdentity)>
                    .Create((a, b) => a.Item2.age.CompareTo(b.Item2.age)));
                if (randomClanMember.Any())
                {
                    actor = randomClanMember.Last().Item2._actor;
                    LogService.LogInfo("随机拥有继承权的人");
                }
                break;
            default:
                actor = data.centerOffice.Minister.GetActor() 
                        ?? (data.centerOffice.General.GetActor()
                            ??(data.centerOffice.CoreOffices.ToList().Find(a=>a.Value.GetActor()!=null).Value.GetActor()
                                ??(CoreKingdom.capital.GetProvince()?.officer
                                   ??CoreKingdom.capital.leader)));
                OfficeIdentity identity = actor.GetIdentity(this);
                if (pci != null)
                {
                    var officeName = String.Join("_", pci.culture, identity.officialLevel);
                    LogService.LogInfo("随机不限制"+LM.Get(officeName));
                }
                
                break;
        }
        return actor;
    }

    public void StartCalcOfficePerformance()
    {
        if (IsNeedToOfficeExam())
        {
            AddRenown(-(int)(CoreKingdom.getRenown() * 0.07));

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

    public bool CanTakeArmedProvince()
    {
        bool flag = false;
        foreach(Kingdom kingdom in kingdoms_list)
        {
            if (kingdom == null) continue;
            if (!kingdom.isAlive()) continue;
            if (kingdom.isRekt()) continue;
            try
            {
                if (!kingdom.isBorder() && Emperor.renown >= kingdom.king.renown * 2&&kingdom.countTotalWarriors()>=this.countWarriors()/5)
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

    public EmpirePeriod GetEmpirePeriod()
    {
        int renown = this.CoreKingdom.getRenown();
        if (renown >= 500)
            this.data.empirePeriod = EmpirePeriod.拓土扩业;
        else if (renown >= 300)
            this.data.empirePeriod = EmpirePeriod.平和;
        else if (renown >= 200)
            this.data.empirePeriod = EmpirePeriod.下降;
        else if (renown >= 150)
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

    public string GetCulture()
    {
        return ConfigData.speciesCulturePair.TryGetValue(CoreKingdom.getSpecies(), out var culture) ? culture : "";
    }
    public bool IsAllowToMakeWar()
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
                data.is_been_controlled = true;
            }
        }
    }

    private void SetOfficeBase(OfficeObject obj)
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
                        if (potential.IsEmperor()) continue;
                        if (potential.isUnitFitToRule() && potential.hasTrait("officer"))
                        {
                            OfficeIdentity identity = potential.GetIdentity(this);
                            if (identity == null) continue;
                            if (identity.honoraryOfficial <= 2 && identity.peerageType == obj.peerage_type)
                            {
                                pool.Add(potential);
                            }
                        }
                        if (potential.hasClan() && !potential.isOfficer())
                        {
                            if (potential.clan == CoreKingdom.getKingClan())
                            {
                                pool2.Add(potential);
                            }
                        }

                        foreach (string requireTrait in obj.require_traits)
                        {
                            if (potential.hasTrait(requireTrait) && !potential.isOfficer())
                            {
                                pool3.Add(potential);
                                break;
                            }
                        }
                    }
                }
            }
            bool flag = false;
            Actor final = null;
            if (pool.Any())
            {
                final = pool.First();
                flag = true;
            }
            else if (pool2.Any())
            {
                if (CoreKingdom.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool2, CoreKingdom.culture);
                }
                else
                {
                    pool2.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool2.First();
                }
                flag = true;
            }
            else if (pool3.Any())
            {
                if (CoreKingdom.hasCulture())
                {
                    final = ListSorters.getUnitSortedByAgeAndTraits(pool3, CoreKingdom.culture);
                } else
                {
                    pool3.Sort(ListSorters.sortUnitByAgeOldFirst);
                    final = pool3.First();
                }
                flag = true;
            }
            if (flag)
            {
                SetOfficer(obj, final);
                final.joinCity(this.CoreKingdom.capital);
                final.goTo(this.CoreKingdom.capital._city_tile);
            }
        } else
        {
            if (obj.GetOnTime()>=16)
            {
                obj.RemoveActor();
            }
        }
    }

    public void SetOfficer(OfficeObject obj, Actor pActor)
    {
        obj.SetActor(pActor, this);
    }
    //设置宰相
    private void SelectMinister()
    {
        SetOfficeBase(this.data.centerOffice.Minister);
    }
    //设置将军
    private void SelectGeneral()
    {
        SetOfficeBase(this.data.centerOffice.General);
    }
    //设置三省
    private void SelectCoreOffices()
    {
        foreach(var office in this.data.centerOffice.CoreOffices)
        {
            SetOfficeBase(office.Value);
        }
    }
    //设置六部
    private void SelectDivisions()
    {
        foreach (var office in this.data.centerOffice.Divisions)
        {
            SetOfficeBase(office.Value);
        }
    }

    public bool IsNeedToEducateHeir()
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

    public Empire RebuildSecondEmpire(Province startProvince, Actor newEmperor)
    {
        Kingdom kingdom = startProvince.becomeKingdom(true, newEmperor);
        kingdom.setCapital(startProvince.province_capital);
        var newEmpire = ModClass.EMPIRE_MANAGER.newEmpire(kingdom);
        startProvince.SetProvinceLevel(provinceLevel.provincelevel_3);
        newEmpire.UpdateCapital(this.OriginalCapital);
        newEmpire.data.history.InsertRange(0, this.data.history);
        newEmpire.ProvinceList = new List<Province>();
        newEmpire.ProvinceList.AddRange(this.ProvinceList);
        newEmpire.ProvinceList.ForEach(p =>
        {
            p.empire = newEmpire;
            p.updateOccupied();
        });
        ProvinceList.Clear();
        string empireName = String.Join("\u200A", this.CalcDir(kingdom.capital.city_center, CoreKingdom.capital.city_center), this.GetEmpireName());
        newEmpire.SetEmpireName(empireName);
        
        List<Province> provinces = new List<Province>();
        StartSplit(newEmpire, startProvince, ref provinces);

        return newEmpire;
    }
    //帝国分裂方法
    private bool StartSplit(Actor newEmperor)
    {
        if (newEmperor.isRekt()) return false;
        if (ProvinceList.Count > 1&&CoreKingdom.cities.Count>1)
        {
            foreach (Province province in ProvinceList.ToList())
            {
                if (province == null) continue;
                if (province.data.isDirectRule) continue;
                if (!province.isAlive()) continue;
                if (province.HasOfficer() && !province.data.isDirectRule)
                {
                    RebuildSecondEmpire(province, newEmperor);
                    break;
                }
            }
        }
        AddRenown(-(int)(this.CoreKingdom.getRenown() * 0.5));
        return true;
    }

    private void MoveToEmpireCapital(Actor actor)
    {
        actor.joinCity(this.CoreKingdom.capital);
        actor.goTo(this.CoreKingdom.capital._city_tile);
        actor.joinKingdom(this.CoreKingdom);
    }
    
    //新皇登基
    public void NewEmperor(Actor actor, bool isNew = false)
    {
        Emperor = actor;
        actor.SetEmpire(this);
        string nameEmpire = "";
        //检查帝国分裂
        var currentSpecificClan = actor.GetSpecificClan();
        if (currentSpecificClan.id != data.empire_specific_clan && data.empire_specific_clan != -1L) 
        {
            if (currentSpecificClan.all_valid_members.Any())
            {
                var validEmperor = currentSpecificClan.all_valid_members?.First()._actor;
                StartSplit(validEmperor);
            }
            nameEmpire = Emperor.culture.getOnomasticData(MetaType.Kingdom).generateName();
            if (Emperor.hasClan())
            {
                if (Emperor.clan.HasHistoryEmpire())
                {
                    nameEmpire = GetDir(Emperor.clan.GetHistoryEmpirePos()) + "\u200A" + this.Emperor.clan.GetHistoryEmpireName();
                }
            }
            SetEmpireName(nameEmpire);
            isNew = true;
        } 
        data.empire_specific_clan = currentSpecificClan.id;
        EmpireClan = Emperor.clan;
        //设定天子身份并移居首都
        if (Emperor.isOfficer())
        {
            Emperor.RemoveIdentity();
            Emperor.SetPeeragesLevel(PeeragesLevel.peerages_0);
        }
        this.Emperor.data.renown += 20;
        MoveToEmpireCapital(this.Emperor);
        Heir = null;
        create_year_name();
        //公屏提示
        TranslateHelper.LogNewEmperor(Emperor, CoreKingdom.capital, data.year_name);
        
        //记录历史
        this.RecordNewEmperorHistory(isNew);
    }


    private void StartSplit(Empire empire, Province start, ref List<Province> pJoinedProvinceList, double possibility=0.8f)
    {
        if (start.data.isDirectRule) return;
        if (pJoinedProvinceList.Contains(start)) return;
        if (start.isKingdom()) return;
        Random rand = new Random();
        double randomValue = rand.NextDouble(); // [0.0, 1.0)
        LogService.LogInfo("当前随机数: "+randomValue);
        LogService.LogInfo("当前概率: "+ possibility);
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
        LogService.LogInfo("存在省份");
        if (pJoinedProvinceList.Count >= empire.ProvinceList.Count) return;
        LogService.LogInfo("存在差集");
        foreach (Province province in empire.ProvinceList.ToList())
        {
            if (province.isNeighbourWith(start))
            {
                try
                {
                    province.becomeKingdom();
                    StartSplit(empire, province, ref pJoinedProvinceList, possibility);
                }
                catch (Exception e) 
                {
                    LogService.LogInfo("转化失败，并入土地帝国");
                    foreach (City city in province.city_list) 
                    {
                        city.joinAnotherKingdom(empire.CoreKingdom);
                        pJoinedProvinceList.Add(province);
                    }
                    province.updateOccupied();
                }

            }
        }
        pJoinedProvinceList.Add(start);
    }

    public void UpdateCapital(City capital)
    {
        this.OriginalCapital = capital;
        this._capitalCenter = capital.city_center;
    }

    public bool IsNeighbourWith(Kingdom kingdom)
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

    public bool IsNeedToSetArmedProvince()
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
        if (this.Emperor == null) return;
        if (this.Emperor.data == null) return;
        if (data.currentHistory == null)
        {
            data.currentHistory = new EmpireCraftHistory
            {
                id = this.Emperor.data.id,
                year_name = data.year_name,
                emperor = this.Emperor.data.name,
                empire_name = this.GetEmpireName(),
                miaohao_name = "",
                shihao_name = "",
                descriptions = new List<string>(),
                cities = new List<string>()
            };
        }
        if (this.Emperor.isAlive())
        {
            this.RecordHistory(EmpireHistoryType.emperor_left_history, new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.Emperor.data.name
            });
        }
        else
        {
            this.RecordHistory(EmpireHistoryType.emperor_die_history, new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.Emperor.data.name
            });
        }

        this.Heir ??= (CheckHeir(EmpireHeirLawType.siblings)
                       ?? (CheckHeir(EmpireHeirLawType.grand_child_generation)
                           ?? (CheckHeir(EmpireHeirLawType.random)
                               ?? CheckHeir(default))));
        
        this.Emperor.RemoveEmpire();
        data.currentHistory.total_time = Date.getYearsSince(data.newEmperor_timestamp);
        data.history.Add(data.currentHistory);
        data.currentHistory = null;
        Emperor = null;
    }
    public bool IsNeedToSetPosthumous()
    {
        if (this.data.history.Count > 0)
        {
            foreach (EmpireCraftHistory cHistory in this.data.history)
            {
                Actor actor = World.world.units.get(cHistory.id);
                if (!string.IsNullOrEmpty(cHistory.emperor))
                {
                    if (string.IsNullOrEmpty(cHistory.miaohao_name))
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

    public int GetEmperorYear()
    {
        return Date.getYearsSince(this.data.newEmperor_timestamp) + 1;
    }

    public string GetYearNameWithTime()
    {
        if (this.data.has_year_name)
        {
            if (this.Emperor!=null)
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return this.data.year_name + "\u200A" + GetEmperorYear() + LM.Get("Year");
                }
            }
        }
        else
        {
            if (this.Emperor!=null)
            {
                if (this.data.year_name != "" || this.data.year_name != null)
                {
                    return Emperor.GetModName().firstName + "\u200A" + GetEmperorYear() + LM.Get("Year");
                }
            }
        }
        return "";
    }

    public ArmySystemType GetArmyType()
    {
        return this.data.armySystemType;
    }

    public bool CanJoinWar()
    {
        return Date.getMonthsSince(data.timestamp_invite_war_cool_down)>=3;
    }

    public void SetArmyType(ArmySystemType type)
    {
        this.data.armySystemType = type;
    }

    public void CreateNewEmpire(Kingdom kingdom, bool isSplit = false)
    {
        if (kingdom == null) return;
        if (kingdom.data == null) return;
        if (!kingdom.isAlive()) return;
        data.heir_type = EmpireHeirLawType.eldest_child;
        data.last_exam_timestamp = World.world.getCurWorldTime();
        data.armySystemType = ArmySystemType.募兵制;
        StartEmpireExam();
        if (ConfigData.speciesCulturePair.TryGetValue(kingdom.getSpecies(), out string culture)) {
            LogService.LogInfo(culture);
            data.centerOffice = new CenterOffice(culture);
            if (ConfigData.yearNameSubspecies.Contains(culture))
            {
                data.has_year_name = true;
            } else
            {
                data.has_year_name = false;
            }
        } else
        {
            LogService.LogInfo("Western");
            data.centerOffice = new CenterOffice("Western");
        }
        data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
        CoreKingdom = kingdom;
        kingdom.SetCountryLevel(countryLevel.countrylevel_0);
        if (CoreKingdom.getKingClan() != null) this.EmpireClan = this.CoreKingdom.getKingClan();
        else 
        {
            EmpireClan = null;
        }
        OriginalCapital = kingdom.capital;
        data.banner_icon_id = kingdom.data.banner_icon_id;
        data.banner_background_id = kingdom.data.banner_background_id;
        data.timestamp_established_time = World.world.getCurWorldTime();
        try
        {
            _capitalCenter = kingdom.capital.city_center;
        } catch
        {
            LogService.LogInfo("找不到帝国首都");
        }
        generateNewMetaObject();
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
            });
            NewEmperor(kingdom.king, !isSplit);
            kingdom.getKingClan().RecordHistoryEmpire(this);

        } catch
        {
            LogService.LogInfo("继承帝国信息失败");
        }

        kingdom.data.name = this.data.name;

    }
    public bool CanSetTitleToPreviousEmperor()
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

    public bool IsAllowToMakeYearName ()
    {
        return this.data.has_year_name;
    }
    public bool HasYearName()
    {
        return !string.IsNullOrEmpty(this.data.year_name);
    }

    public void create_year_name()
    {
        this.data.year_name = YearNameHelper.generateName();
        this.data.newEmperor_timestamp = World.world.getCurWorldTime();
    }

    private string GetDir(Vector2 v)
    {
        float ax = Math.Abs(v.x- _capitalCenter.x);
        float ay = Math.Abs(v.y- _capitalCenter.y);
        if (ax > ay)
        {
            return LM.Get(_capitalCenter.x > v.x ?"Eastern" : "Western");
        }
        else if (ay > ax)
        {
            return LM.Get(_capitalCenter.y > v.y ? "Northern" : "Southern");
        }
        else
        {
            return LM.Get("Later");
        }
    }

    private string CalcDir(Vector2 ori_v, Vector2 v)
    {
        float ax = Math.Abs(v.x- ori_v.x);
        float ay = Math.Abs(v.y- ori_v.y);
        if (ax > ay)
        {
            return LM.Get(ori_v.x > v.x ?"Eastern" : "Western");
        }
        else if (ay > ax)
        {
            return LM.Get(ori_v.y > v.y ? "Northern" : "Southern");
        }
        else
        {
            return LM.Get("Later");
        }
    }

    public bool IsRoyalBeenChanged()
    {
        return data.original_royal_been_changed;
    }

    public void SetEmpireName(string name)
    {
        string culture = ConfigData.speciesCulturePair.TryGetValue(CoreKingdom.getSpecies(), out var a) ? a : "default";
        this.data.name = name + "\u200A" + LM.Get($"{culture}_" + countryLevel.countrylevel_0.ToString());
        this.CoreKingdom.data.name = this.data.name;
    }

    public void CheckDissolve(Kingdom mainKingdom)
    {
        this.kingdoms_hashset.Remove(mainKingdom);
        mainKingdom.empireLeave(false);
        Kingdom heirEmpire = null;
        if (EmpireClan != null)
        {
            if (EmpireClan.isAlive())
            {
                foreach (Kingdom kingdom in kingdoms_hashset)
                {
                    if (kingdom.getKingClan() != null)
                        if (kingdom.getKingClan().getID() == EmpireClan.getID())
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
        ReplaceEmpire(heirEmpire);
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

    public void ReplaceEmpire(Kingdom newKingdom)
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
        if (newKingdom.getKingClan() == EmpireClan)
        {
            newEmpire.SetEmpireName(newEmpire.GetDir(this._empireCenter) + "\u200A" + GetEmpireName());
        }
        if (newKingdom.king.hasClan())
        {
            newKingdom.getKingClan().RecordHistoryEmpire(newEmpire);
            newEmpire.EmpireClan = newKingdom.getKingClan();
        }
        else
        {
            Clan clan = World.world.clans.newClan(newKingdom.king, true);
            newEmpire.EmpireClan = clan;
            clan.RecordHistoryEmpire(newEmpire);
        }
        TranslateHelper.LogministerAqcuireEmpire(newKingdom.king, newEmpire);
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
        foreach (Province province in this.ProvinceList)
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
        if (!pKingdom.isOpinionTowardsKingdomGood(CoreKingdom))
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
        if (this.CoreKingdom == null) return;
        if (this.CoreKingdom.data == null) return;
        this.data.kingdoms = new List<long>();
        foreach (Kingdom tKingdom in this.kingdoms_hashset)
        {
            if (tKingdom!=null)
            {
                this.data.kingdoms.Add(tKingdom.id);
            }
        }
        foreach(Province province in this.ProvinceList)
        {
            if (province!=null)
            {
                this.data.province_list.Add(province.id);
            }
        }

        if (this.Emperor != null)
            this.data.emperor = this.Emperor.data.id;
        else
            this.data.emperor = -1L;
        this.data.empire = this.CoreKingdom.data.id;
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
        this.data.original_capital = this.OriginalCapital.isAlive() ? this.OriginalCapital.data.id : -1L;
        try
        {
            this.data.empire_clan = this.EmpireClan == null ? -1L : this.EmpireClan.data.id;
        }
        catch
        {
            this.data.empire_clan = -1L;
            LogService.LogInfo("存储帝国氏族失败");
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
        this.Emperor = World.world.units.get(pData.emperor);
        this.CoreKingdom = World.world.kingdoms.get(pData.empire);
        this.EmpireClan = World.world.clans.get(pData.empire_clan);
        this.OriginalCapital = World.world.cities.get(pData.original_capital);
        this.Heir = World.world.units.get(pData.Heir);
        this.recalculate();
    }

    public void syncProvince()
    {
        this.ProvinceList = new List<Province>();
        foreach (long provinceID in this.data.province_list)
        {
            Province p = ModClass.PROVINCE_MANAGER.get(provinceID);
            if (p != null)
            {
                this.ProvinceList.Add(p);
            }
        }
    }

    public bool isNeedToGetBackProvince()
    {
        UpdateProvinceStatus();
        foreach (Province province in ProvinceList)
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
        if (this.ProvinceList == null) return;
        ListPool<Province> invalid_province = new ListPool<Province> { };
        foreach (Province province in ProvinceList)
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
            if (province2 == null) { this.ProvinceList.Remove(province2); }
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
            CheckDissolve(pKingdom);
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
        foreach (var tKingdom in tKingdoms)
        {
            tResult += tKingdom.getPopulationPeople();
        }
        return tResult;
    }


    public int countMaxPopulation()
    {
        int tResult = 0;
        List<Kingdom> tKingdoms = this.kingdoms_list;
        foreach (var tKingdom in tKingdoms)
        {
            tResult += tKingdom.getPopulationTotalPossible();
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
        return World.world.wars.hasWars(this.CoreKingdom);
    }

    // Token: 0x0600113A RID: 4410 RVA: 0x000C7F8F File Offset: 0x000C618F
    public IEnumerable<War> getWars(bool pRandom = false)
    {
        return World.world.wars.getWars(this.CoreKingdom, pRandom);
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
            for (int i = 0; i < tWars.Count(); i++)
            {
                War tWar = tWars.ElementAt(i);
                if (!tWar.hasEnded())
                {
                    for (int j = i + 1; j < tWars.Count(); j++)
                    {
                        War tWar2 = tWars.ElementAt(j);
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
            return this._lastEmpireCenter;

        if (this.countZones()<=0)
        {
            this._empireCenter = Globals.POINT_IN_VOID_2;
            return this._empireCenter;
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
        this._empireCenter.x = num / (float)zones.Count;
        this._empireCenter.y = num2 / (float)zones.Count;
        for (int j = 0; j < zones.Count; j++)
        {
            TileZone tileZone3 = zones[j];
            float num4 = Toolbox.SquaredDist((float)tileZone3.centerTile.x, (float)tileZone3.centerTile.y, this._empireCenter.x, this._empireCenter.y);
            if (num4 < num3)
            {
                tileZone = tileZone3;
                num3 = num4;
            }
        }
        this._empireCenter.x = tileZone.centerTile.posV3.x;
        this._empireCenter.y = tileZone.centerTile.posV3.y + 2f;
        this._lastEmpireCenter = this._empireCenter;
        this._units_dirty = false;
        return this._lastEmpireCenter;
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
        var allCities = this.CoreKingdom.cities;
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

            while (queue.Count > 0 && region.Count < _avgCitiesPerKingdom)
            {
                var curr = queue.Dequeue();
                foreach (var nei in curr.neighbours_cities)
                {
                    if (unassigned.Contains(nei))
                    {
                        region.Add(nei);
                        unassigned.Remove(nei);
                        queue.Enqueue(nei);
                        if (region.Count >= _avgCitiesPerKingdom) break;
                    }
                }
            }
            region = region.FindAll(c => c.getID() != CoreKingdom.capital.getID());
            CoreKingdom.getMaxCities();
            if (region.Count > 0)
            {
                City capital = region.GetRandom();
                List<Actor> SatisfiedCandidates = new List<Actor>();
                if (CoreKingdom.getKingClan()!=null)
                {
                    var RoyalCandidates = CoreKingdom.getKingClan().getUnits();
                    SatisfiedCandidates = RoyalCandidates.TakeWhile(c => c.isActor() && c.isAlive() && c.isAdult() && c.getID() != CoreKingdom.getID() && !c.isKing()).ToList();
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
                    if (CoreKingdom.king.getChildren().Contains(king))
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
                    newKingdom = SetEnfeoff(capital, king);
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
                        location = this.CoreKingdom.location,
                        color_special1 = this.CoreKingdom.kingdomColor.getColorText()
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

    public bool IsNeedToSetProvince()
    {
        foreach(City city in CoreKingdom.cities)
        {
            if (!city.hasProvince())
            {
                return true;
            }
        }
        return false;
    }



    public Kingdom SetEnfeoff(City capital, Actor king)
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

    public Sprite GetBackgroundSprite()
    {
        return World.world.alliances.getBackgroundsList()[this.data.banner_background_id];
    }

    public Sprite GetIconSprite()
    {
        return CoreKingdom.getSpriteIcon();
    }

    public override void Dispose()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_hashset.Clear();
        this.CoreKingdom = null;
        ProvinceList.Clear();
        if (!ModClass.ALL_HISTORY_DATA.ContainsKey(this.data.id))
        {
            ModClass.ALL_HISTORY_DATA.Add(this.data.id, this.data.history);
        }
    }


    public List<Kingdom> kingdoms_list = new List<Kingdom>();
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();

    public Kingdom CoreKingdom;

    public int power;
}

public static class ProvinceDivider
{
    public static List<Province> DivideIntoProvince(this Empire empire)
    {
        List<City> cities = empire.CoreKingdom.cities.FindAll(a=>!a.hasProvince());
        var result = new List<Province>();
        if (cities.Count == 0) return result;
        var remaining = new List<City>(cities);

        Dictionary<KingdomTitle, Province> kpPair = new Dictionary<KingdomTitle, Province>();
        foreach (City city in cities) 
        {
            if (city.isRekt()) continue;
            if (city.hasTitle())
            {
                KingdomTitle title = city.GetTitle();
                if (kpPair.TryGetValue(title, out var province1))
                {
                    province1.addCity(city);
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