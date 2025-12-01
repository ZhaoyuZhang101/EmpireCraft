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
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using NCMS;
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
    
    public List<Kingdom> kingdoms_list = new List<Kingdom>();
    public HashSet<Kingdom> kingdoms_hashset = new HashSet<Kingdom>();
    
    //岁币国
    public List<Kingdom> given_Kingdoms = new List<Kingdom>();
    //朝贡国
    public List<Kingdom> taken_Kingdoms = new List<Kingdom>();

    public Kingdom CoreKingdom;
    public Actor Emperor;
    public int CurrentMoney => CoreKingdom.GetMoney();
    private Vector3 _capitalCenter;
    public City OriginalCapital;
    public　SpecificClan EmpireSpecificClan => SpecificClanManager.Get(data.empire_specific_clan);
    
    public override MetaType meta_type => MetaTypeExtension.Empire;

    public bool HasEmperor()
    {
        return !Emperor.isRekt();
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
        //增加税收减少威望
        data.Prestige -= (int)(addition * 100);
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
        //减少税收增加威望
        data.Prestige += (int)(substraction * 100);
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

    public override IEnumerable<City> getCities()
    {
        var cities = new List<City>();
        foreach (var kingdom in kingdoms_list)
        {
            cities.AddRange(kingdom.cities);
        }

        return cities;
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

    private void MoveToEmpireCapital(Actor actor)
    {
        actor.joinCity(this.CoreKingdom.capital);
        actor.goTo(this.CoreKingdom.capital._city_tile);
        actor.joinKingdom(this.CoreKingdom);
    }
    
    //新皇登基
    public void NewEmperor(Actor actor, bool isNew = false)
    {
        this.Emperor = actor;
        actor.SetEmpire(this);
        string nameEmpire = "";
        actor.CheckSpecificClan();
        //检查帝国分裂
        var currentSpecificClan = actor.GetSpecificClan();
        
        if (currentSpecificClan.id != data.empire_specific_clan && data.empire_specific_clan != -1L) 
        {
            if (currentSpecificClan.all_valid_members.Any())
            {
                var validEmperor = currentSpecificClan.all_valid_members?.First()._actor;
            }
            nameEmpire = actor.culture.getOnomasticData(MetaType.Kingdom).generateName();
            this.data.directPre = "";
            if (actor.hasClan())
            {
                if (actor.clan.HasHistoryEmpire())
                {
                    this.data.directPre = GetDir(actor.clan.GetHistoryEmpirePos());
                    nameEmpire = actor.clan.GetHistoryEmpireName();
                }
            }
            SetEmpireName(nameEmpire);
            isNew = true;
            data.history_emperrors.Clear();
        } 
        
        data.empire_specific_clan = currentSpecificClan.id;
        EmpireClan = actor.clan;
        //设定天子身份并移居首都
        if (actor.isOfficer())
        {
            actor.RemoveIdentity();
            actor.SetPeeragesLevel(PeeragesLevel.peerages_0);
        }
        actor.data.renown += 20;
        MoveToEmpireCapital(actor);
        create_year_name();
        //公屏提示
        TranslateHelper.LogNewEmperor(actor, CoreKingdom.capital, data.year_name);
        
        //记录历史
        this.RecordNewEmperorHistory(isNew);
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

    public void EmperorLeft(Kingdom kingdom)
    {
        if (this.Emperor == null) return;
        if (this.Emperor.data == null) return;
        data.currentHistory ??= new EmpireCraftHistory
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
        this.RecordHistory(
            Emperor.isAlive() ? EmpireHistoryType.emperor_left_history : EmpireHistoryType.emperor_die_history,
            new Dictionary<string, string>()
            {
                ["year_name"] = data.year_name,
                ["actor"] = this.Emperor.data.name
            });
        data.history_emperrors.Add(Emperor?.name);
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

    public bool CanJoinWar()
    {
        return Date.getMonthsSince(data.timestamp_invite_war_cool_down)>=3;
    }

    public void CreateNewEmpire(Kingdom kingdom, bool isSplit = false)
    {
        if (kingdom == null) return;
        if (kingdom.data == null) return;
        if (!kingdom.isAlive()) return;
        data.history_emperrors = new List<string>();
        data.heir_type = EmpireHeirLawType.eldest_child;
        data.last_exam_timestamp = World.world.getCurWorldTime();
        StartEmpireExam();
        Regime regime = kingdom.GetRegime();
        if (regime != null)
        {
            
            data.has_year_name = regime.HasEraName();
            LogService.LogInfo(regime.HasEraName().ToString());
            regime.Factions.ForEach(f=>
            {
                f.EmpireId = this.getID();
                f.TemporaryFactions.ForEach(tf=>tf.Init(f));
            });
        }
        data.timestamp_invite_war_cool_down = World.world.getCurWorldTime();
        CoreKingdom = kingdom;
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
                    this.data.directPre = GetDir(kingdom.getKingClan().GetHistoryEmpirePos());
                    empireName = kingdom.getKingClan().GetHistoryEmpireName();
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

    public bool AddCabinetMember(Actor actor)
    {
        if (actor == null) return false;
        if (!actor.HasOfficeIdentity()) return false;
        OfficeIdentity identity = actor.GetIdentity();
        if (identity.IsCabinet()) return false;
        identity.EnterCabinet();
        if (data.CabinetMembers.Contains(actor.id)) return false;
        data.CabinetMembers.Add(actor.id);
        return true;
    }

    public bool SetCabinetLeader(Actor actor)
    {
        if (actor == null) return false;
        if (!actor.HasOfficeIdentity()) return false;
        OfficeIdentity identity = actor.GetIdentity();
        RemoveCabinetMember(actor);
        data.CabinetMembers.Insert(0, actor.id);
        identity.EnterCabinet();
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
        Regime regime = CoreKingdom.GetRegime();
        var originalName = name + "\u200A" + LM.Get(regime.type == RegimeType.LvLing?"LvLing_empire":EmpireCraftKingdomBehCheckKingdomType.CalcKingdomType(CoreKingdom).ToString());
        data.name = string.IsNullOrEmpty(data.directPre)?originalName: string.Join("\u200A", data.directPre, originalName);
        CoreKingdom.data.name = data.name;
    }

    public void CheckDissolve(Kingdom mainKingdom)
    {
        this.kingdoms_hashset.Remove(mainKingdom);
        mainKingdom.EmpireLeave(false);
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
        newEmpire.data.history.InsertRange(0, data.history);
        newEmpire.SetEmpireName(newKingdom.GetKingdomName());
        if (newKingdom.capital.HasKingdomName()) 
        {
            SetEmpireName(newKingdom.capital.SelectKingdomName());
        }
        if (newKingdom.getKingClan().HasHistoryEmpire())
        {
            data.directPre = newEmpire.GetDir(newKingdom.getKingClan().GetHistoryEmpirePos());
            string empireName = newKingdom.getKingClan().GetHistoryEmpireName();
            newEmpire.SetEmpireName(empireName);
        }
        if (newKingdom.king.HasTitle())
        {
            newEmpire.SetEmpireName(newKingdom.king.GetTitle());
        }
        if (newKingdom.getKingClan() == EmpireClan)
        {
            data.directPre = newEmpire.GetDir(this._empireCenter);
            newEmpire.SetEmpireName(GetEmpireName());
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
            newEmpire.kingdoms_hashset.Add(kingdom);
            kingdom.EmpireJoin(newEmpire);
            newEmpire.data.timestamp_member_joined = World.world.getCurWorldTime();
            
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
        foreach (Kingdom kingdom in this.kingdoms_hashset)
        {
            kingdom.EmpireLeave();
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

        foreach (var k in this.given_Kingdoms)
        {
            this.data.given_Kingdoms.Add(k.getID());
        }

        foreach (var k in this.taken_Kingdoms)
        {
            this.data.taken_Kingdoms.Add(k.getID());
        }
        if (this.Emperor != null)
            this.data.emperor = this.Emperor.data.id;
        else
            this.data.emperor = -1L;
        this.data.empire = this.CoreKingdom.data.id;
        this.data.original_capital = !this.OriginalCapital.isRekt() ? this.OriginalCapital.data.id : -1L;
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
                kingdoms_hashset.Add(tKingdom);
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
        
        this.CoreKingdom = World.world.kingdoms.get(pData.empire);
        this.EmpireClan = World.world.clans.get(pData.empire_clan);
        this.OriginalCapital = World.world.cities.get(pData.original_capital);
        this.recalculate();
    }

    // Token: 0x06001128 RID: 4392 RVA: 0x000C7890 File Offset: 0x000C5A90
    public void join(Kingdom pKingdom, bool pRecalc = true, bool pForce = false)
    {
        if (hasKingdom(pKingdom))
        {
            return;
        }
        if (!pForce && !this.canJoin(pKingdom))
        {
            return;
        }
        this.kingdoms_hashset.Add(pKingdom);
        pKingdom.EmpireJoin(this);
        if (pRecalc)
        {
            this.recalculate();
        }
        this.data.timestamp_member_joined = World.world.getCurWorldTime();
    }

    public void leave(Kingdom pKingdom, bool pRecalc = true)
    {
        this.kingdoms_hashset.Remove(pKingdom);
        pKingdom.EmpireLeave(false);
        if (pKingdom.IsEmpire())
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
                new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_enfeoff_log, this.name)
                {
                    location = this.CoreKingdom.location,
                    color_special1 = this.CoreKingdom.getColor().getColorText()
                }.add();
                this.join(newKingdom, true, false);
                WorldLog.logNewKingdom(newKingdom);
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
        EmpireCraftMetaTypeLibrary.selected_empire = this;
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
        this.given_Kingdoms.Clear();
        this.taken_Kingdoms.Clear();
        this.CoreKingdom = null;
        if (!ModClass.ALL_HISTORY_DATA.ContainsKey(this.data.id))
        {
            ModClass.ALL_HISTORY_DATA.Add(this.data.id, this.data.history);
        }
    }
}