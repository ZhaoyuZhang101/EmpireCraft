using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.UI.Windows;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using EmpireCraft.Scripts.System;
using NCMS;
using UnityEngine;
using Random = System.Random;

namespace EmpireCraft.Scripts.Layer;
// Token: 0x0200023B RID: 571
public class Empire : MetaObject<EmpireData>
{
    public const int PowerfulMinisterStageNone = 0;
    public const int PowerfulMinisterStageDominant = 1;
    public const int PowerfulMinisterStageDuke = 2;
    public const int PowerfulMinisterStageKing = 3;

    public BannerAsset BannerAsset;
    private Vector3 _lastEmpireCenter;
    private Vector3 _empireCenter;
    private bool _updatingHonoraryPeerages;
    private bool _completingMinisterUsurpation;
    public EmpireAddition Additions => data.additions;
    private readonly List<TileZone> _zoneScratch = new();
    private readonly int _avgCitiesPerKingdom = 3;
    public Clan EmpireClan;
    public int Mandate => data.Mandate;
    public Regime regime => CoreKingdom.GetRegime();
    public List<Kingdom> kingdoms_list = new List<Kingdom>();
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();
    public List<City> cities_list = new List<City>();
    
    //岁币国
    public List<Kingdom> given_Kingdoms = new List<Kingdom>();
    //朝贡国
    public List<Kingdom> taken_Kingdoms = new List<Kingdom>();
    
    public Religion Religion = null;
    public TemporaryFaction RunningTemporaryFaction = null;

    public Kingdom CoreKingdom;
    public Actor Emperor => CoreKingdom?.king;
    public int CurrentMoney => CoreKingdom?.GetMoney() ?? 0;
    private Vector3 _capitalCenter;
    public City OriginalCapital;
    public　SpecificClan EmpireSpecificClan => SpecificClanManager.Get(data.empire_specific_clan);
    
    public override MetaType meta_type => MetaTypeExtension.Empire;

    public bool HasEmperor()
    {
        return !Emperor.isRekt();
    }
    /// <summary>
    /// 增加或减少帝国正统值
    /// </summary>
    /// <param name="change">增加的数值</param>
    /// <returns></returns>
    public void AddMandate(int change)
    {
        data.Mandate+=change;
        if (Mandate < 0)
        {
            data.Mandate = 0;
        }
        if (Mandate > 100)
        {
            data.Mandate = 100;
        }
    }

    public bool IsNeedToIncreaseMandate()
    {
        return data.last_increase_mandate_timestamp<0||Date.getYearsSince(data.last_increase_mandate_timestamp)>=1;
    }
    public bool IsArchived()
    {
        return this.data?.archived ?? false;
    }
    public void Archive()
    {
        if (this.data != null) this.data.archived = true;
    }

    public void AddTaxRate(float addition = 0.1f)
    {
        if (data.TaxRate < 1.0f)
        {
            data.TaxRate += addition;
            if (data.TaxRate >= 1.0f)
            {
                data.TaxRate = 1.0f;
            }
        }
        //增加税收减少正统性
        AddMandate(-(int)(addition * 100));
    }

    public void SubTaxRate(float substraction = 0.1f)
    {
        if (data.TaxRate > 0.0f)
        {
            data.TaxRate  -= substraction;
            if (data.TaxRate <= 0.0f)
            {
                data.TaxRate =  0.0f;
            }
        }
        //减少税收增加正统性
        AddMandate((int)(substraction * 100));
    }

    public List<Kingdom> GetKingdomNeighbours()
    {
        List<Kingdom> neighbours = new();
        foreach (var kingdom in World.world.kingdoms)
        {
            if (kingdom.IsInSameEmpire(CoreKingdom))continue;
            if (IsNeighbourWith(kingdom))
            {
                if (!neighbours.Contains(kingdom))
                {
                    neighbours.Add(kingdom);
                }
            }
        }

        return neighbours;
    }
    
    public List<Actor> GetMembersWithTrait(string trait)
    {
        List<Actor>  list = new List<Actor>();
        foreach (Actor actor in EmpirePopulation.Enumerate(kingdoms_hashset))
        {
            if (actor.hasTrait(trait))
            {
                list.Add(actor);
            }
        }
        return list;
    }

    public override IEnumerable<City> getCities()
    {
        return cities_list;
    }

