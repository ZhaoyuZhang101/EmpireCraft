
using System;
using System.IO;
using NeoModLoader.services;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using EmpireCraft.Scripts.Layer;
using static EmpireCraft.Scripts.GameClassExtensions.ActorExtension;
using static EmpireCraft.Scripts.GameClassExtensions.CityExtension;
using static EmpireCraft.Scripts.GameClassExtensions.KingdomExtension;
using static EmpireCraft.Scripts.GameClassExtensions.ClanExtension;
using static EmpireCraft.Scripts.GameClassExtensions.WarExtension;
using db;
using EmpireCraft.Scripts.AI.KingdomAI;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.HelperFunc;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using EmpireCraft.Scripts.Compatibility;
using EmpireCraft.Scripts.GeneralSystems;
using EmpireCraft.Scripts.GamePatches;
using EmpireCraft.Scripts.Enums;

namespace EmpireCraft.Scripts.Data;

public static class DataManager
{
    public const string EmpireCraftSaveFileName = "EmpireCraftModData.json";
    public static string CurrentSaveDataPath { get; private set; } = "";

    public static void ResetCurrentSaveDataPath()
    {
        CurrentSaveDataPath = "";
    }

    // 读档各段耗时(玩家截图 2026-10-27：读档里"LOADING EMPIRE MOD DATA"一步 8.7 秒)：结束时一行日志 [读档耗时]
    private static global::System.Diagnostics.Stopwatch _loadWatch;
    private static global::System.Text.StringBuilder _loadLaps;

    private static void Lap(string phase)
    {
        if (_loadWatch == null) return;
        _loadLaps.Append(phase).Append(' ').Append((_loadWatch.ElapsedMilliseconds / 1000d).ToString("0.00")).Append("秒；");
        _loadWatch.Restart();
    }

    public static void LoadAll(string loadRootPath)
    {
        _loadWatch = global::System.Diagnostics.Stopwatch.StartNew();
        _loadLaps = new global::System.Text.StringBuilder();
        try
        {
            LoadAllCore(loadRootPath);
        }
        finally
        {
            Lap("其余整理");
            LogService.LogInfo($"[EmpireCraft][读档耗时] {_loadLaps}");
            _loadWatch = null;
            _loadLaps = null;
        }
    }

    private static void LoadAllCore(string loadRootPath)
    {
        InstitutionSystem.ResetWorldState();
        TechnologySystem.ResetWorldState();
        FamousTalentSystem.ResetWorldState();
        CityPopulationSystem.ResetWorldState();
        MapWallSystem.ResetWorldState();
        RoadPaintSystem.ResetWorldState();
        string loadPath = Path.Combine(loadRootPath, EmpireCraftSaveFileName);
        CurrentSaveDataPath = loadPath;
        NormalizeLoadedNameSeparators();
        if (!File.Exists(loadPath))
        {
            InitialRegimeForAll();
            RepairTemplateNamesWithoutSave();
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        FactionRatioConverter.ResetLegacyDiscardCount();
        SaveData saveData = null;
        // 文件截断、格式坏了时 JsonConvert 抛异常而不是返回空：接住，当作读不出来(下面按文化给各国定政体)
        // 边读边解析(以前先把整个文件读成一个字符串，上百兆的存档要多占两三百兆内存，读完还要回收)
        try
        {
            using var stream = new FileStream(loadPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var reader = new StreamReader(stream, global::System.Text.Encoding.UTF8, true, 1 << 16);
            using var json = new JsonTextReader(reader);
            saveData = JsonSerializer.CreateDefault().Deserialize<SaveData>(json);
        }
        catch (Exception e)
        {
            LogService.LogError($"模组存档读不出来(文件损坏？)，各国按文化重定政体: {e}");
        }
        Lap("解析");
        NormalizeSaveData(saveData);
        InstitutionSystem.ImportCultureStates(saveData?.cultureInstitutionStates);
        TechnologySystem.ImportStates(saveData?.cultureTechStates);
        FamousTalentSystem.Import(saveData?.famousTalents);
        if (FactionRatioConverter.DiscardedLegacyEntryCount > 0)
        {
            LogService.LogWarning($"已迁移旧存档中 {FactionRatioConverter.DiscardedLegacyEntryCount} 条无法识别的派系占比；将在加载后按当前派系配置重建。");
        }
        LogService.LogInfo("初始化模组数据模板");
        bool isOldSave = saveData == null || saveData.mod_version < ModClass.MOD_DATA_VERSION;

        // 一份模组数据都读不出来(文件损坏)：同没有存档文件，按文化给各国定政体(以前直接返回，政体留在默认值律令)
        if (saveData == null)
        {
            InitialRegimeForAll();
            RepairTemplateNamesWithoutSave();
            LogService.LogInfo("没有找到任何保存数据。");
            return;
        }
        // 人物附加数据为空不等于没有存档：世上一个实体小人都没有的世界(无小人、人口全在背景里)存不出人物数据，
        // 以前在这里整个返回，国家、城市、文化、人口、法理一概没读——全图律令制(政体默认值)、国名被改、文化图层没颜色、
        // 人口不长(玩家 2026-10-22 反馈，最新版读旧档)。现在只跳过人物这一段
        if (saveData.actorsExtraData.Count == 0) LogService.LogInfo("存档里没有人物附加数据(世上没有实体小人)，其余照常读取。");
        List<string> a = new List<string>();
        var unitById = World.world.units.ToDictionary(u => u.getID());
        var kingdomById = World.world.kingdoms.ToDictionary(k => k.getID());
        var cityById = World.world.cities.ToDictionary(c => c.getID());
        var clanById = World.world.clans.ToDictionary(c => c.getID());
        var warById = World.world.wars.ToDictionary(w => w.getID());
        var religionById = World.world.religions.ToDictionary(r => r.getID());
        CulturePatch.ImportCultureBindings(saveData.cultureBindings);
        // 没记绑定的旧档里混进字表名的起名模板重套(见 TemplateNameRepair)
        Guarded("起名模板修复", TemplateNameRepair.RepairCultures);
        LogService.LogInfo("准备各项数据");
        OfficeManager.Offices = saveData.officeObjects;
        // 批量同步
        foreach (var entry in saveData.actorsExtraData)
        {
            if (entry == null) continue;
            if (unitById.TryGetValue(entry.id, out Actor actor))
                actor.SyncData(entry);
        }
        LogService.LogInfo("Sync Actor Data");
        Lap("人物");
        // 没存过政体选项的旧存档，读完后按封号修复成员地位(见 RepairLegacyRegimeRole)
        var legacyRegimeKingdoms = new HashSet<long>();
        foreach (var entry in saveData.kingdomExtraData)
        {
            if (entry == null) continue;
            try
            {
                if (kingdomById.TryGetValue(entry.id, out var kingdom))
                {
                    if (entry.saved_regime_options == null) legacyRegimeKingdoms.Add(entry.id);
                    kingdom.SyncData(entry);
                    if (kingdom.GetOfficeID() == -1L)
                    {
                        bool manuallySelected = kingdom.GetOrCreate().regime_manually_selected;
                        // 旧制犹存的按旧制所属的文化(见 LegacyInstitutionSystem)
                        var culture = CultureService.GetInstitutionCulture(kingdom);
                        RegimeType regimeType = InstitutionSystem.TryResolveCultureRegime(culture, out RegimeType resolvedRegime)
                ? resolvedRegime
                : OnomasticsRule.ALL_CULTURE_RULE.TryGetValue(culture, out Setting setting)
                    ? setting.regime
                    : RegimeType.Feudalism;
                        if (manuallySelected || RepublicSystem.IsRegimeLocked(kingdom))
                            regimeType = kingdom.GetOrCreate().regimeType;
                        // 君主制帝国里的成员不因本文化已废君主制而成了现代政体(同 CultureService 的政体换算)
                        else regimeType = InstitutionSystem.AdjustForMonarchyEmpire(kingdom, regimeType);
                        kingdom.SetRegimeType(regimeType);
                        kingdom.GetOrCreate().regime_manually_selected = manuallySelected;
                        kingdom.LoadRegime();
                        EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(kingdom);
                    }
                }
            }
            catch (Exception e)
            {
                LogService.LogError($"读取国家 {entry.id} 的模组数据失败，已跳过: {e}");
            }
        }
        LogService.LogInfo("Sync Kingdom Data");
        Lap("国家");
        foreach (var entry in saveData.cityExtraData)
        {
            if (entry == null) continue;
            if (cityById.TryGetValue(entry.id, out var city))
            {
                city.SyncData(entry);
                if (city.GetOfficeID() == -1L && city.kingdom != null && !city.kingdom.isRekt())
                {
                    Regime regime = city.kingdom.GetRegime();
                    if (regime?.bureau_config?.cities != null)
                    {
                        CityType cityType = EmpireCraftKingdomBehCheckKingdomType.CalcCityType(city.kingdom);
                        if (!regime.bureau_config.cities.TryGetValue(cityType, out BureauSetting citySetting))
                        {
                            continue;
                        }
                        OfficeObject officeObject = city.GetOffice();
                        if (officeObject != null)
                        {
                            officeObject.InitialOffice(citySetting, isNew:false);
                            officeObject.regimeType = regime.type;
                        }
                        else
                        {
                            officeObject = new OfficeObject();
                            officeObject.InitialOffice(citySetting);
                            officeObject.regimeType = regime.type;
                            city.SetOffice(officeObject);
                        }
                    }
                }
            }
        }
        LogService.LogInfo("Sync City Data");
        Lap("城市");
        foreach (var entry in saveData.clanExtraData)
        {
            if (entry == null) continue;
            if (clanById.TryGetValue(entry.id, out var clan))
                clan.SyncData(entry);
        }
        LogService.LogInfo("Sync Clan Data");
        Lap("氏族");
        if (saveData.bookExtraData != null && saveData.bookExtraData.Count > 0)
        {
            var bookById = World.world.books.ToDictionary(b => b.getID());
            foreach (var entry in saveData.bookExtraData)
            {
                if (entry != null && bookById.TryGetValue(entry.id, out var book))
                    book.SyncData(entry);
            }
        }
        foreach (var entry in saveData.warExtraData)
        {
            if (entry == null) continue;
            if (warById.TryGetValue(entry.id, out var war))
                war.SyncData(entry);
        }
        LogService.LogInfo("Sync War Data");
        foreach (var entry in saveData.religionExtraData)
        {
            if (entry == null) continue;
            if (religionById.TryGetValue(entry.id, out var religion))
                religion.SyncData(entry);
        }
        LogService.LogInfo("Sync Religion Data");
        // 史书与已亡帝国名录先读进来：下面整理帝国时就可能有帝国解散，要记进这两处(见 EmpireManager.dissolveEmpire)
        ModClass.ALL_HISTORY_DATA = saveData.all_history;
        FallenEmpireRecords.Load(saveData.fallen_empires);
        Guarded("整理帝国成员", () => EmpireMembershipService.NormalizeLoadedMembership(saveData.empireDatas,
            World.world.kingdoms,
            new HashSet<long>(saveData.kingdomExtraData.Where(entry => entry != null).Select(entry => entry.id))));
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            if (empireData == null) continue;
            try
            {
                NormalizeEmpireData(empireData);
                // 旧版"减税随时能提、加税几乎提不成"把基础税率压到了 0(见 TempFac_提高赋税)：恢复默认税率
                if (empireData.TaxRate < Empire.MinimumTaxRate) empireData.TaxRate = 0.2f;
                Empire empire = new Empire();
                empire.loadData(empireData);
                ModClass.EMPIRE_MANAGER.addObject(empire);
                LogService.LogInfo($"加载帝国{empire.name}");
                if (empire.data.centerOffice == null && empire.CoreKingdom != null)
                {
                    empire.data.centerOffice = new CenterOffice();
                    empire.data.centerOffice.Init(empire.CoreKingdom);
                    empire.CoreKingdom.SetLevel(0);
                }
                if (empire.data.centerOffice != null && empire.CoreKingdom != null)
                {
                    empire.data.centerOffice.SyncMetaObject(empire.CoreKingdom);
                }
            }
            catch (Exception e)
            {
                LogService.LogError($"加载帝国数据失败，已跳过: {e}");
            }
        }
        // 只整理成员与核心国，不跑帝国月度更新：帝国核心、法理、宗族等还在后面才读(见 EmpireManager.RepairAfterLoad)
        Guarded("整理帝国", () => ModClass.EMPIRE_MANAGER.RepairAfterLoad());
        Guarded("已亡帝国名录", () => FallenEmpireRecords.AdoptArchived(ModClass.EMPIRE_MANAGER));
        LogService.LogInfo("Sync Empire Data");
        Lap("战争宗教帝国");
        BookNamingSystem.RepairLegacyNames();
        EmpireCoreManager.EmpireCores = BuildEmpireCoreMap(saveData.empireCoreDatas);

        List<KingdomTitleData> titleDatas = saveData.kingdomTitleDatas;
        if (titleDatas == null || titleDatas.Count == 0)
        {
            titleDatas = RebuildKingdomTitleDataFromCityData(saveData, cityById);
        }
        // 城市自己记的法理只在指向一个确实存在的法理时才作数：-1(城市这边的记录丢了)或指向已不存在的法理时，
        // 以法理存档里的城市名单为准，否则读旧档时这些城市会被整片移出法理
        HashSet<long> existingTitleIds = new HashSet<long>(titleDatas.Where(data => data != null).Select(data => data.id));
        Dictionary<long, long> savedCityTitles = saveData.cityExtraData
            .Where(cityData => cityData != null && cityData.title_id > 0 && existingTitleIds.Contains(cityData.title_id))
            .GroupBy(cityData => cityData.id)
            .ToDictionary(group => group.Key, group => group.Last().title_id);

        foreach (KingdomTitleData kingdomTitleData in titleDatas)
        {
            if (kingdomTitleData == null) continue;
            try
            {
                NormalizeKingdomTitleData(kingdomTitleData, savedCityTitles, cityById);
                if (kingdomTitleData.cities.Count == 0) continue;
                KingdomTitle kt = new KingdomTitle();
                kt.loadData(kingdomTitleData);
                if (kt.getCities().Any())
                {
                    kt.isBeenControlled();
                }
                ModClass.KINGDOM_TITLE_MANAGER.addObject(kt);
            }
            catch (Exception e)
            {
                LogService.LogError($"加载法理头衔数据失败，已跳过: {e}");
            }

        }
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        // 旧存档没有法理的初始文化：以现在的文化补上(见 CultureService.RememberInitialTitleCulture)
        foreach (KingdomTitle title in ModClass.KINGDOM_TITLE_MANAGER.ToList())
            CultureService.RememberInitialTitleCulture(title);
        // 修复：临时子核心抢走的原核心城市归还原核心(见 EmpireCoreManager.RestoreParentCities)
        Guarded("帝国核心修复", () =>
        {
            EmpireCoreManager.RepairContestedCoreOwners();
            EmpireCoreManager.RestoreAllParentCities();
            EmpireCoreManager.RepairUntitledCoreCities();
            EmpireCoreManager.ReconcileCityCores();
        });
        // 兼属的反查表重建；旧存档的争议地带宣称折成兼属(见 DeJureShareSystem)
        Guarded("兼属", DeJureShareSystem.AfterLoad);
        // 本朝法理里因水土设的羁縻改回直辖(一次)
        Guarded("水土羁縻", ShuiTuSystem.RestoreHomelandJimi);
        Guarded("文化数据迁移", CultureService.MigrateWorldCultureData);
        SpecificClanManager._specificClans = saveData.specificClans;
        SpecificClanManager.RebuildCache();
        // Run after culture bindings, extra data and genealogy links are restored.
        foreach (Actor actor in World.world.units)
            if (actor?.data != null && !AncientWarfareCompatibility.OwnsObject(actor))
                actor.GetModName()?.RepairOrder(actor);
        // 已经按坏模板起出来的"Unitnamesmale1""Clannames"按文化重起(要在宗族、族谱读进来以后，整理国家之前)
        Guarded("字表名修复", TemplateNameRepair.RepairNames);
        try
        {
            int spouseFixes = SpecificClanManager.RepairSpouseRecords();
            if (spouseFixes > 0) LogService.LogInfo($"[EmpireCraft] 读档修复了 {spouseFixes} 条对不上的配偶记录");
        }
        catch (Exception e)
        {
            LogService.LogError($"修复配偶记录失败，已跳过: {e}");
        }
        LogService.LogInfo("Sync Titles Data");
        Lap("法理");
        ConfigData.yearNameSubspecies = saveData.yearNameSubspecies;
        LogService.LogInfo("Sync history Data");
        ModClass.CULTURE_NO_EMPIRE_SINCE = saveData.culture_no_empire_since ?? new Dictionary<string, double>();
        ModClass.FEUDAL_EMPIRE_LINEAGE = saveData.feudal_empire_lineage ?? new Dictionary<string, List<long>>();
        ModClass.MOD_ALLIANCE_IDS = new HashSet<long>(saveData.mod_alliance_ids ?? new List<long>());
        MapWallSystem.Load(saveData.map_walls);
        RoadPaintSystem.Load(saveData.painted_roads);
        RuralSettlementSystem.Load(saveData.rural_settlements);
        FactorySystem.Load(saveData.factories);
        RoadTreatySystem.Load(saveData.road_treaties);
        if (PlayerConfig.dict.TryGetValue("switch_real_num", out PlayerOptionData realNumOption))
        {
            realNumOption.boolVal = saveData.switch_real_num;
        }
        ModClass.REAL_NUM_SWITCH = saveData.switch_real_num;
        EmpireCraft.Scripts.GeneralSystems.WorldMap.WorldMapScale.ZoneKm = saveData.world_map_zone_km;
        if (PlayerConfig.dict.TryGetValue("switch_simple_nameplate", out PlayerOptionData simpleNameplateOption))
        {
            simpleNameplateOption.boolVal = saveData.switch_simple_nameplate;
        }
        ModClass.SIMPLE_NAMEPLATE_SWITCH = saveData.switch_simple_nameplate;
        if (PlayerConfig.dict.TryGetValue("switch_empire_show_alliance", out PlayerOptionData empireAllianceOption))
        {
            empireAllianceOption.boolVal = saveData.switch_empire_show_alliance;
        }
        ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH = saveData.switch_empire_show_alliance;
        foreach (var worldKingdom in World.world.kingdoms)
        {
            if (worldKingdom == null || worldKingdom.isRekt()) continue;
            if (EmpireCraft.Scripts.Compatibility.AncientWarfareCompatibility.Owns(worldKingdom)) continue;
            try
            {
                if (isOldSave)
                {
                    worldKingdom.InitialRegime();
                }
                worldKingdom.NormalizeFactionRebelName();
                worldKingdom.SyncRealmTitlesFromRuler();
                if (worldKingdom.hasKing()) worldKingdom.TransferRealmTitlesToRuler(worldKingdom.king);
                worldKingdom.ReconcileMainTitle();
                worldKingdom.GetInitialRandomKingdomName();
                if (legacyRegimeKingdoms.Contains(worldKingdom.id)) worldKingdom.RepairLegacyRegimeRole();
                EmpireCraftKingdomBehCheckKingdomType.SyncKingdomStatus(worldKingdom);
            }
            catch (Exception e)
            {
                LogService.LogError($"读档整理国家 {worldKingdom.id} 失败，已跳过: {e}");
            }
        }
        NomadFeudalismSystem.RepairAfterLoad();
        OfficeManager.Prune();
        foreach (Empire empire in ModClass.EMPIRE_MANAGER
                     .Where(value => value != null && !value.IsArchived() && !value.isRekt()).ToList())
        {
            try
            {
                CompositeEmpireService.SynchronizeRegionalInstitutions(empire);
                RealmNamingSystem.NormalizeGrandName(empire);
                CompositeEmpireService.TryAdoptInstitutionalCultureName(empire);
                InstitutionSystem.MigrateEmpire(empire);
            }
            catch (Exception exception)
            {
                LogService.LogError($"帝国文化与制度存档迁移失败，保留原制度: {exception}");
            }
        }
        // 旧档里一部自立的部落帝国静默撤去(部落不再是帝国，用户 2026-10-26)；要在帝国、法理、制度都读进来以后、
        // 解散诊断记底(EmpireLossDiagnostics.OnLoaded)之前
        Guarded("部落帝国迁移", TribalFissionService.AfterLoad);
        Guarded("空世界补人口", () =>
        {
            int seeded = CityPopulationSystem.ReseedEmptyWorld();
            if (seeded > 0) LogService.LogWarning($"全世界的城都没有人口(旧版读档丢了人口又存过档)，已给 {seeded} 座城各补一户");
        });
        // 只读的一致性检查：把帝国/王国/诉求/议会的状态错乱统一以 [存档检查] 写进日志
        try
        {
            EmpireCraft.Scripts.Diagnostics.SaveIntegrityChecker.Run("读档");
        }
        catch (Exception exception)
        {
            LogService.LogWarning($"存档一致性检查失败: {exception}");
        }
        // 给读进来的各帝国记底，读档后按月对账(见 EmpireLossDiagnostics)
        EmpireCraft.Scripts.Diagnostics.EmpireLossDiagnostics.OnLoaded();
    }

    // 没有模组存档、或读不出来：各国按文化定政体(古战模组的国家不归本模组管)
    private static void InitialRegimeForAll()
    {
        foreach (var worldKingdom in World.world.kingdoms)
        {
            if (worldKingdom == null || worldKingdom.isRekt() || AncientWarfareCompatibility.Owns(worldKingdom)) continue;
            try
            {
                worldKingdom.InitialRegime();
            }
            catch (Exception e)
            {
                LogService.LogError($"国家 {worldKingdom.id} 按文化定政体失败: {e}");
            }
        }
    }

    // 没有模组存档的图(原版图、模组数据丢了)也可能带着混进字表名的起名模板(见 TemplateNameRepair)
    private static void RepairTemplateNamesWithoutSave()
    {
        Guarded("起名模板修复", TemplateNameRepair.RepairCultures);
        Guarded("字表名修复", TemplateNameRepair.RepairNames);
    }

    // 读档链上的一段出错只记日志：以前整个读档只有最外面一层 try，一段抛异常，后面定政体、国名、帝国迁移全部跳过
    private static void Guarded(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            LogService.LogError($"读档[{what}]失败，已跳过这一段: {e}");
        }
    }