    public bool IsNeedToExam()
    {
        if (data.last_exam_timestamp <= 0) 
        {
            return true;
        }

        if (Date.getYearsSince(data.last_exam_timestamp)>=4)
        {
            return true;
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

    public bool IsNeedToOfficeExam()
    {
        if (data.last_exam_timestamp == -1L) return true;
        if (Date.getYearsSince(data.last_office_exam_timestamp)>=1)
        {
            return true;
        }
        return false;
    }

    public new long getTotalDeaths()
    {
        long deaths = 0;
        foreach(Kingdom kingdom in kingdoms_hashset)
        {
            deaths += kingdom.getTotalDeaths();
        }
        return deaths;
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
        if (CurrentMoney<0)
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

    public string GetEmpireFullName()
    {
        if (data == null) return "";
        string storedName = data.name?.Trim();
        if (string.IsNullOrWhiteSpace(storedName))
            storedName = CoreKingdom?.GetKingdomFullName() ?? "";

        Regime regime = CoreKingdom?.GetRegime();
        string suffixKey = regime == null ? "EmpireText" : $"{regime.type}_empire";
        string empireSuffix = LM.Get(suffixKey);
        if (string.IsNullOrWhiteSpace(empireSuffix) || string.Equals(empireSuffix, suffixKey, StringComparison.Ordinal))
            empireSuffix = LM.Get("EmpireText");
        if (string.IsNullOrWhiteSpace(empireSuffix)) return storedName;

        string[] parts = storedName.Split(new[] { '\u200A' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return storedName.EndsWith(empireSuffix, StringComparison.Ordinal) ? storedName : storedName + empireSuffix;

        string imperialName = string.Concat(parts.Take(parts.Length - 1));
        return imperialName.EndsWith(empireSuffix, StringComparison.Ordinal)
            ? imperialName
            : imperialName + empireSuffix;
    }

    private void MoveToEmpireCapital(Actor actor)
    {
        if (CoreKingdom?.capital?._city_tile==null) return;
        actor.joinCity(this.CoreKingdom.capital);
        actor.goTo(this.CoreKingdom.capital._city_tile);
    }

    private void SyncCoreKingdomReference()
    {
        if (data == null) return;
        data.empire = CoreKingdom?.id ?? -1L;
        data.last_core_kingdom_id = CoreKingdom?.id ?? data.last_core_kingdom_id;
    }
    //提供岁币
    public void StartToGive()
    {
        data.timestamp_given_time = World.world.getCurWorldTime();
        var tempGiven = given_Kingdoms.ToList();
        foreach (var kingdom in tempGiven)
        {
            if (AncientWarfareCompatibility.Owns(kingdom)) continue;
            if (CoreKingdom.GetMoney() <= 0)
            {
                kingdom.RemoveGivenAlliance();
                continue;
            }
            CoreKingdom.SubMoney(countUnits()/2);
            kingdom.AddMoney(countUnits()/2);
            if (kingdom.NeedToRemoveGivenAlliance())
            {
                kingdom.RemoveGivenAlliance();
            }
        }
    }
    public bool IsNeedToGive()
    {
        return Date.getYearsSince(data.timestamp_given_time) >= 1;
    }
    
    //新皇登基
    public void NewEmperor(Actor actor, bool isNew = false)
    {
        if (actor == null) return;
        actor.SetEmpire(this);
        if (!isNew)
        {
            AddMandate(-20);
        }
        string nameEmpire = "";
        SpecificClan previousRoyalClan = EmpireSpecificClan;
        Clan previousEmpireClan = EmpireClan;
        actor.CheckSpecificClan();
        //检查帝国分裂
        var currentSpecificClan = actor.GetSpecificClan();
        if (currentSpecificClan != previousRoyalClan && data.empire_specific_clan != -1L &&
            IsDynasticSuccessor(actor, previousRoyalClan, previousEmpireClan))
        {
            currentSpecificClan = RestoreDynasticSuccessorClan(actor, previousRoyalClan, previousEmpireClan);
            LogService.LogInfo($"修复皇位继承宗族：{actor.getName()} 继承原皇族，不更改国号");
        }
        if (currentSpecificClan == null)
        {
            LogService.LogError($"新皇 {actor.getName()} 无法建立宗族身份，取消帝国继承处理");
            return;
        }
        if (currentSpecificClan != previousRoyalClan && data.empire_specific_clan != -1L)
        {
            LogService.LogInfo("篡位逻辑");
            LogService.LogInfo($"上一任皇室: {EmpireSpecificClan?.name??"None"}");
            LogService.LogInfo($"皇室活人: {EmpireSpecificClan?.Count??0}");
            LogService.LogInfo($"合法继承人: {EmpireSpecificClan?.all_valid_members.Count??0}");
            if (!_completingMinisterUsurpation && Mandate >= 70)
            {
                if (EmpireSpecificClan?.all_valid_members.Any()??false)
                {
                    LogService.LogInfo("存在合法继承人");
                    var validEmperor = EmpireSpecificClan.all_valid_members?.First()._actor;
                    var newEmpire = StartSplit(validEmperor);
                    LogService.LogInfo($"开始分裂{actor.id}{actor.name}");
                    if (newEmpire != null)
                    {
                        War war = World.world.diplomacy.startWar(newEmpire.CoreKingdom,this.CoreKingdom, WarTypeLibrary.normal);
                        war.SetEmpireWarType(EmpireWarType.帝国正统, pre: CoreKingdom.GetEmpireCraftCulture(true));
                    }
                }
            }
            AddMandate(-30);
            foreach (var k in kingdoms_list.ToList())
            {
                if (_completingMinisterUsurpation) break;
                if (k.IsEmpire()) continue;
                if (!k.hasKing()) continue;
                var clan = k.king.GetSpecificClan();
                if (clan.id == data.empire_specific_clan)
                {
                    leave(k);
                    DiplomacyHelpers.wars.newWar(k, CoreKingdom, WarTypeLibrary.normal);
                }
            }
            if (currentSpecificClan.HasHistoryEmpire())
            {
                var historyRecord = currentSpecificClan.GetHistoryEmpire();
                data.directPre = GetDir(historyRecord.pos);
                SetEmpireName(historyRecord.name);
            }
            if (CoreKingdom.GetRegime().type == RegimeType.LvLing)
            {
                data.directPre = "";
                nameEmpire = actor.culture.getOnomasticData(MetaType.Kingdom).generateName();
                SetEmpireName(nameEmpire);
                currentSpecificClan.RecordHistoryEmpire(this, CoreKingdom.capital);
            }
            isNew = true;
            data.history_emperrors.Clear();
            CoreKingdom.updateColor(getColorLibrary().getNextColor(actor.getActorAsset()));
            updateColor(CoreKingdom.getColor());
            foreach (var tk in taken_Kingdoms.ToList())
            {
                if (AncientWarfareCompatibility.Owns(tk)) continue;
                tk.RemoveTakenAlliance();
            }

            foreach (var k in kingdoms_list.ToList())
            {
                if (_completingMinisterUsurpation) break;
                if (!k.isOpinionTowardsKingdomGood(CoreKingdom)&&Mandate<20)
                {
                    this.leave(k);
                }
            }
        } 
        
        data.empire_specific_clan = currentSpecificClan.id;
        EmpireClan = actor.clan;
        CoreKingdom?.SetSpecificClan(currentSpecificClan);
        //设定天子身份并移居首都
        if (actor.isOfficer())
        {
            actor.RemoveIdentity();
            actor.SetPeeragesLevel(PeeragesLevel.peerages_0);
        }
        
        actor.data.renown += 20;
        MoveToEmpireCapital(actor);
        create_year_name();
        if (data.has_year_name)
        {
            //公屏提示
            TranslateHelper.LogNewEmperor(actor, CoreKingdom.capital, data.year_name); 
        }
        else
        {
            TranslateHelper.LogNewEmperorWest(actor, CoreKingdom.capital);
        }
        
        
        //记录历史
        this.RecordNewEmperorHistory(isNew);
    }

    private bool IsDynasticSuccessor(Actor actor, SpecificClan previousRoyalClan, Clan previousEmpireClan)
    {
        if (actor == null || previousRoyalClan == null) return false;
        if (actor.GetSpecificClan() == previousRoyalClan) return true;
        if (previousEmpireClan != null && actor.clan == previousEmpireClan) return true;

        PersonalClanIdentity successorIdentity = actor.GetPersonalIdentity();
        EmpireCraftHistory previousReign = data.history?.LastOrDefault(history => history != null && history.id > 0);
        if (previousReign == null) return false;
        SpecificClanManager._actorToPersonLookup.TryGetValue(previousReign.id, out var previousIdentity);
        previousIdentity ??= World.world.units.get(previousReign.id)?.GetPersonalIdentity();
        if (previousIdentity != null && successorIdentity != null)
        {
            if (successorIdentity.father == previousIdentity.id || successorIdentity.mother == previousIdentity.id ||
                previousIdentity.children.Contains(successorIdentity.id)) return true;
        }

        Actor previousEmperor = World.world.units.get(previousReign.id);
        return actor.getParents().Any(parent => parent != null && parent.id == previousReign.id) ||
               (previousEmperor?.getChildren().Any(child => child != null && child.id == actor.id) ?? false);
    }

    private static SpecificClan RestoreDynasticSuccessorClan(Actor actor, SpecificClan previousRoyalClan,
        Clan previousEmpireClan)
    {
        if (actor == null || previousRoyalClan == null) return actor?.GetSpecificClan();
        PersonalClanIdentity identity = actor.GetPersonalIdentity();
        if (identity == null)
        {
            previousRoyalClan.addActor(actor);
            identity = actor.GetPersonalIdentity();
        }
        else if (identity.specific_clan_id != previousRoyalClan.id)
        {
            identity._specificClan?._cache.Remove(identity.id);
            identity.specific_clan_id = previousRoyalClan.id;
            previousRoyalClan.Upsert(identity);
            actor.SetPersonalIdentity(identity);
        }

        if (previousEmpireClan != null)
        {
            if (actor.clan != previousEmpireClan) actor.setClan(previousEmpireClan);
            previousEmpireClan.SetSpecificClan(previousRoyalClan);
        }
        return previousRoyalClan;
    }
    public bool IsNeedToChooseLovers()
    {
        if (Date.getYearsSince(data.last_select_lovers_timestamp) >= 3)
        {
            data.last_select_lovers_timestamp = World.world.getCurWorldTime();
            return true;
        }
        else
        {
            return false;
        }
    }
    private Empire RebuildSecondEmpire(City startProvince, Actor newEmperor)
    {
        var kingdom = startProvince.makeOwnKingdom(newEmperor);
        kingdom.setCapital(startProvince);
        var newEmpire = ModClass.EMPIRE_MANAGER.NewEmpire(kingdom);
        if (newEmpire == null)
        {
            return null;
        }
        newEmpire.UpdateCapital(this.OriginalCapital);
        newEmpire.data.history.InsertRange(0, this.data.history);
        newEmpire.SetEmpireName(this.GetEmpireName());
        newEmpire.data.directPre = this.CalcDir(kingdom.capital.city_center, CoreKingdom.capital.city_center);
        
        var provinces = new List<City>() {};
        StartSplit(newEmpire, startProvince, ref provinces);
        
        return newEmpire;
    }
    //帝国分裂方法
    private Empire StartSplit(Actor newEmperor)
    {
        Empire newEmpire = null;
        if (newEmperor.isRekt()) return null;
        if (cities_list.Count > 1)
        {
            foreach (City province in cities_list)
            {
                if (province == null) continue;
                if (province.isCapitalCity()&&province.kingdom.IsEmpire()) continue;
                if (!province.isAlive()) continue;
                if (!province.hasLeader()) continue;
                newEmpire = RebuildSecondEmpire(province, newEmperor);
                break;
            }
        }
        AddRenown(-(int)(this.CoreKingdom.getRenown() * 0.5));
        return newEmpire;
    }
    private void StartSplit(Empire empire, City start, ref List<City> pJoinedProvinceList, double possibility=0.5f)
    {
        if (start.isCapitalCity()&&start.kingdom.IsEmpire()&&start.kingdom.GetEmpire()!=empire) return;
        if (pJoinedProvinceList.Contains(start)) return;
        Random rand = new Random();
        double randomValue = rand.NextDouble(); // [0.0, 1.0)
        LogService.LogInfo("当前随机数: "+randomValue);
        LogService.LogInfo("当前概率: "+ possibility);
        if (randomValue >= possibility) return;
        if (empire == null) return;
        if (pJoinedProvinceList.Count >= this.cities_list.Count-1) return;
        LogService.LogInfo("存在差集");
        foreach (City province in this.cities_list.ToList())
        {
            if (province.isCapitalCity()&&province.kingdom.IsEmpire()) continue;
            if (start.neighbours_cities.Contains(province))
            {
                try
                {
                    province.joinAnotherKingdom(empire.CoreKingdom);
                    StartSplit(empire, province, ref pJoinedProvinceList, possibility);
                }
                catch (Exception e) 
                {
                    LogService.LogError($"帝国省份转化失败: {e}");
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

    public void EmperorLeft()
    {
        if (this.Emperor == null) return;
        if (this.Emperor.data == null) return;
        data.currentHistory ??= new EmpireCraftHistory
        {
            id = this.Emperor.data.id,
            year_name = data.year_name,
            emperor = this.Emperor.data.name,
            empire_name = this.GetEmpireName(),
            empire_full_name = this.GetEmpireFullName(),
            dynasty_name = this.GetEmpireName(),
            royal_surname = this.Emperor.GetSpecificClan()?.name??"",
            miaohao_name = "",
            shihao_name = "",
            descriptions = new List<HistoryDescription>()
        };
        this.RecordHistory(
            Emperor.isAlive() ? EmpireHistoryType.emperor_left_history : EmpireHistoryType.emperor_die_history,
            new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.Emperor.data.name
            });
        data.history_emperrors.Add(Emperor?.name);
        if (string.IsNullOrWhiteSpace(data.currentHistory.empire_full_name))
            data.currentHistory.empire_full_name = GetEmpireFullName();
        this.Emperor.RemoveEmpire();
        data.empire_specific_clan = Emperor?.GetSpecificClan()?.id??-1L;
        LogService.LogInfo("上一任皇氏族记录:"+Emperor?.GetSpecificClan().name);
        data.currentHistory.total_time = Date.getYearsSince(data.newEmperor_timestamp);
        data.history.Add(data.currentHistory);
        data.currentHistory = null;
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

    public bool CanJoinWar()
    {
        return Date.getMonthsSince(data.timestamp_invite_war_cool_down)>=3;
    }

    public void CreateNewEmpire(Kingdom kingdom, bool isSplit = false)
    {
        if (kingdom == null) return;
        if (kingdom.data == null) return;
        if (!kingdom.isAlive()) return;
        if (!kingdom.hasKing()) return;
        kingdom.ai.setTask("do_mod_empire_beh");
        kingdom.GetOrCreate().isEmpire = true;
        data.history_emperrors = new List<string>();
        data.heir_type = EmpireHeirLawType.eldest_child;
        data.last_exam_timestamp = World.world.getCurWorldTime();
        StartEmpireExam();
        Regime regime = kingdom.GetRegime();
        if (regime != null)
        {
            if (regime.type == RegimeType.YouMu)
            {
                data.directPre = LM.Get("great");
            }
            data.has_year_name = regime.HasEraName();
            regime.GetPlayerFactions().ForEach(f=>
            {
                f.EmpireId = this.getID();
                f.FixMissedTemporaryFactions();
                f.TemporaryFactions.ForEach(tf=>tf.Init(f));
            });
        }
        data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
        CoreKingdom = kingdom;
        SyncCoreKingdomReference();
        data.centerOffice = new CenterOffice();
        data.centerOffice.Init(CoreKingdom);
        kingdom.SetLevel(0);
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
        } catch (Exception e)
        {
            LogService.LogError($"读取帝国首都失败: {e}");
        }
        generateNewMetaObject();
        string empireName = kingdom.GetKingdomName();
        EmpireCore riseCore = EmpireCoreManager.GetRiseCandidateCore(kingdom);
        if (riseCore != null && !string.IsNullOrWhiteSpace(riseCore.name))
        {
            empireName = riseCore.name;
        }
        if (kingdom.king.HasTitle())
        {
            empireName = kingdom.king.GetTitle();
        }
        try
        {
            if (kingdom.getKingClan() != null)
            {
                if (kingdom.king?.GetSpecificClan()?.HasHistoryEmpire()??false)
                {
                    var historyRecord = kingdom.king.GetSpecificClan().GetHistoryEmpire();
                    this.data.directPre = GetDir(historyRecord.pos);
                    empireName = historyRecord.name;
                }
            }

        } catch (Exception e)
        {
            LogService.LogError($"读取氏族历史帝国名称失败: {e}");
        }
        SetEmpireName(empireName);
        try
        {
            this.data.currentHistory = new EmpireCraftHistory
            {
                id = kingdom.king?.data?.id??-1L,
                year_name = data.year_name,
                emperor = kingdom.king?.getName()??"",
                empire_name = this.GetEmpireName(),
                empire_full_name = this.GetEmpireFullName(),
                dynasty_name = this.GetEmpireName(),
                royal_surname = kingdom.king?.GetSpecificClan()?.name??"",
                is_first = true,
                miaohao_name = "",
                shihao_name = "",
                descriptions = new List<HistoryDescription>(),
            };
            if (data.has_year_name)
            {
                this.RecordHistory(EmpireHistoryType.new_empire_history, new Dictionary<string, string>()
                {
                    ["actor"] = kingdom.king?.getName()??"",
                    ["place"] = kingdom.capital.GetCityName(),
                    ["name"] = GetEmpireName(),
                });
            }
            else
            {
                this.RecordHistory(EmpireHistoryType.new_empire_history_west, new Dictionary<string, string>()
                {
                    ["actor"] = kingdom.king?.getName()??"",
                    ["place"] = kingdom.capital.GetCityName(),
                    ["name"] = GetEmpireName(),
                });
            }
            NewEmperor(kingdom.king, !isSplit);
            kingdom.king?.GetSpecificClan()?.RecordHistoryEmpire(this, CoreKingdom.capital);

        } catch (Exception e)
        {
            LogService.LogError($"继承帝国信息失败: {e}");
        }

        kingdom.data.name = this.data.name;
        World.world.zone_calculator.dirtyAndClear();
    }
    public bool CanSetTitleToPreviousEmperor()
    {
        if (this.data.history.Count > 0)
        {
            foreach(EmpireCraftHistory cHistory in this.data.history)
            {
                if (cHistory is { emperor: not null } && cHistory.emperor != "" && !World.world.units.get(cHistory.id)!.isAlive())
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

    public bool AddCabinetMember(Actor actor)
    {
        if (actor == null) return false;
        if (!actor.HasOfficeIdentity()) return false;
        OfficeIdentity identity = actor.GetIdentity();
        if (identity.IsCabinet()) return false;
        if (data.CabinetMembers.Contains(actor.id)) return false;
        identity.EnterCabinet();
        data.CabinetMembers.Add(actor.id);
        actor.RecordPersonalHistory(LM.Get("personal_history_joined_cabinet"));
        return true;
    }

    public bool SetCabinetLeader(Actor actor)
    {
        if (actor == null) return false;
        if (!actor.HasOfficeIdentity()) return false;
        if (GetCabinetLeader()?.id == actor.id) return false;
        OfficeIdentity identity = actor.GetIdentity();
        bool wasCabinetMember = data.CabinetMembers.Contains(actor.id);
        RemoveCabinetMember(actor);
        data.CabinetMembers.Insert(0, actor.id);
        identity.EnterCabinet();
        if (!wasCabinetMember)
        {
            actor.RecordPersonalHistory(LM.Get("personal_history_joined_cabinet"));
        }
        actor.RecordPersonalHistory(LM.Get("personal_history_became_cabinet_leader"));
        return true;
    }

    public Actor GetCabinetLeader()
    {
        return data.CabinetMembers.Count<=0?null:World.world.units.get(data.CabinetMembers[0]);
    }

    public List<Actor> GetCabinetMembers()
    {
        return data.CabinetMembers.Select(a=>World.world.units.get(a)).ToList();
    }

    public bool RemoveCabinetMember(Actor actor)
    {
        if (actor.HasOfficeIdentity())
        {
            OfficeIdentity identity = actor.GetIdentity();
            identity.ExitCabinet();
        }
        data.CabinetMembers.Remove(actor.id);
        return true;
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
        var core = CoreKingdom;
        if (core == null) return;
        Regime regime = core.GetRegime();
        var originalName = name;
        if (regime != null)
        {
            if (regime.centre_empire_separate)
            {
                originalName += "\u200A" + LM.Get($"{regime.type}_empire");
            }
            else
            {
                originalName += "\u200A" + LM.Get(EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(core).ToString());
            }
        }
        data.name = string.IsNullOrEmpty(data.directPre)?originalName: string.Join("\u200A", data.directPre, originalName);
        if (core.data != null) core.data.name = data.name;
        if (data.currentHistory != null && data.currentHistory.id == Emperor?.id)
            data.currentHistory.empire_full_name = GetEmpireFullName();
    }

    public void CheckDissolve(Kingdom mainKingdom)
    {
        if (mainKingdom != null)
        {
            this.kingdoms_hashset.Remove(mainKingdom);
            mainKingdom.EmpireLeave(false);
            recalculate();
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
            LogService.LogInfo("解散帝国1");
        }
        Kingdom heirEmpire = null;
        if (EmpireSpecificClan != null)
        {
            foreach (Kingdom kingdom in kingdoms_list)
            {
                if (kingdom.isRekt()) continue;
                if (kingdom.king.HasSpecificClan())
                    if (kingdom.king.GetSpecificClan() == EmpireSpecificClan)
                    {
                        if (heirEmpire.isRekt() || kingdom.countTotalWarriors() > heirEmpire.countTotalWarriors())
                        {
                            heirEmpire = kingdom;
                        }
                    }
            }
        }
        if (heirEmpire == null)
        {
            ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
            LogService.LogInfo("解散帝国2");
            return;
        }
        ReplaceEmpire(heirEmpire);
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
        Empire newEmpire = ModClass.EMPIRE_MANAGER.NewEmpire(newKingdom);
        if (newEmpire == null)
        {
            return;
        }
        newEmpire.data.history.InsertRange(0, data.history);
        newEmpire.SetEmpireName(newKingdom.GetKingdomName());
        newKingdom.GetOrCreate().isEmpire = true;
        newEmpire.data.Mandate = data.Mandate - 50;
        data.directPre = "";
        if (newKingdom.capital.HasKingdomName()) 
        {
            SetEmpireName(newKingdom.capital.SelectKingdomName());
        }

        if (newKingdom.king.HasSpecificClan())
        {
            if (newKingdom.king.GetSpecificClan().HasHistoryEmpire())
            {
                var historyRecord = newKingdom.king.GetSpecificClan().GetHistoryEmpire();
                data.directPre = newEmpire.GetDir(historyRecord.pos);
                string empireName = historyRecord.name;
                newEmpire.SetEmpireName(empireName);
            }
        }
        if (newKingdom.king.HasTitle())
        {
            newEmpire.SetEmpireName(newKingdom.king.GetTitle());
        }

        if (newKingdom.king.HasSpecificClan())
        {
            if (newKingdom.king.GetSpecificClan() == EmpireSpecificClan)
            {
                data.directPre = newEmpire.GetDir(this._empireCenter);
                newEmpire.SetEmpireName(GetEmpireName());
            }
        }
        if (newKingdom.king.HasSpecificClan())
        {
            newKingdom.king.GetSpecificClan().RecordHistoryEmpire(this, newEmpire.CoreKingdom.capital);
            newEmpire.EmpireClan = newKingdom.getKingClan();
            newEmpire.data.empire_specific_clan = newKingdom.king.GetSpecificClan().id;
        }
        else
        {
            newKingdom.king.CheckSpecificClan();
            newEmpire.EmpireClan = newKingdom.getKingClan();
            newKingdom.king.GetSpecificClan().RecordHistoryEmpire(this, newEmpire.CoreKingdom.capital);
            newEmpire.data.empire_specific_clan = newKingdom.king.GetSpecificClan().id;
        }
        TranslateHelper.LogministerAqcuireEmpire(newKingdom.king, newEmpire);
        foreach (Kingdom kingdom in kingdoms_hashset)
        {
            newEmpire.kingdoms_hashset.Add(kingdom);
            kingdom.EmpireJoin(newEmpire);
            newEmpire.data.timestamp_member_joined = World.world.getCurWorldTime();
            
        }
        newEmpire.create_year_name();
        newEmpire.recalculate();
        if (data.has_year_name)
        {
            TranslateHelper.LogNewEmperor(newKingdom.king, newKingdom.capital, newEmpire.data.year_name);
        }
        else
        {
            TranslateHelper.LogNewEmperorWest(newKingdom.king, newKingdom.capital);
        }
        
        newKingdom.data.name = newEmpire.data.name;
        ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
    }
    public sealed override void setDefaultValues()
    {
        base.setDefaultValues();
    }
    public override int countTotalMoney()
    {
        // Base statistics dispatch through our safe getUnits; kingdom counters bypass it.
        return base.countTotalMoney();
    }
    public override int countHappyUnits()
    {
        return base.countHappyUnits();
    }
    public override int countSick()
    {
        return base.countSick();
    }
    public override int countHungry()
    {
        return base.countHungry();
    }
    public override int countStarving()
    {
        return base.countStarving();
    }
    public override int countChildren()
    {
        return base.countChildren();
    }
    public override int countAdults()
    {
        return base.countAdults();
    }
    public override int countHomeless()
    {
        return base.countHomeless();
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
        return base.countMales();
    }

    // Token: 0x0600111B RID: 4379 RVA: 0x000C7578 File Offset: 0x000C5778
    public override int countFemales()
    {
        return base.countFemales();
    }

    // Token: 0x0600111C RID: 4380 RVA: 0x000C75B4 File Offset: 0x000C57B4
    public override int countHoused()
    {
        return base.countHoused();
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
        data.founder_kingdom_name = pKingdom.data.name;
        data.founder_kingdom_id = pKingdom.getID();
        EmpireData empireData = data;
        Actor king = pKingdom.king;
        empireData.founder_actor_name = king?.getName();
        empireData.founder_actor_id = king?.getID() ?? -1L;
        join(pKingdom, true, true);
    }

    public void update()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        if (data == null || World.world == null || _updatingHonoraryPeerages) return;
        Kingdom coreKingdom = CoreKingdom;
        if (coreKingdom == null || coreKingdom.isRekt()) return;
        Regime regime = coreKingdom.GetRegime();
        if (regime?.type == RegimeType.LvLing)
        {
            UpdatePowerfulMinister(regime);
        }
        else if (regime != null && data.powerful_minister_id > 0)
        {
            ClearPowerfulMinister();
        }
        if (regime?.enfeoff_virtual_only != true) return;
        bool legalPeeragesDue = data.last_legal_peerage_timestamp < 0 ||
            Date.getMonthsSince(data.last_legal_peerage_timestamp) >= 1;
        bool honoraryPeeragesDue = regime.enable_auto_honorary_peerages &&
            (data.last_honorary_peerage_timestamp < 0 || Date.getYearsSince(data.last_honorary_peerage_timestamp) >= 1);
        if (!legalPeeragesDue && !honoraryPeeragesDue) return;

        _updatingHonoraryPeerages = true;
        try
        {
            if (legalPeeragesDue)
            {
                data.last_legal_peerage_timestamp = World.world.getCurWorldTime();
                ProcessLegalPeerageSuccession();
                if (regime.enable_auto_honorary_peerages) ProcessHonoraryPeerageInheritance(regime);
            }
            if (!honoraryPeeragesDue) return;
            data.last_honorary_peerage_timestamp = World.world.getCurWorldTime();

            List<Actor> candidates = getUnits()
                .Where(a => a != null && !a.isRekt() && !a.isKing() && !a.HasHonoraryPeerage())
                .ToList();
            foreach (string peerageKey in regime.virtual_honorary_peerages ?? new List<string>())
            {
                if (IsHonoraryPeerageReserved(peerageKey)) continue;
                Actor candidate = GetHonoraryPeerageCandidate(candidates, peerageKey);
                if (candidate != null && candidate.GrantHonoraryPeerage(this, peerageKey)) return;
            }
        }
        finally
        {
            _updatingHonoraryPeerages = false;
        }
    }

    private Actor GetHonoraryPeerageCandidate(IEnumerable<Actor> candidates, string peerageKey)
    {
        bool IsCivilMerit(Actor a) => !a.isWarrior() && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 500 && a.renown >= 100;
        bool IsMilitaryMerit(Actor a) => a.isWarrior() && a.renown >= 120;
        return peerageKey switch
        {
            "tang_honorary_anle_gong" => candidates.Select(actor => (actor, priority: GetAnlePeeragePriority(actor)))
                .Where(item => item.priority > 0).OrderByDescending(item => item.priority)
                .ThenBy(item => item.actor.GetPersonalIdentity()?.rank ?? int.MaxValue)
                .ThenBy(item => item.actor.id).Select(item => item.actor).FirstOrDefault(),
            "tang_honorary_wenan_gong" or "tang_honorary_longxi_gong" => candidates.Where(IsCivilMerit).OrderByDescending(a => a.renown).FirstOrDefault(),
            "tang_honorary_wujun_hou" => candidates.Where(a => IsMilitaryMerit(a) && a.renown >= 250).OrderByDescending(a => a.renown).FirstOrDefault(),
            "tang_honorary_zhongyong_hou" => candidates.Where(a => IsMilitaryMerit(a) && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 150).OrderByDescending(a => a.renown).FirstOrDefault(),
            "tang_honorary_huaide_hou" => candidates.Where(a => !a.isWarrior() && (a.GetOrCreate().officeIdentity?.TotalPerformance ?? 0) >= 350).OrderByDescending(a => a.GetOrCreate().officeIdentity.TotalPerformance).FirstOrDefault(),
            _ => null
        };
    }

    public bool IsHonoraryPeerageReserved(string peerageKey)
    {
        return PeerageSuccessionRules.IsReserved(data.honorary_peerage_holders, peerageKey) ||
            getUnits().Any(actor => actor != null && !actor.isRekt() && actor.HasHonoraryPeerage(this) &&
                actor.GetOrCreate().honorary_peerage_key == peerageKey);
    }

    public int GetAnlePeeragePriority(Actor actor)
    {
        if (actor == null || actor.isRekt()) return 0;
        return AnlePeerageRules.CandidatePriority(data.defeated_empire_houses, id, actor.id,
            actor.GetSpecificClan()?.id ?? -1L, actor.kingdom?.GetEmpire() == this, actor.isKing());
    }

    public void RememberDefeatedEmpireHouse(DefeatedEmpireHouse house)
    {
        if (house == null || house.empire_id <= 0 || house.empire_id == id) return;
        data.defeated_empire_houses ??= new List<DefeatedEmpireHouse>();
        if (data.defeated_empire_houses.Any(old => old != null && old.empire_id == house.empire_id &&
            old.emperor_id == house.emperor_id && old.royal_clan_id == house.royal_clan_id)) return;
        data.defeated_empire_houses.Add(new DefeatedEmpireHouse
        {
            empire_id = house.empire_id,
            emperor_id = house.emperor_id,
            royal_clan_id = house.royal_clan_id
        });
    }

    public void RememberHonoraryPeerageHolder(string peerageKey, Actor holder)
    {
        holder.CheckSpecificClan(false);
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        data.honorary_peerage_holders[peerageKey] = holder.id;
        data.honorary_peerage_holder_identities[peerageKey] = holder.GetPersonalIdentity()?.id ?? -1L;
        data.honorary_peerage_holder_names[peerageKey] = holder.getName();
    }

    private static PersonalClanIdentity ResolvePeerageIdentity(long actorId, long identityId)
    {
        PersonalClanIdentity identity = SpecificClanManager.getPerson(identityId);
        if (identity != null) return identity;
        if (SpecificClanManager._actorToPersonLookup.TryGetValue(actorId, out identity)) return identity;
        return World.world.units.get(actorId)?.GetPersonalIdentity();
    }

    private Actor FindPeerageDescendant(PersonalClanIdentity predecessor, Func<Actor, bool> isAvailable)
    {
        if (predecessor?._specificClan == null) return null;
        return PeerageSuccessionRules.FindDescendant(predecessor,
            person => SpecificClanManager.getChildren(person).Select(item => item.Item2)
                .OrderBy(child => child.rank).ThenBy(child => child.id),
            person => person.specific_clan_id == predecessor.specific_clan_id && person.is_main && person.IsHeirPriority(),
            person => person.CanHeir(predecessor) && isAvailable(person._actor),
            person => person.id)?._actor;
    }

    private void ProcessHonoraryPeerageInheritance(Regime regime)
    {
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        // Backfill legacy saves without letting duplicate grants overwrite the recorded lineage.
        foreach (var group in getUnits().Where(a => a != null && !a.isRekt() && a.HasHonoraryPeerage(this))
            .GroupBy(a => a.GetOrCreate().honorary_peerage_key).ToList())
        {
            if (!data.honorary_peerage_holders.TryGetValue(group.Key, out long registeredId))
            {
                Actor first = group.OrderBy(actor => actor.id).First();
                RememberHonoraryPeerageHolder(group.Key, first);
                registeredId = first.id;
            }
            foreach (Actor actor in group)
            {
                if (actor.id == registeredId) RememberHonoraryPeerageHolder(group.Key, actor);
                else
                {
                    actor.GetOrCreate().honorary_peerage_key = "";
                    actor.GetOrCreate().honorary_peerage_empire_id = -1L;
                }
            }
        }

        foreach (string peerageKey in (regime.virtual_honorary_peerages ?? new List<string>()).ToList())
        {
            if (!data.honorary_peerage_holders.TryGetValue(peerageKey, out long holderId)) continue;
            Actor holder = World.world.units.get(holderId);
            if (holder != null && !holder.isRekt()) continue;
            data.honorary_peerage_holder_identities.TryGetValue(peerageKey, out long identityId);
            PersonalClanIdentity predecessor = ResolvePeerageIdentity(holderId, identityId);
            Actor heir = FindPeerageDescendant(predecessor, a => a != null && !a.isRekt() && !a.isKing() &&
                a.kingdom?.GetEmpire() == this && !a.HasHonoraryPeerage());
            if (heir == null) continue;

            if (holder != null)
            {
                holder.GetOrCreate().honorary_peerage_key = "";
                holder.GetOrCreate().honorary_peerage_empire_id = -1L;
            }
            heir.GetOrCreate().honorary_peerage_key = peerageKey;
            heir.GetOrCreate().honorary_peerage_empire_id = data.id;
            data.honorary_peerage_holder_names.TryGetValue(peerageKey, out string predecessorName);
            RememberHonoraryPeerageHolder(peerageKey, heir);
            TranslateHelper.LogPeerageInherited(heir, this, LM.Get(peerageKey), predecessor,
                holderId, predecessorName ?? holder?.getName());
        }
    }

    public bool TryRepairState(string reason = "")
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return true;
        if (IsArchived() || data == null || World.world == null)
        {
            return false;
        }

        kingdoms_hashset ??= new HashSet<Kingdom>();
        kingdoms_list ??= new List<Kingdom>();
        cities_list ??= new List<City>();
        given_Kingdoms ??= new List<Kingdom>();
        taken_Kingdoms ??= new List<Kingdom>();
        data.kingdoms ??= new List<long>();
        data.cities ??= new List<long>();
        data.given_Kingdoms ??= new List<long>();
        data.taken_Kingdoms ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.PreviousYearsMoney ??= new List<int>();
        data.honorary_peerage_holders ??= new Dictionary<string, long>();

        bool repaired = false;
        HashSet<Kingdom> rebuiltKingdoms = new HashSet<Kingdom>();

        foreach (Kingdom kingdom in kingdoms_hashset)
        {
            if (kingdom != null && !kingdom.isRekt())
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (Kingdom kingdom in kingdoms_list)
        {
            if (kingdom != null && !kingdom.isRekt())
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (long kingdomId in data.kingdoms)
        {
            Kingdom kingdom = World.world.kingdoms.get(kingdomId);
            if (kingdom != null && !kingdom.isRekt())
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            if (kingdom.GetEmpireID() == this.id)
            {
                rebuiltKingdoms.Add(kingdom);
            }
        }

        if (!kingdoms_hashset.SetEquals(rebuiltKingdoms))
        {
            kingdoms_hashset = rebuiltKingdoms;
            repaired = true;
        }

        recalculate();

        List<City> rebuiltCities = new List<City>();
        HashSet<long> seenCityIds = new HashSet<long>();
        foreach (Kingdom kingdom in kingdoms_list)
        {
            if (kingdom == null || kingdom.isRekt()) continue;
            kingdom.SetEmpireID(this.id);
            foreach (City city in kingdom.cities)
            {
                if (city == null || city.isRekt()) continue;
                if (seenCityIds.Add(city.id))
                {
                    rebuiltCities.Add(city);
                }
            }
        }

        if (cities_list.Count != rebuiltCities.Count || cities_list.Except(rebuiltCities).Any())
        {
            cities_list = rebuiltCities;
            repaired = true;
        }

        Kingdom currentCore = CoreKingdom;
        bool coreInvalid = currentCore == null || currentCore.isRekt() || !kingdoms_hashset.Contains(currentCore) || currentCore.cities.Count <= 0;
        if (coreInvalid)
        {
            long preferredCoreId = data.last_core_kingdom_id > 0 ? data.last_core_kingdom_id : data.empire;
            Kingdom candidate = kingdoms_list
                .Where(k => k != null && !k.isRekt() && k.cities.Count > 0)
                .OrderByDescending(k => k.id == preferredCoreId)
                .ThenByDescending(k => k == currentCore)
                .ThenByDescending(k => k.IsEmpire())
                .ThenByDescending(k => k.countTotalWarriors())
                .ThenByDescending(k => k.cities.Count)
                .FirstOrDefault();

            if (candidate != null)
            {
                CoreKingdom = candidate;
                SyncCoreKingdomReference();
                repaired = true;
            }
        }

        if (CoreKingdom == null || CoreKingdom.isRekt() || !kingdoms_hashset.Contains(CoreKingdom))
        {
            if (!string.IsNullOrEmpty(reason))
            {
                LogService.LogInfo($"帝国 {id} 修复失败: {reason}");
            }
            return false;
        }

        if (data.centerOffice == null)
        {
            data.centerOffice = new CenterOffice();
            repaired = true;
        }

        try
        {
            data.centerOffice.Init(CoreKingdom);
        }
        catch (Exception e)
        {
            LogService.LogError($"中央官署初始化失败，正在重置: {e}");
            data.centerOffice = new CenterOffice();
            data.centerOffice.Init(CoreKingdom);
            repaired = true;
        }

        if (OriginalCapital == null || OriginalCapital.isRekt() || OriginalCapital.kingdom != CoreKingdom)
        {
            OriginalCapital = CoreKingdom.capital;
            repaired = true;
        }

        if (OriginalCapital != null && !OriginalCapital.isRekt())
        {
            _capitalCenter = OriginalCapital.city_center;
            data.original_capital = OriginalCapital.id;
        }
        SyncCoreKingdomReference();

        if (EmpireClan == null || EmpireClan.isRekt())
        {
            EmpireClan = CoreKingdom.getKingClan();
            data.empire_clan = EmpireClan?.data?.id ?? -1L;
            repaired = true;
        }

        if (Religion == null || Religion.isRekt())
        {
            Religion = World.world.religions.get(data.Religion);
        }

        given_Kingdoms = given_Kingdoms.Where(k => k != null && !k.isRekt()).Distinct().ToList();
        taken_Kingdoms = taken_Kingdoms.Where(k => k != null && !k.isRekt()).Distinct().ToList();

        if (string.IsNullOrWhiteSpace(data.name))
        {
            string empireName = CoreKingdom.GetKingdomName();
            if (CoreKingdom.king != null && CoreKingdom.king.HasTitle())
            {
                empireName = CoreKingdom.king.GetTitle();
            }
            SetEmpireName(empireName);
            repaired = true;
        }

        data.kingdoms = kingdoms_list.Where(k => k != null && !k.isRekt()).Select(k => k.id).Distinct().ToList();
        data.cities = cities_list.Where(c => c != null && !c.isRekt()).Select(c => c.id).Distinct().ToList();
        SyncCoreKingdomReference();

        if (repaired && !string.IsNullOrEmpty(reason))
        {
            LogService.LogInfo($"帝国 {id} 已重置并修复: {reason}");
        }

        return kingdoms_hashset.Count > 0;
    }

    public bool checkActive()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return true;
        if (IsArchived()) return false;
        if (!TryRepairState("checkActive precheck"))
        {
            return false;
        }
        bool tChanged = false;
        recalculate();
        List<Kingdom> tKingdoms = this.kingdoms_list;
        if (tKingdoms.Count<=0)
        {
            return false;
        }
        if (CoreKingdom != null)
        {
            if (CoreKingdom.cities.Count <= 0)
            {
                var originalEmperor = Emperor;
                var originalHasHeir = CoreKingdom.HasHeir();
                Actor originalHeir = originalHasHeir ? CoreKingdom.GetHeir() : null;
                Kingdom candidate = null;
                foreach (var k in tKingdoms)
                {
                    if (k == CoreKingdom) continue;
                    if (k.isRekt()) continue;
                    if (k.cities.Count <= 0) continue;
                    if (k.GetRegime().GetLeaderSelectMethod() != LeaderSelectMethod.Succession)
                    {
                        candidate = k;
                        break;
                    }
                }
                if (candidate == null)
                {
                    int maxWarriors = -1;
                    foreach (var k in tKingdoms)
                    {
                        if (k == CoreKingdom) continue;
                        if (k.isRekt()) continue;
                        if (k.cities.Count <= 0) continue;
                        int warriors = k.countTotalWarriors();
                        if (warriors >= maxWarriors)
                        {
                            maxWarriors = warriors;
                            candidate = k;
                        }
                    }
                }
                if (candidate != null)
                {
                    CoreKingdom = candidate;
                    SyncCoreKingdomReference();
                    data.centerOffice.Init(CoreKingdom);
                    if (originalEmperor != null && !originalEmperor.isRekt())
                    {
                        if (CoreKingdom.king != originalEmperor)
                        {
                            CoreKingdom.setKing(originalEmperor);
                        }
                        MoveToEmpireCapital(originalEmperor);
                    }
                    else
                    {
                        if (originalHeir != null && !originalHeir.isRekt())
                        {
                            if (CoreKingdom.king != originalHeir)
                            {
                                CoreKingdom.setKing(originalHeir);
                            }
                            MoveToEmpireCapital(originalHeir);
                        }
                    }
                }
                else
                {
                    return false;
                }
            }
        }
        List<Kingdom> remove_tKingdoms = new List<Kingdom>();
        foreach( Kingdom k in tKingdoms )
        {
            if (k.isRekt()||k.GetEmpire()!=this)
            {
                remove_tKingdoms.Add(k);
                tChanged = true;
            }
        }
        foreach (Kingdom k in remove_tKingdoms)
        {
            if (!k.isRekt())
            {
                leave(k);
            }
            kingdoms_hashset.Remove(k);
        }
        if (tChanged)
        {
            recalculate();
            return TryRepairState("checkActive cleanup");
        }
        return kingdoms_hashset.Count >= 1;
    }

    public int GetCapitalLostTime()
    {
        return Date.getYearsSince(data.center_loss_timestamp);
    }

    public bool IsCapitalLost()
    {
        return data.is_center_lost;
    }
    // Token: 0x06001125 RID: 4389 RVA: 0x000C77B4 File Offset: 0x000C59B4
    public void dissolve()
    {
        foreach (Kingdom kingdom in this.kingdoms_hashset)
        {
            kingdom?.EmpireLeave();
        }
        this.kingdoms_hashset.Clear();
        this.kingdoms_list.Clear();
    }

    // Token: 0x06001126 RID: 4390 RVA: 0x000C7810 File Offset: 0x000C5A10
    public void RefreshCompatibilityMembership()
    {
        if (!AncientWarfareCompatibility.Loaded || AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        // Filter simulation lists only. Keep saved membership so disabling AW does not erase it.
        kingdoms_list.RemoveAll(k => AncientWarfareCompatibility.Owns(k));
        cities_list.RemoveAll(c => AncientWarfareCompatibility.OwnsObject(c));
    }

    public void recalculate()
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom)) return;
        this.kingdoms_list.Clear();
        this.kingdoms_list.AddRange(this.kingdoms_hashset.Where(k => !AncientWarfareCompatibility.Owns(k)));
        this.mergeWars();
    }

    // Token: 0x06001127 RID: 4391 RVA: 0x000C7834 File Offset: 0x000C5A34
    public bool canJoin(Kingdom pKingdom)
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom) || AncientWarfareCompatibility.Owns(pKingdom)) return false;
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
        this.data.cities = new List<long>();
        foreach (City tCity in this.cities_list)
        {
            if (!tCity.isRekt())
            {
                this.data.cities.Add(tCity.id);
            }
        }

        this.data.Religion = Religion?.id ?? -1L;
        foreach (var k in this.given_Kingdoms)
        {
            this.data.given_Kingdoms.Add(k.getID());
        }

        foreach (var k in this.taken_Kingdoms)
        {
            this.data.taken_Kingdoms.Add(k.getID());
        }
        SyncCoreKingdomReference();
        this.data.original_capital = !this.OriginalCapital.isRekt() ? this.OriginalCapital.data.id : -1L;
        try
        {
            this.data.empire_clan = this.EmpireClan == null ? -1L : this.EmpireClan.data.id;
        }
        catch (Exception e)
        {
            this.data.empire_clan = -1L;
            LogService.LogError($"存储帝国氏族失败: {e}");
        }

    }

    // Token: 0x0600112B RID: 4395 RVA: 0x000C7CCC File Offset: 0x000C5ECC
    public override void loadData(EmpireData pData)
    {
        base.loadData(pData);
        if (pData.archived)
        {
            this.EmpireClan = World.world.clans.get(pData.empire_clan);
            this.Religion = World.world.religions.get(pData.Religion);
            this.OriginalCapital = World.world.cities.get(pData.original_capital);
            this.CoreKingdom = World.world.kingdoms.get(pData.empire);
            if (this.CoreKingdom == null || this.CoreKingdom.isRekt())
            {
                this.CoreKingdom = World.world.kingdoms.get(pData.last_core_kingdom_id);
            }
            SyncCoreKingdomReference();
            return;
        }
        foreach (long tKingdomID in this.data.kingdoms)
        {
            Kingdom tKingdom = World.world.kingdoms.get(tKingdomID);
            if (tKingdom != null)
            {

                tKingdom.SetEmpireID(this.id);
                kingdoms_hashset.Add(tKingdom);
            }
        }
        if (this.data.cities != null)
        {
            foreach (long tCityID in this.data.cities)
            {
                City tCity = World.world.cities.get(tCityID);
                if (tCity != null)
                {
                    cities_list.Add(tCity);
                }
            }       
        }
        else
        {
            cities_list = new List<City>();
            foreach (var kingdom in kingdoms_hashset)
            {
                cities_list = cities_list.Union(kingdom.cities).ToList();
            }
        }
        
        foreach (var k in pData.given_Kingdoms)
        {
            given_Kingdoms.Add(World.world.kingdoms.get(k));
        }

        foreach (var k in pData.taken_Kingdoms)
        {
            taken_Kingdoms.Add(World.world.kingdoms.get(k));
        }

        this.Religion = World.world.religions.get(pData.Religion);
        this.CoreKingdom = World.world.kingdoms.get(pData.empire);
        if (this.CoreKingdom == null || this.CoreKingdom.isRekt())
        {
            this.CoreKingdom = World.world.kingdoms.get(pData.last_core_kingdom_id);
        }
        if (this.CoreKingdom != null && !this.CoreKingdom.isRekt())
        {
            this.CoreKingdom.SetEmpireID(this.id);
            this.CoreKingdom.GetOrCreate().isEmpire = true;
            SyncCoreKingdomReference();
        }
        this.EmpireClan = World.world.clans.get(pData.empire_clan);
        this.OriginalCapital = World.world.cities.get(pData.original_capital);
        this.recalculate();
    }

    // Token: 0x06001128 RID: 4392 RVA: 0x000C7890 File Offset: 0x000C5A90
    public void join(Kingdom pKingdom, bool pRecalc = true, bool pForce = false)
    {
        if (AncientWarfareCompatibility.Owns(CoreKingdom) || AncientWarfareCompatibility.Owns(pKingdom)) return;
        if (hasKingdom(pKingdom))
        {
            return;
        }
        if (!pForce && !this.canJoin(pKingdom))
        {
            return;
        }

        if (pKingdom.IsInEmpire())
        {
            var originalEmpire = pKingdom.GetEmpire();
            originalEmpire.leave(pKingdom);
        }
        kingdoms_hashset.Add(pKingdom);
        cities_list = cities_list.Union(pKingdom.cities).ToList();
        pKingdom.EmpireJoin(this);
        pKingdom.SetFiedTimestamp(World.world.getCurWorldTime());
        if (pKingdom.HasTakenAlliance())
        {
            pKingdom.RemoveTakenAlliance();
        }
        if (pKingdom.HasGivenAlliance())
        {
            pKingdom.RemoveGivenAlliance();
        }
        pKingdom.RemoveFactionRatio();
        if (pRecalc)
        {
            recalculate();
        }
        data.timestamp_member_joined = World.world.getCurWorldTime();
    }

    public void leave(Kingdom pKingdom, bool pRecalc = true, bool isLeave = false)
    {
        bool isCoreKingdom = pKingdom != null && (pKingdom == CoreKingdom || pKingdom.IsEmpire());
        this.kingdoms_hashset.Remove(pKingdom);
        pKingdom.EmpireLeave(isLeave);
        cities_list = this.cities_list.Except(pKingdom?.cities??new List<City>()).ToList();
        if (isCoreKingdom)
        {
            CheckDissolve(pKingdom);
        } else
        {
            if (ShouldDissolveEmpire())
            {
                if (!TryRepairState("leave after kingdom removed"))
                {
                    ModClass.EMPIRE_MANAGER.dissolveEmpire(this);
                    LogService.LogInfo("帝国内部国家数量为0解散");
                }
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
        return this.CountPopulation();
    }


    public int CountPopulation()
    {
        return data.cached_population;
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
        return data.cached_warriors;
    }

    // Token: 0x06001133 RID: 4403 RVA: 0x000C7E9C File Offset: 0x000C609C
    public int countWarriorsMax()
    {
        return data.cached_warriors_max;
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
        foreach (var tileZone2 in zones)
        {
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
    
    
    public override IEnumerable<Actor> getUnits()
    {
        return EmpirePopulation.Enumerate(kingdoms_list);
    }


    private List<KingdomTitle> GetLegalPeerageTitles()
    {
        EmpireCore core = EmpireCoreManager.Get(this);
        if (core == null) return new List<KingdomTitle>();
        return EmpireCoreManager.GetTitles(core)
            .Where(title => title != null && !title.isRekt() &&
                !string.Equals(title.data.name, GetEmpireName(), StringComparison.Ordinal))
            .OrderBy(title => title.data.name)
            .ToList();
    }

    private Actor GetLivingLegalPeerageHolder(long titleId)
    {
        Actor holder = getUnits().FirstOrDefault(actor => actor != null && !actor.isRekt() &&
            actor.HasVirtualEnfeoff(this) && actor.GetOrCreate().virtual_enfeoff_title_id == titleId);
        if (holder != null) return holder;
        if (data.legal_peerage_holders?.TryGetValue(titleId, out long holderId) != true) return null;
        holder = World.world.units.get(holderId);
        return holder != null && !holder.isRekt() && holder.HasVirtualEnfeoff(this) &&
            holder.GetOrCreate().virtual_enfeoff_title_id == titleId ? holder : null;
    }

    public Actor GetPowerfulMinister()
    {
        if (data?.powerful_minister_id <= 0 || World.world == null) return null;
        Actor actor = World.world.units.get(data.powerful_minister_id);
        return actor != null && !actor.isRekt() && actor.kingdom?.GetEmpire() == this ? actor : null;
    }

    public bool IsCurrentPowerfulMinister(Actor actor)
    {
        return actor != null && GetPowerfulMinister()?.id == actor.id;
    }

    public string GetPowerfulMinisterStatusText()
    {
        Actor minister = GetPowerfulMinister();
        if (minister == null) return "";
        string progress = string.Format(LM.Get(data.powerful_minister_is_regent
            ? "powerful_minister_status_regent" : "powerful_minister_status_progress"),
            data.powerful_minister_progress);
        return data.powerful_minister_stage switch
        {
            PowerfulMinisterStageKing or PowerfulMinisterStageDuke => minister.GetPeerageDisplayName() + "\n" + progress,
            PowerfulMinisterStageDominant when data.is_been_controlled => LM.Get("powerful_minister_status_dominant") + "\n" + progress,
            _ => progress
        };
    }

    private bool IsCentralMinister(Actor actor)
    {
        if (actor == null || actor.isRekt() || actor.isKing() || !actor.isAdult() ||
            actor.kingdom != CoreKingdom || actor.GetSpecificClan() == EmpireSpecificClan) return false;
        OfficeObject office = actor.GetOffice();
        return office != null && !office.is_local && office.actor_id == actor.id && office.meta_object == CoreKingdom;
    }

    private bool HasCentralMinisterSupport(Actor actor, Regime currentRegime)
    {
        FixedFaction dominant = currentRegime?.GetDominateFaction();
        return dominant != null && dominant.TotalPower > 0 && actor?.GetFaction() == dominant;
    }

    private bool HasStrongEmperor()
    {
        Actor emperor = Emperor;
        return emperor != null && !emperor.isRekt() && emperor.isAdult() && Mandate >= 80 &&
            emperor.stewardship >= 10 && !HasVulnerableNewEmperor();
    }

    private bool HasVulnerableNewEmperor()
    {
        Actor emperor = Emperor;
        return emperor != null && !emperor.isRekt() && data.newEmperor_timestamp >= 0 &&
            PowerfulMinisterRules.IsVulnerableNewEmperor(emperor.renown,
                Date.getMonthsSince(data.newEmperor_timestamp));
    }

    private bool IsRegencyEnding()
    {
        return data.powerful_minister_regency_end_timestamp >= 0 &&
            Date.getMonthsSince(data.powerful_minister_regency_end_timestamp) < PowerfulMinisterRules.RegencyRecoveryMonths;
    }

    private bool IsEligiblePowerBase(Actor actor, Regime currentRegime, bool requireInitialInfluence,
        out bool isRegent)
    {
        isRegent = false;
        if (!IsCentralMinister(actor) || currentRegime?.type != RegimeType.LvLing) return false;
        Actor cabinetLeader = GetCabinetLeader();
        isRegent = Emperor != null && !Emperor.isRekt() && !Emperor.isAdult() &&
            cabinetLeader?.id == actor.id;
        if (isRegent) return true;
        bool isFactionLeader = actor.GetFaction()?.GetLeader()?.id == actor.id;
        bool isShangshu = actor.GetOffice()?.officeType == 4;
        if (cabinetLeader?.id != actor.id && !isFactionLeader && !isShangshu) return false;
        return !requireInitialInfluence || actor.renown >= PowerfulMinisterRules.EntryInfluence;
    }

    private Actor FindPowerfulMinisterCandidate(Regime currentRegime, out bool isRegent)
    {
        isRegent = false;
        Actor current = GetPowerfulMinister();
        if (IsCentralMinister(current)) return current;
        Actor cabinetLeader = GetCabinetLeader();
        if (IsEligiblePowerBase(cabinetLeader, currentRegime, false, out isRegent) && isRegent) return cabinetLeader;
        if (data.centerOffice == null) return null;
        // Only examine central office holders, not every resident of the empire.
        return data.centerOffice.CoreOffices.Concat(data.centerOffice.Divisions).Concat(data.centerOffice.Harems)
            .Distinct()
            .Select(id => OfficeManager.Offices.TryGetValue(id, out var office) ? office.GetActor() : null)
            .Where(actor => IsEligiblePowerBase(actor, currentRegime, true, out _))
            .OrderByDescending(actor => actor.id == cabinetLeader?.id)
            .ThenByDescending(actor => HasCentralMinisterSupport(actor, currentRegime))
            .ThenByDescending(actor => actor.renown)
            .ThenByDescending(actor => actor.GetOrCreate().officeIdentity?.TotalPerformance ?? 0)
            .FirstOrDefault();
    }

    private void UpdatePowerfulMinister(Regime currentRegime)
    {
        Actor emperor = Emperor;
        if (currentRegime?.type != RegimeType.LvLing) { ClearPowerfulMinister(); return; }
        if (emperor == null || emperor.isRekt())
        {
            data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
            return;
        }
        Actor candidate = FindPowerfulMinisterCandidate(currentRegime, out bool isRegent);
        if (candidate?.id != data.powerful_minister_id && data.is_been_controlled)
        {
            Actor previous = World.world.units.get(data.powerful_minister_id);
            if (previous != null && !previous.isRekt()) RecordMinisterTransition(previous, "history_minister_lost_control");
        }
        if (candidate == null)
        {
            ClearPowerfulMinister();
            return;
        }
        if (data.powerful_minister_id != candidate.id)
        {
            ClearPowerfulMinister();
            data.powerful_minister_id = candidate.id;
            data.powerful_minister_progress = 0;
            data.powerful_minister_is_regent = isRegent;
            data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
            data.powerful_minister_emperor_id = emperor.id;
            if (candidate.HasVirtualEnfeoff(this))
            {
                var titleData = candidate.GetOrCreate();
                data.powerful_minister_title_id = titleData.virtual_enfeoff_title_id;
                data.powerful_minister_stage = titleData.virtual_enfeoff_peerage_key == "default_peerages_2"
                    ? PowerfulMinisterStageKing : PowerfulMinisterStageDuke;
                data.powerful_minister_stage_timestamp = World.world.getCurWorldTime();
            }
            if (isRegent) RecordMinisterTransition(candidate, "history_minister_regent");
            return;
        }

        isRegent = !emperor.isAdult() && GetCabinetLeader()?.id == candidate.id;
        if (data.powerful_minister_is_regent && !isRegent)
        {
            data.powerful_minister_regency_end_timestamp = World.world.getCurWorldTime();
            data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
        }
        if (isRegent)
        {
            data.powerful_minister_regency_end_timestamp = -1L;
            if (!data.powerful_minister_is_regent) RecordMinisterTransition(candidate, "history_minister_regent");
        }
        data.powerful_minister_is_regent = isRegent;
        if (data.powerful_minister_emperor_id != emperor.id)
        {
            bool isNewEmperor = data.powerful_minister_emperor_id > 0;
            data.powerful_minister_emperor_id = emperor.id;
            if (isNewEmperor)
            {
                data.is_been_controlled = false;
                data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
            }
        }
        // Older saves have no stage date: begin a full cooldown rather than allowing a stage skip.
        if (data.powerful_minister_stage >= PowerfulMinisterStageDominant && data.powerful_minister_stage_timestamp < 0)
            data.powerful_minister_stage_timestamp = World.world.getCurWorldTime();
        if (data.last_powerful_minister_timestamp < 0)
        {
            data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
            return;
        }
        int elapsedMonths = Date.getMonthsSince(data.last_powerful_minister_timestamp);
        if (elapsedMonths < 1) return;
        data.last_powerful_minister_timestamp = World.world.getCurWorldTime();
        bool hasSupport = HasCentralMinisterSupport(candidate, currentRegime);
        bool chiefAndLeader = GetCabinetLeader()?.id == candidate.id &&
            hasSupport && candidate.GetFaction()?.GetLeader()?.id == candidate.id;
        int monthlyChange = PowerfulMinisterRules.MonthlyChange(isRegent, chiefAndLeader, hasSupport,
            HasStrongEmperor(), IsRegencyEnding(), HasVulnerableNewEmperor());
        monthlyChange = PowerfulMinisterRules.ApplyMandate(monthlyChange, Mandate);
        data.powerful_minister_progress = PowerfulMinisterRules.Advance(data.powerful_minister_progress,
            candidate.renown, elapsedMonths, monthlyChange, out int cost);
        candidate.data.renown -= cost;
        if (data.is_been_controlled && data.powerful_minister_progress < PowerfulMinisterRules.ReleaseControlBelow)
        {
            data.is_been_controlled = false;
            RecordMinisterTransition(candidate, "history_minister_lost_control");
        }
        if (data.powerful_minister_progress < 100) return;
        if (data.is_been_controlled) return;
        if (data.powerful_minister_stage < PowerfulMinisterStageDominant)
            data.powerful_minister_stage = PowerfulMinisterStageDominant;
        data.powerful_minister_stage_timestamp = World.world.getCurWorldTime();
        data.is_been_controlled = true;
        TranslateHelper.LogPowerfulMinisterControlsCourt(candidate, this);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_controls_court"),
            candidate.getName(), emperor?.getName() ?? ""), actorId: candidate.id);
        candidate.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_controls_court"),
            GetEmpireName()), relatedActorId: emperor?.id ?? -1L);
        emperor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_became_puppet"),
            candidate.getName()), relatedActorId: candidate.id);
    }

    private void RecordMinisterTransition(Actor minister, string key)
    {
        string content = string.Format(LM.Get(key), minister.getName(), Emperor?.getName() ?? "", GetEmpireFullName());
        this.RecordHistory(directContent: content, actorId: minister.id);
        minister.RecordPersonalHistory(content, relatedActorId: Emperor?.id ?? -1L);
        Emperor?.RecordPersonalHistory(content, relatedActorId: minister.id);
    }

    private void ClearPowerfulMinister()
    {
        data.powerful_minister_id = -1L;
        data.powerful_minister_progress = 0;
        data.powerful_minister_stage = PowerfulMinisterStageNone;
        data.powerful_minister_is_regent = false;
        data.powerful_minister_title_id = -1L;
        data.last_powerful_minister_timestamp = -1L;
        data.powerful_minister_stage_timestamp = -1L;
        data.powerful_minister_regency_end_timestamp = -1L;
        data.powerful_minister_emperor_id = -1L;
        data.is_been_controlled = false;
    }

    private bool CanAdvanceMinisterPlot(Actor actor)
    {
        Regime currentRegime = CoreKingdom?.GetRegime();
        if (!IsCurrentPowerfulMinister(actor) || !IsCentralMinister(actor) ||
            currentRegime?.type != RegimeType.LvLing || Emperor == null || Emperor.isRekt() ||
            data.powerful_minister_emperor_id != Emperor.id ||
            data.powerful_minister_stage_timestamp < 0) return false;
        bool regencyEnding = IsRegencyEnding() || (data.powerful_minister_is_regent && Emperor.isAdult());
        return PowerfulMinisterRules.CanAdvance(data.is_been_controlled, data.powerful_minister_progress,
            HasCentralMinisterSupport(actor, currentRegime), HasStrongEmperor(), regencyEnding,
            HasUsurpingDisposition(actor), Date.getMonthsSince(data.powerful_minister_stage_timestamp),
            data.powerful_minister_stage == PowerfulMinisterStageKing);
    }

    public bool CanPowerfulMinisterSeekDukedom(Actor actor)
    {
        return CanAdvanceMinisterPlot(actor) &&
            data.powerful_minister_stage == PowerfulMinisterStageDominant && !actor.HasVirtualEnfeoff(this) &&
            FindMinisterDukedomTarget() != null;
    }

    private KingdomTitle FindMinisterDukedomTarget()
    {
        // Occupied royal titles are taken before vacant titles; never dispossess another non-royal duke.
        // Scan residents once, not once per legal title during plot eligibility checks.
        var livingHolders = new Dictionary<long, Actor>();
        foreach (Actor holder in getUnits())
        {
            if (holder == null || holder.isRekt() || !holder.HasVirtualEnfeoff(this)) continue;
            long titleId = holder.GetOrCreate().virtual_enfeoff_title_id;
            if (!livingHolders.ContainsKey(titleId)) livingHolders[titleId] = holder;
        }
        Actor FindHolder(long titleId)
        {
            if (livingHolders.TryGetValue(titleId, out Actor holder)) return holder;
            if (data.legal_peerage_holders?.TryGetValue(titleId, out long actorId) != true) return null;
            holder = World.world.units.get(actorId);
            return holder != null && !holder.isRekt() && holder.HasVirtualEnfeoff(this) &&
                holder.GetOrCreate().virtual_enfeoff_title_id == titleId ? holder : null;
        }
        var titles = GetLegalPeerageTitles().Select(title => new
        {
            Title = title,
            Holder = FindHolder(title.id)
        }).ToList();
        return titles.Where(item => item.Holder != null && item.Holder != Emperor &&
                EmpireSpecificClan != null && item.Holder.GetSpecificClan() == EmpireSpecificClan)
            .OrderBy(item => item.Holder.renown).ThenBy(item => item.Title.id)
            .Select(item => item.Title).FirstOrDefault() ??
            titles.Where(item => item.Holder == null).OrderBy(item => item.Title.id)
                .Select(item => item.Title).FirstOrDefault();
    }

    public bool TryGrantPowerfulMinisterDukedom(Actor actor)
    {
        if (!CanPowerfulMinisterSeekDukedom(actor)) return false;
        KingdomTitle title = FindMinisterDukedomTarget();
        if (title == null) return false;
        Actor displaced = GetLivingLegalPeerageHolder(title.id);
        string displacedTitle = displaced?.GetPeerageDisplayName();
        var displacedData = displaced?.GetOrCreate();
        PeeragesLevel previousLevel = displaced?.GetPeeragesLevel() ?? PeeragesLevel.peerages_6;
        if (displacedData != null) displacedData.virtual_enfeoff = false;
        bool granted = false;
        try
        {
            granted = AssignLegalPeerage(actor, title, "tang_peerage_guogong",
                PeeragesLevel.peerages_3, -10, relocate: false);
        }
        finally
        {
            if (!granted && displacedData != null) displacedData.virtual_enfeoff = true;
        }
        if (!granted) return false;
        if (displacedData != null)
        {
            displacedData.virtual_enfeoff_empire_id = -1L;
            displacedData.virtual_enfeoff_title_id = -1L;
            displacedData.virtual_enfeoff_peerage_key = "";
            if (previousLevel == PeeragesLevel.peerages_2 && !displaced.isKing() &&
                displaced.GetOwnedTitle().Count == 0) displaced.SetPeeragesLevel(PeeragesLevel.peerages_6);
            string transfer = string.Format(LM.Get("history_minister_dispossessed_royal"),
                displaced.getName(), displacedTitle, actor.getName());
            this.RecordHistory(directContent: transfer, actorId: displaced.id);
            displaced.RecordPersonalHistory(transfer, relatedActorId: actor.id);
            actor.RecordPersonalHistory(transfer, relatedActorId: displaced.id);
        }
        data.powerful_minister_title_id = title.id;
        data.powerful_minister_stage = PowerfulMinisterStageDuke;
        data.powerful_minister_stage_timestamp = World.world.getCurWorldTime();
        string peerageName = actor.GetPeerageDisplayName();
        TranslateHelper.LogPowerfulMinisterAcquireTitle(actor, this, peerageName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_duke"),
            actor.getName(), peerageName), actorId: actor.id);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_duke"), peerageName));
        return true;
    }

    public bool CanPowerfulMinisterReceiveNineBestowments(Actor actor)
    {
        return CanAdvanceMinisterPlot(actor) && data.powerful_minister_stage == PowerfulMinisterStageDuke &&
            actor.HasVirtualEnfeoff(this) && actor.GetOrCreate().virtual_enfeoff_title_id == data.powerful_minister_title_id;
    }

    public bool GrantPowerfulMinisterNineBestowments(Actor actor)
    {
        if (!CanPowerfulMinisterReceiveNineBestowments(actor)) return false;
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff_peerage_key = "default_peerages_2";
        actor.SetPeeragesLevel(PeeragesLevel.peerages_2);
        data.legal_peerage_types[data.powerful_minister_title_id] = "default_peerages_2";
        data.powerful_minister_stage = PowerfulMinisterStageKing;
        data.powerful_minister_stage_timestamp = World.world.getCurWorldTime();
        AddMandate(-20);
        string peerageName = actor.GetPeerageDisplayName();
        TranslateHelper.LogPowerfulMinisterNineBestowments(actor, this, peerageName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_nine_bestowments"),
            actor.getName(), peerageName), actorId: actor.id);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_nine_bestowments"),
            peerageName));
        ResolveMinisterOpposition(actor);
        return true;
    }

    private static bool HasUsurpingDisposition(Actor actor)
    {
        return actor != null && (actor.hasTrait("evil") || actor.hasTrait("ambitious"));
    }

    public bool CanPowerfulMinisterUsurp(Actor actor)
    {
        if (!CanAdvanceMinisterPlot(actor) || data.powerful_minister_stage != PowerfulMinisterStageKing ||
            data.powerful_minister_title_id <= 0) return false;
        return actor.HasVirtualEnfeoff(this) &&
            actor.GetOrCreate().virtual_enfeoff_title_id == data.powerful_minister_title_id;
    }

    public int GetMinisterOppositionPenalty()
    {
        Actor minister = data.minister_usurper_id == Emperor?.id ? Emperor :
            data.is_been_controlled && data.powerful_minister_stage == PowerfulMinisterStageKing
                ? GetPowerfulMinister() : null;
        return minister == null || minister.isRekt() ? 0 : PowerfulMinisterRules.OppositionPenalty(minister.renown);
    }

    private City GetMinisterOpponentPowerBase(Actor actor)
    {
        if (actor == null || actor.isRekt() || !actor.isAdult() || actor == Emperor ||
            actor.kingdom?.GetEmpire() != this) return null;
        bool IsLocal(City city) => city != null && !city.isRekt() && city != CoreKingdom?.capital &&
            city.kingdom?.GetEmpire() == this;
        if (actor.isKing() && actor.kingdom != CoreKingdom && IsLocal(actor.kingdom.capital))
            return actor.kingdom.capital;
        OfficeObject office = actor.GetOffice();
        if (office != null && office.is_local && office.actor_id == actor.id)
        {
            City seat = office.meta_object is City city ? city : (office.meta_object as Kingdom)?.capital;
            if (IsLocal(seat)) return seat;
        }
        // An honorary/virtual title alone does not command any land or army.
        return actor.GetOwnedTitle().Select(id => ModClass.KINGDOM_TITLE_MANAGER.get(id))
            .Where(title => title != null && !title.isRekt() && title.owner == actor)
            .SelectMany(title => title.getCities()).Where(IsLocal).OrderBy(city => city.id).FirstOrDefault();
    }

    private void ResolveMinisterOpposition(Actor minister, int? influence = null)
    {
        int ministerInfluence = influence ?? minister.renown;
        if (!PowerfulMinisterRules.ShouldRiseAgainstMinister(ministerInfluence, 501, true)) return;
        // Snapshot before creating kingdoms: each successful revolt changes empire membership.
        var opponents = getUnits().Where(actor => actor != null && actor != minister &&
                !actor.isRekt() && PowerfulMinisterRules.ShouldRiseAgainstMinister(ministerInfluence, actor.renown, true))
            .Distinct().Select(actor => new { Actor = actor, City = GetMinisterOpponentPowerBase(actor) })
            .Where(item => item.City != null).OrderByDescending(item => item.Actor.renown)
            .ThenBy(item => item.Actor.id).ToList();
        foreach (var opponent in opponents)
        {
            Actor leader = opponent.Actor;
            City seat = opponent.City;
            if (leader.isRekt() || seat.isRekt() || seat.kingdom?.GetEmpire() != this ||
                leader.kingdom?.GetEmpire() != this || seat == CoreKingdom?.capital) continue;
            Kingdom rebels = leader.isKing() && leader.kingdom != CoreKingdom ? leader.kingdom : null;
            if (rebels != null && (rebels.IsLocalRebelling() || rebels.IsFactionRebelling() ||
                rebels.isInWarWith(CoreKingdom))) continue;
            if (rebels == null)
            {
                leader.joinCity(seat);
                rebels = seat.makeOwnKingdom(leader, pRebellion: true);
            }
            if (rebels == null || rebels == CoreKingdom) continue;
            rebels.GetRegime()?.SetAllowArmy(true);
            rebels.GetRegime()?.SetAllowSupportCenterArmy(false);
            War war = World.world.diplomacy.startWar(rebels, CoreKingdom, WarTypeLibrary.rebellion);
            if (war == null) continue;
            war.SetEmpireWarType(EmpireWarType.地方叛乱, pre: CoreKingdom.name,
                nanoObject: this, belongingFaction: leader.GetFaction());
            if (rebels.GetEmpire() == this) leave(rebels, true, true);
            rebels.StartLocalRebelling(EmpireWarType.地方叛乱);
            string content = string.Format(LM.Get("history_minister_opposition_rebellion"),
                leader.getName(), minister.getName());
            this.RecordHistory(directContent: content, actorId: leader.id);
            leader.RecordPersonalHistory(content, relatedActorId: minister.id);
            minister.RecordPersonalHistory(content, relatedActorId: leader.id);
        }
    }

    public bool CompletePowerfulMinisterUsurpation(Actor actor)
    {
        if (!CanPowerfulMinisterUsurp(actor)) return false;
        KingdomTitle title = ModClass.KINGDOM_TITLE_MANAGER.get(data.powerful_minister_title_id);
        Kingdom coreKingdom = CoreKingdom;
        City capital = coreKingdom?.capital;
        if (title == null || coreKingdom == null || capital == null) return false;

        string newEmpireName = title.data.name;
        string oldEmpireName = GetEmpireName();
        Actor previousEmperor = Emperor;
        int ministerInfluence = actor.renown;
        TranslateHelper.LogPowerfulMinisterUsurpation(actor, this, newEmpireName);
        this.RecordHistory(directContent: string.Format(LM.Get("history_powerful_minister_usurpation"),
            actor.getName(), newEmpireName), actorId: actor.id);
        previousEmperor?.RecordPersonalHistory(string.Format(LM.Get("personal_history_deposed_by_powerful_minister"),
            actor.getName()), relatedActorId: actor.id);
        actor.RecordPersonalHistory(string.Format(LM.Get("personal_history_powerful_minister_usurpation"),
            oldEmpireName, newEmpireName), relatedActorId: previousEmperor?.id ?? -1L);

        long titleId = data.powerful_minister_title_id;
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff = false;
        actorData.virtual_enfeoff_empire_id = -1L;
        actorData.virtual_enfeoff_title_id = -1L;
        actorData.virtual_enfeoff_peerage_key = "";
        data.legal_peerage_holders?.Remove(titleId);
        data.legal_peerage_holder_identities?.Remove(titleId);
        data.legal_peerage_types?.Remove(titleId);

        if (previousEmperor != null && previousEmperor.id != actor.id)
        {
            coreKingdom.removeKing();
        }
        actor.joinCity(capital);
        actor.setKingdom(coreKingdom);
        // This path has its own influence-based opposition, not the generic dynasty-change rebellion.
        _completingMinisterUsurpation = true;
        try { coreKingdom.setKing(actor); }
        finally { _completingMinisterUsurpation = false; }
        data.directPre = "";
        SetEmpireName(newEmpireName);
        if (data.currentHistory != null)
        {
            data.currentHistory.empire_name = newEmpireName;
            data.currentHistory.dynasty_name = newEmpireName;
        }
        if (Emperor?.id != actor.id) return false;
        data.minister_usurper_id = actor.id;
        ResolveMinisterOpposition(actor, ministerInfluence);
        ClearPowerfulMinister();
        return Emperor?.id == actor.id;
    }

    private Actor GetLegalPeerageCandidate(KingdomTitle title, out PersonalClanIdentity predecessor)
    {
        predecessor = null;
        bool IsAvailable(Actor actor) => actor != null && !actor.isRekt() && !actor.isKing() &&
            !actor.HasVirtualEnfeoff(this) && actor.kingdom?.GetEmpire() == this &&
            actor.id != (CoreKingdom?.GetHeir()?.id ?? -1L);
        bool IsDynasticHeir(PersonalClanIdentity identity, PersonalClanIdentity source) =>
            identity?._specificClan != null && identity._specificClan == EmpireSpecificClan &&
            identity.CanHeir(source) && IsAvailable(identity._actor);

        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        if (data.legal_peerage_types.TryGetValue(title.id, out string previousType) &&
            previousType == "default_peerages_2")
        {
            data.legal_peerage_holder_identities.TryGetValue(title.id, out long previousIdentityId);
            data.legal_peerage_holders.TryGetValue(title.id, out long previousActorId);
            PersonalClanIdentity previousHolder = ResolvePeerageIdentity(previousActorId, previousIdentityId);
            Actor branchHeir = FindPeerageDescendant(previousHolder,
                actor => IsAvailable(actor) && actor.GetSpecificClan() == EmpireSpecificClan);
            if (branchHeir != null)
            {
                predecessor = previousHolder;
                return branchHeir;
            }
        }

        PersonalClanIdentity emperorIdentity = Emperor?.GetPersonalIdentity();
        Actor sibling = SpecificClanManager.GetSiblingsWithRelation(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => IsDynasticHeir(identity, emperorIdentity))
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
        if (sibling != null) return sibling;

        long crownPrinceId = CoreKingdom?.GetHeir()?.id ?? -1L;
        Actor son = SpecificClanManager.getChildren(emperorIdentity)
            .Select(item => item.Item2)
            .Where(identity => IsDynasticHeir(identity, emperorIdentity) && identity.actor_id != crownPrinceId)
            .OrderBy(identity => identity.rank)
            .Select(identity => identity._actor)
            .FirstOrDefault();
        return son;
    }

    private bool GrantLegalPeerage(Actor actor, KingdomTitle title, PersonalClanIdentity predecessor = null)
    {
        if (actor == null || title == null || GetLivingLegalPeerageHolder(title.id) != null) return false;
        bool isRoyal = actor.GetSpecificClan() != null && actor.GetSpecificClan() == EmpireSpecificClan;
        if (!isRoyal) return false;
        if (!AssignLegalPeerage(actor, title, "default_peerages_2", PeeragesLevel.peerages_2,
                predecessor == null ? 10 : 0)) return false;
        if (predecessor == null)
            TranslateHelper.LogPeerageGranted(actor, this, actor.GetPeerageDisplayName());
        else
            TranslateHelper.LogPeerageInherited(actor, this, actor.GetPeerageDisplayName(), predecessor,
                predecessor.actor_id, predecessor.name);
        return true;
    }

    private bool AssignLegalPeerage(Actor actor, KingdomTitle title, string peerageKey,
        PeeragesLevel peeragesLevel, int mandateChange, bool relocate = true)
    {
        if (actor == null || title == null || GetLivingLegalPeerageHolder(title.id) != null) return false;
        actor.CheckSpecificClan(false);
        var actorData = actor.GetOrCreate();
        actorData.virtual_enfeoff = true;
        actorData.virtual_enfeoff_empire_id = data.id;
        actorData.virtual_enfeoff_title_id = title.id;
        actorData.virtual_enfeoff_peerage_key = peerageKey;
        actor.SetPeeragesLevel(peeragesLevel);

        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_holders[title.id] = actor.id;
        data.legal_peerage_holder_identities[title.id] = actor.GetPersonalIdentity()?.id ?? -1L;
        data.legal_peerage_types[title.id] = peerageKey;

        City destination = title.title_capital;
        if (relocate && destination != null && !destination.isRekt() && destination.kingdom?.GetEmpire() == this)
        {
            actor.joinCity(destination);
            actor.goTo(destination._city_tile);
        }
        AddMandate(mandateChange);
        return true;
    }

    private void ProcessLegalPeerageSuccession()
    {
        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        List<KingdomTitle> legalTitles = GetLegalPeerageTitles();
        HashSet<long> validTitleIds = legalTitles.Select(title => title.id).ToHashSet();
        foreach (long obsoleteTitleId in data.legal_peerage_holders.Keys.Where(id => !validTitleIds.Contains(id)).ToList())
        {
            data.legal_peerage_holders.Remove(obsoleteTitleId);
            data.legal_peerage_holder_identities.Remove(obsoleteTitleId);
            data.legal_peerage_types.Remove(obsoleteTitleId);
        }

        foreach (Actor holder in getUnits().Where(actor => actor != null && !actor.isRekt() && actor.HasVirtualEnfeoff(this)))
        {
            var holderData = holder.GetOrCreate();
            if (!validTitleIds.Contains(holderData.virtual_enfeoff_title_id)) continue;
            if (string.IsNullOrWhiteSpace(holderData.virtual_enfeoff_peerage_key)) holder.GetPeerageDisplayName();
            data.legal_peerage_holders[holderData.virtual_enfeoff_title_id] = holder.id;
            data.legal_peerage_holder_identities[holderData.virtual_enfeoff_title_id] = holder.GetPersonalIdentity()?.id ?? -1L;
            data.legal_peerage_types[holderData.virtual_enfeoff_title_id] = holderData.virtual_enfeoff_peerage_key;
        }

        foreach (KingdomTitle title in legalTitles)
        {
            if (GetLivingLegalPeerageHolder(title.id) != null) continue;
            // New legal titles have no saved holder/type yet. They are royal peerages by default;
            // non-royal dukedoms keep their separate powerful-minister succession rules.
            if (data.legal_peerage_types.TryGetValue(title.id, out string peerageType) &&
                peerageType != "default_peerages_2") continue;
            Actor successor = GetLegalPeerageCandidate(title, out PersonalClanIdentity predecessor);
            if (successor != null) GrantLegalPeerage(successor, title, predecessor);
        }
    }

    public void AutoEnfeoff()
    {
        Kingdom coreKingdom = CoreKingdom;
        if (data == null || coreKingdom == null || coreKingdom.isRekt()) return;
        Regime regime = coreKingdom.GetRegime();
        if (regime != null && regime.enfeoff_virtual_only)
        {
            data.last_legal_peerage_timestamp = -1L;
            return;
        }
        var allCities = coreKingdom.cities;
        EmpireCore empireCore = EmpireCoreManager.Get(this);
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
            region = region.FindAll(c =>
            {
                if (c == null || c.isRekt() || c.getID() == CoreKingdom.capital.getID()) return false;
                if (empireCore != null && c.GetEmpireCore() == empireCore) return false;
                KingdomTitle title = c.GetTitle();
                return title == null || !string.Equals(title.data.name, GetEmpireName(), StringComparison.Ordinal);
            });
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
                Actor king;
                if (SatisfiedCandidates.Count() > 0)
                {
                    king = SatisfiedCandidates.First();
                }
                else
                {
                    king = capital.hasLeader()?capital.leader:capital.getUnits().FirstOrDefault();
                }
                
                newKingdom = SetEnfeoff(capital, king);
                foreach (var city in region)
                {
                    city.joinAnotherKingdom(newKingdom);
                }
                newKingdom.setCapital(capital);
                newKingdom.data.name = capital.data.name;
                newKingdom.SetFiedTimestamp(World.world.getCurWorldTime());
                join(newKingdom, true, true);
                WorldLog.logNewKingdom(newKingdom);
                newKingdom.SetRegimeType(CoreKingdom.GetRegime().type);
                newKingdom.LoadRegime();
                if (newKingdom.GetRegime().type == RegimeType.LvLing)
                {
                    newKingdom.GetRegime().SetAllowDiplomacy(false);
                    newKingdom.GetRegime().SetLeaderSelectMethod(LeaderSelectMethod.Exam);
                }
                if (king?.GetSpecificClan() != null && king.GetSpecificClan() == EmpireSpecificClan)
                {
                    TranslateHelper.LogPeerageGranted(king, this,
                        newKingdom.data.name + LM.Get("default_peerages_2"));
                }
                else
                {
                    new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_enfeoff_log, this.name)
                    {
                        location = this.CoreKingdom.location,
                        color_special1 = this.CoreKingdom.getColor().getColorText()
                    }.RecordNationalHistoryIntoEmpire(this, king, newKingdom);
                }
            }
        }
        World.world.zone_calculator.dirtyAndClear();
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
        World.world.zone_calculator.dirtyAndClear();
        return kingdom;
    }

    public void SelectAndInspect()
    {
        EmpireCraftMetaTypeLibrary.selected_empire = this;
        SelectedMetas.selected_kingdom = CoreKingdom;
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
    
    public bool isRekt()
    {
        return IsArchived()||!isAlive();
    }

    public override void Dispose()
    {
        this.kingdoms_list.Clear();
        this.kingdoms_hashset.Clear();
        this.given_Kingdoms.Clear();
        this.taken_Kingdoms.Clear();
        this.cities_list.Clear();
        this.CoreKingdom = null;
        if (!ModClass.ALL_HISTORY_DATA.ContainsKey(this.data.id))
        {
            ModClass.ALL_HISTORY_DATA.Add(this.data.id, this.data.history);
        }
        LogService.LogInfo("清空");
    }
}