    private static void NormalizeLoadedNameSeparators()
    {
        if (World.world == null) return;

        foreach (Actor actor in World.world.units)
        {
            if (actor?.data == null || AncientWarfareCompatibility.OwnsObject(actor)) continue;
            actor.data.name = actor.data.name.UseLocalizedNameSeparator();
            if (actor.clan?.data != null)
                actor.clan.data.name = actor.clan.data.name.UseLocalizedNameSeparator();
            if (actor.family?.data != null)
                actor.family.data.name = actor.family.data.name.UseLocalizedNameSeparator();
        }

        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || AncientWarfareCompatibility.Owns(kingdom)) continue;
            kingdom.data.name = kingdom.data.name.UseLocalizedNameSeparator();
        }

        foreach (City city in World.world.cities)
        {
            if (city?.data == null || AncientWarfareCompatibility.Owns(city.kingdom)) continue;
            city.data.name = city.data.name.UseLocalizedNameSeparator();
        }
    }
    public static void SaveAll(string saveRootPath)
    {
        if (string.IsNullOrEmpty(saveRootPath))
        {
            LogService.LogError("保存路径为空，无法保存mod数据");
            return;
        }
        string savePath = Path.Combine(saveRootPath, EmpireCraftSaveFileName);
        CurrentSaveDataPath = savePath;
        var saveTimer = global::System.Diagnostics.Stopwatch.StartNew();
        CultureService.MigrateWorldCultureData();
        SaveData saveData = new SaveData();
        saveData.actorsExtraData = World.world.units.Select(a=>a.GetExtraData<Actor, ActorExtraData>(true)).Where(ed=>ed!=null).ToList();
        saveData.cityExtraData = World.world.cities.Select(a => a.GetExtraData<City, CityExtraData>(true)).Where(ed => ed != null).ToList();
        saveData.religionExtraData = World.world.religions.Select(a => a.GetExtraData<Religion, ReligionExtension.ReligionExtraData>(true)).Where(ed => ed != null).ToList();
        // 派系与政党随王国一起存(政体对象本身不存，见 KingdomExtraData.saved_factions)；
        // 读档后还没建过政体的王国保留读进来的那份
        foreach (Kingdom kingdom in World.world.kingdoms)
        {
            if (kingdom?.data == null || kingdom.isRekt()) continue;
            KingdomExtraData extra = kingdom.GetOrCreate();
            if (extra?.regime?.options != null)
            {
                extra.saved_regime_options = extra.regime.options.Where(option => option.Value != null && option.Value.Length > 0)
                    .ToDictionary(option => option.Key, option => option.Value[0]);
                extra.saved_regime_options_type = extra.regime.type;
            }
            if (extra?.regime?.PlayerFactions == null) continue;
            extra.saved_factions = extra.regime.PlayerFactions.Where(f => f != null).Select(f => f.ToSaveCopy()).ToList();
            extra.saved_factions_regime = extra.regime.type;
        }
        saveData.kingdomExtraData = World.world.kingdoms.Select(a => a.GetExtraData<Kingdom, KingdomExtraData>(true)).Where(ed => ed != null).ToList();
        saveData.warExtraData = World.world.wars.Select(a => a.GetExtraData<War, WarExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.clanExtraData = World.world.clans.Select(a => a.GetExtraData<Clan, ClanExtraData>(true)).Where(ed => ed != null).ToList(); ;
        saveData.bookExtraData = World.world.books.Select(b => b.GetExtraData<Book, BookExtension.BookExtraData>(true))
            .Where(ed => ed != null && (!string.IsNullOrEmpty(ed.landmark_id) || !string.IsNullOrEmpty(ed.culture))).ToList();
        OfficeManager.Prune();
        saveData.empireDatas = new List<EmpireData>(ModClass.EMPIRE_MANAGER.Count);
        saveData.empireCoreDatas = EmpireCoreManager.EmpireCores.Values.Where(c => c != null).ToList();
        saveData.kingdomTitleDatas = new List<KingdomTitleData>(ModClass.KINGDOM_TITLE_MANAGER.Count);
        saveData.cultureBindings = CulturePatch.ExportCultureBindings();
        saveData.cultureInstitutionStates = InstitutionSystem.ExportCultureStates();
        saveData.cultureTechStates = TechnologySystem.ExportStates();
        saveData.famousTalents = FamousTalentSystem.Export();
        ModClass.EMPIRE_MANAGER.update(-1L);
        ModClass.KINGDOM_TITLE_MANAGER.update(-1L);
        saveData.officeObjects = OfficeManager.Offices;
        saveData.mod_version = ModClass.MOD_DATA_VERSION;
        foreach (Empire empire in ModClass.EMPIRE_MANAGER)
        {
            try
            {
                empire.save();
                saveData.empireDatas.Add(empire.data);
            } catch (Exception e)
            {
                LogService.LogError($"帝国存档更新失败，保留现有数据: {e}");
                if (empire?.data != null && !saveData.empireDatas.Contains(empire.data))
                {
                    saveData.empireDatas.Add(empire.data);
                }
            }

        }
        foreach (KingdomTitle kt in ModClass.KINGDOM_TITLE_MANAGER)
        {
            try
            {
                kt.save();
                saveData.kingdomTitleDatas.Add(kt.data);
            } 
            catch (Exception e)
            {
                LogService.LogError($"头衔存档更新失败，保留现有数据: {e}");
                if (kt?.data != null && !saveData.kingdomTitleDatas.Contains(kt.data))
                {
                    saveData.kingdomTitleDatas.Add(kt.data);
                }
            }

        }
        saveData.yearNameSubspecies = ConfigData.yearNameSubspecies;
        saveData.all_history = ModClass.ALL_HISTORY_DATA;
        saveData.fallen_empires = FallenEmpireRecords.Save();
        saveData.culture_no_empire_since = ModClass.CULTURE_NO_EMPIRE_SINCE;
        saveData.feudal_empire_lineage = ModClass.FEUDAL_EMPIRE_LINEAGE;
        saveData.mod_alliance_ids = ModClass.MOD_ALLIANCE_IDS.ToList();
        saveData.map_walls = MapWallSystem.Save();
        saveData.painted_roads = RoadPaintSystem.Save();
        saveData.rural_settlements = RuralSettlementSystem.Save();
        saveData.factories = FactorySystem.Save();
        saveData.road_treaties = RoadTreatySystem.Save();
        saveData.specificClans = SpecificClanManager._specificClans;
        saveData.switch_real_num = ModClass.REAL_NUM_SWITCH;
        saveData.world_map_zone_km = EmpireCraft.Scripts.GeneralSystems.WorldMap.WorldMapScale.ZoneKm;
        saveData.switch_simple_nameplate = ModClass.SIMPLE_NAMEPLATE_SWITCH;
        saveData.switch_empire_show_alliance = ModClass.EMPIRE_SHOW_ALLIANCE_SWITCH;
        double collectMs = saveTimer.Elapsed.TotalMilliseconds;
        // 流式序列化直接写盘：不再先攒出一个几 MB 的 JSON 大字符串(大对象堆分配 + 复制各一次)
        using (var writer = new StreamWriter(savePath, false, new global::System.Text.UTF8Encoding(false)))
            JsonSerializer.CreateDefault().Serialize(writer, saveData);
        double serializeMs = saveTimer.Elapsed.TotalMilliseconds - collectMs;
        // 序列化完再清掉内存里的派系副本：政体已在内存中的王国不需要它，留着反而会在之后重建政体时被误用
        foreach (KingdomExtraData extra in saveData.kingdomExtraData)
            if (extra?.regime != null)
            {
                extra.saved_factions = null;
                extra.saved_regime_options = null;
            }
        LogService.LogInfo($"Save Data: actors={saveData.actorsExtraData.Count}, wars={saveData.warExtraData.Count}, kingdoms={saveData.kingdomExtraData.Count}, cities={saveData.cityExtraData.Count}, religions={saveData.religionExtraData.Count}");
        double totalMs = saveTimer.Elapsed.TotalMilliseconds;
        LogService.LogInfo($"[EmpireCraft][存档] 收集 {collectMs:0} ms / 序列化写盘 {serializeMs:0} ms，" +
                           $"共 {totalMs:0} ms，文件 {new FileInfo(savePath).Length / 1048576d:0.0} MB");
        LogService.LogInfo("Save Finished");
    }

    private static void NormalizeSaveData(SaveData saveData)
    {
        if (saveData == null) return;
        saveData.actorsExtraData ??= new List<ActorExtraData>();
        saveData.kingdomExtraData ??= new List<KingdomExtraData>();
        saveData.cityExtraData ??= new List<CityExtraData>();
        saveData.clanExtraData ??= new List<ClanExtraData>();
        saveData.warExtraData ??= new List<WarExtraData>();
        saveData.religionExtraData ??= new List<ReligionExtension.ReligionExtraData>();
        saveData.empireDatas ??= new List<EmpireData>();
        saveData.empireCoreDatas ??= new List<EmpireCore>();
        saveData.kingdomTitleDatas ??= new List<KingdomTitleData>();
        saveData.cultureBindings ??= new Dictionary<long, string>();
        saveData.cultureInstitutionStates ??= new Dictionary<string, CultureInstitutionState>();
        foreach (CultureInstitutionState state in saveData.cultureInstitutionStates.Values)
        {
            InstitutionStateNormalizer.Normalize(state);
        }
        // 独立王国自己推的制度改革(可空，见 InstitutionRealm)
        foreach (KingdomExtraData extra in saveData.kingdomExtraData)
            InstitutionStateNormalizer.Normalize(extra?.institution_state);
        saveData.yearNameSubspecies ??= new List<string>();
        saveData.all_history ??= new Dictionary<long, List<EmpireCraftHistory>>();
        saveData.specificClans = saveData.specificClans?.Where(sc => sc != null).ToList() ?? new List<SpecificClan>();
        saveData.officeObjects ??= new Dictionary<long, OfficeObject>();
        foreach (EmpireData empireData in saveData.empireDatas)
        {
            NormalizeEmpireData(empireData);
        }
        foreach (KingdomTitleData titleData in saveData.kingdomTitleDatas)
        {
            NormalizeKingdomTitleCollections(titleData);
        }
    }

    private static void NormalizeEmpireData(EmpireData data)
    {
        if (data == null) return;
        data.CabinetMembers ??= new List<long>();
        data.court ??= new CourtState();
        data.PreviousYearsMoney ??= new List<int>();
        data.history ??= new List<EmpireCraftHistory>();
        data.given_Kingdoms ??= new List<long>();
        data.taken_Kingdoms ??= new List<long>();
        data.additions ??= new EmpireAddition();
        data.additions.addition ??= new Dictionary<OfficerPowerType, int>();
        foreach (OfficerPowerType powerType in OfficeManager.AllPower ?? new List<OfficerPowerType>())
        {
            if (!data.additions.addition.ContainsKey(powerType))
            {
                data.additions.addition[powerType] = 0;
            }
        }
        data.kingdoms ??= new List<long>();
        data.cities ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.honorary_peerage_holders ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_identities ??= new Dictionary<string, long>();
        data.honorary_peerage_holder_names ??= new Dictionary<string, string>();
        data.defeated_empire_houses ??= new List<DefeatedEmpireHouse>();
        data.legal_peerage_holders ??= new Dictionary<long, long>();
        data.legal_peerage_holder_identities ??= new Dictionary<long, long>();
        data.legal_peerage_types ??= new Dictionary<long, string>();
        data.legal_peerage_kingdoms ??= new Dictionary<long, long>();
        data.conquest_founder_culture ??= "";
        data.ruling_culture ??= "";
        data.institutional_culture ??= "";
        data.external_identity_culture ??= "";
        data.military_tradition ??= "";
        data.composite_cultural_name ??= "";
        data.composite_cultural_name_culture ??= "";
        data.composite_cultural_name_source ??= "";
        data.institution_state ??= new InstitutionEmpireState();
        InstitutionStateNormalizer.Normalize(data.institution_state);
        NormalizeHolyRoman(data.holy_roman);
        data.composite_integration = Math.Max(0, Math.Min(100, data.composite_integration));
        data.central_plains_legitimacy = Math.Max(0, Math.Min(100, data.central_plains_legitimacy));
        data.ruling_tradition_legitimacy = Math.Max(0, Math.Min(100, data.ruling_tradition_legitimacy));
        foreach (EmpireCraftHistory history in data.history)
        {
            if (history == null) continue;
            history.initial_cities ??= new List<string>();
            history.descriptions ??= new List<HistoryDescription>();
            foreach (HistoryDescription description in history.descriptions)
            {
                if (description == null) continue;
                description.cities ??= new List<string>();
            }
        }
    }

    // 神罗式帝国(见 HolyRomanSystem)：补空表；已有议会、选举、改革这些动静却还没起算帝国权威的(旧档)记 -2，
    // 第一次年结按现状折算(不记事件)；从没有动静的照新帝国起算 50
    private static void NormalizeHolyRoman(HolyRomanState state)
    {
        if (state == null) return;
        state.concessions ??= new List<string>();
        state.diets ??= new List<DietRecord>();
        state.imperial_war_members ??= new List<long>();
        state.defiance ??= new Dictionary<long, double>();
        state.privileged_ids ??= new List<long>();
        state.circles ??= new Dictionary<long, int>();
        state.league_member_ids ??= new List<long>();
        state.authority_log ??= new List<AuthorityEntry>();
        state.protect_wars ??= new Dictionary<long, long>();
        state.private_wars ??= new List<PrivateWarRecord>();
        state.private_wars.RemoveAll(record => record == null);
        foreach (PrivateWarRecord record in state.private_wars) record.city_ids ??= new List<long>();
        state.lawsuits ??= new List<LawsuitRecord>();
        state.lawsuits.RemoveAll(record => record == null);
        state.court_log ??= new List<string>();
        state.elector_realm_ids ??= new List<long>();
        state.elector_log ??= new List<string>();
        state.campaign ??= new Dictionary<string, long>();
        state.elections ??= new List<ElectionRecord>();
        state.elections.RemoveAll(record => record == null);
        state.pawns ??= new List<PawnRecord>();
        state.pawns.RemoveAll(record => record == null);
        state.mediatizations ??= new List<MediatizationRecord>();
        state.mediatizations.RemoveAll(record => record == null);
        state.departed_log ??= new List<string>();
        bool active = state.last_diet_at >= 0d || state.house_terms > 0 || state.concessions.Count > 0 ||
                      state.circles.Count > 0 || state.sovereign_princes || state.imperial_peace_at >= 0d ||
                      state.league_leader_id >= 0;
        if (state.authority == -1 && active) state.authority = -2;
    }

    private static Dictionary<long, EmpireCore> BuildEmpireCoreMap(IEnumerable<EmpireCore> cores)
    {
        Dictionary<long, EmpireCore> result = new Dictionary<long, EmpireCore>();
        if (cores == null) return result;
        foreach (EmpireCore core in cores)
        {
            if (core == null || core.id <= 0) continue;
            core.titlesRecord ??= new List<(double time, long titleId)>();
            core.empire_history_ids ??= new List<long>();
            core.name = EmpireCoreManager.NormalizeCoreName(core.name);
            result[core.id] = core;
        }
        return result;
    }

    private static void NormalizeKingdomTitleCollections(KingdomTitleData data)
    {
        if (data == null) return;
        data.cities ??= new List<long>();
        data.history_emperrors ??= new List<string>();
        data.jurisdiction_history ??= new List<KingdomTitleJurisdictionRecord>();
        data.title_name_history ??= new Dictionary<string, string>();
        data.province_name_history ??= new Dictionary<string, string>();
    }

    private static void NormalizeKingdomTitleData(KingdomTitleData data, Dictionary<long, long> savedCityTitles,
        Dictionary<long, City> cityById)
    {
        if (data == null)
        {
            return;
        }

        NormalizeKingdomTitleCollections(data);
        HashSet<long> cityIds = new HashSet<long>();
        foreach (long cityId in data.cities)
        {
            if (cityById.ContainsKey(cityId) &&
                (savedCityTitles == null || !savedCityTitles.TryGetValue(cityId, out long savedTitleId) ||
                 savedTitleId == data.id))
            {
                cityIds.Add(cityId);
            }
        }

        if (savedCityTitles != null)
        {
            foreach (var cityData in savedCityTitles)
            {
                if (cityData.Value != data.id)
                {
                    continue;
                }

                if (cityById.ContainsKey(cityData.Key))
                {
                    cityIds.Add(cityData.Key);
                }
            }
        }

        data.cities = cityIds.ToList();

        if (!cityIds.Contains(data.title_capital) && data.cities.Count > 0)
        {
            data.title_capital = data.cities.First();
        }

        if (string.IsNullOrWhiteSpace(data.original_actor_asset) &&
            data.title_capital > 0 &&
            cityById.TryGetValue(data.title_capital, out City capital))
        {
            data.original_actor_asset = capital.kingdom?.king?.asset?.id ?? capital.kingdom?.asset?.id ?? capital.getSpecies();
        }
    }

    private static List<KingdomTitleData> RebuildKingdomTitleDataFromCityData(SaveData saveData, Dictionary<long, City> cityById)
    {
        List<KingdomTitleData> result = new List<KingdomTitleData>();
        if (saveData?.cityExtraData == null)
        {
            return result;
        }

        Dictionary<long, List<long>> titleCities = new Dictionary<long, List<long>>();
        foreach (var cityData in saveData.cityExtraData)
        {
            if (cityData == null || cityData.title_id <= 0 || !cityById.ContainsKey(cityData.id))
            {
                continue;
            }

            if (!titleCities.TryGetValue(cityData.title_id, out var cities))
            {
                cities = new List<long>();
                titleCities[cityData.title_id] = cities;
            }

            cities.Add(cityData.id);
        }

        foreach (var pair in titleCities)
        {
            long capitalId = pair.Value.FirstOrDefault();
            cityById.TryGetValue(capitalId, out City capital);
            string name = capital?.SelectKingdomName();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = capital?.kingdom?.GetKingdomName() ?? capital?.GetCityName() ?? "";
            }

            result.Add(new KingdomTitleData
            {
                id = pair.Key,
                cities = pair.Value.Distinct().ToList(),
                title_capital = capitalId,
                province_name = capital?.GetCityName(),
                name = name,
                original_actor_asset = capital?.kingdom?.king?.asset?.id ?? capital?.kingdom?.asset?.id ?? capital?.getSpecies(),
                banner_background_id = capital?.kingdom?.data?.banner_background_id ?? 0,
                banner_icon_id = capital?.kingdom?.data?.banner_icon_id ?? 0,
                founder_kingdom_id = capital?.kingdom?.id ?? -1L,
                founder_kingdom_name = capital?.kingdom?.name
            });
        }

        return result;
    }

}
