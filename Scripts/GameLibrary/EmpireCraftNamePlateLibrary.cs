using db;
using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.Regimes.TemporaryFactions;
using NeoModLoader.api.attributes;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.GameLibrary;
public static class EmpireCraftNamePlateLibrary
{
    public static string additionNum => ModClass.REAL_NUM_SWITCH ? "k" : "";
    private static double _last_plate_update_ts = -1L;
    private static UnityEngine.Vector3 _last_cam_pos;
    private static float _last_cam_size = -1f;
    private static readonly Dictionary<string, Sprite> _sliced_sprite_cache = new Dictionary<string, Sprite>(256);
    private static readonly Dictionary<int, string> _text_cache = new Dictionary<int, string>(512);
    private static readonly Dictionary<int, Vector3> _text_position_cache = new Dictionary<int, Vector3>(512);
    private static readonly Dictionary<long, Vector3> _empire_position_cache = new Dictionary<long, Vector3>(128);
    private static readonly Dictionary<long, float> _empire_position_cache_time = new Dictionary<long, float>(128);
    private static readonly List<Empire> _cached_empires = new List<Empire>();
    private static readonly List<Kingdom> _cached_kingdoms = new List<Kingdom>();
    private static readonly List<Kingdom> _cached_kingdoms_no_back = new List<Kingdom>();
    private static readonly List<City> _cached_cities = new List<City>();
    private static readonly List<City> _cached_cities_no_title = new List<City>();
    private static readonly List<City> _cached_neutral_cities = new List<City>();
    private static readonly List<KingdomTitle> _cached_titles = new List<KingdomTitle>();
    private static readonly List<EmpireCore> _cached_empire_cores = new List<EmpireCore>();
    private static readonly List<Kingdom> _render_kingdoms_buffer = new List<Kingdom>();
    private static readonly List<Kingdom> _render_kingdoms_no_back_buffer = new List<Kingdom>();
    private static readonly HashSet<long> _visible_core_ids = new HashSet<long>();
    private static readonly HashSet<long> _visible_title_ids = new HashSet<long>();
    private static readonly Queue<City> _empire_city_queue = new Queue<City>();
    private static readonly HashSet<long> _empire_city_visited = new HashSet<long>();
    private static readonly HashSet<long> _empire_city_members = new HashSet<long>();
    private static readonly List<City> _empire_component_cities = new List<City>();
    private static readonly List<City> _empire_best_component_cities = new List<City>();
    private static bool _is_camera_moving_this_frame;
    private static Empire GetDisplayedEmpireForKingdom(Kingdom kingdom)
    {
        if (kingdom == null) return null;
        if (kingdom.IsInEmpire()) return kingdom.GetEmpire();
        if (kingdom.HasTakenAlliance()) return kingdom.GetTakenAllianceEmpire();
        return null;
    }
    private static bool IsCameraMoving()
    {
        return _is_camera_moving_this_frame;
    }
    private static bool _shouldThrottle()
    {
        var cam = MoveCamera.instance?.main_camera;
        if (cam == null) return false;
        if (Time.timeScale == 0f) return false;
        var now = World.world.getCurWorldTime();
        var pos = cam.transform.position;
        var moved = UnityEngine.Vector3.Distance(pos, _last_cam_pos);
        float zoomChanged = _last_cam_size < 0f ? 0f : Mathf.Abs(cam.orthographicSize - _last_cam_size);
        _is_camera_moving_this_frame = moved > 0.01f || zoomChanged > 0.01f;
        var heavy = (World.world.kingdoms.Count > 120) || (World.world.cities.Count > 250);
        if (_is_camera_moving_this_frame)
        {
            _last_cam_pos = pos;
            _last_cam_size = cam.orthographicSize;
            _last_plate_update_ts = now;
            return false;
        }
        if (_last_plate_update_ts > 0)
        {
            var interval = heavy ? 0.35 : 0.2;
            if (now - _last_plate_update_ts < interval && moved < 0.5) return true;
        }
        _last_cam_pos = pos;
        _last_cam_size = cam.orthographicSize;
        _last_plate_update_ts = now;
        return false;
    }
    public static void init()
    {
        NameplateAsset asset = new NameplateAsset
        {
            id = "plate_empire",
            path_sprite = "ui/nameplates/nameplate_empire",
            map_mode = MetaTypeExtension.Empire,
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                bool throttle = _shouldThrottle();
                if (throttle)
                {
                    _cached_empires.Sort((a, b) => (a?.countWarriors() ?? 0).CompareTo(b?.countWarriors() ?? 0));
                    for (int i = 0; i < _cached_empires.Count; i++)
                    {
                        var empire = _cached_empires[i];
                        if (empire == null || empire.IsArchived() || empire.CoreKingdom == null) continue;
                        Vector3 empirePos = GetEmpireDisplayPosition(empire);
                        if (!isWithinCamera(empirePos)) continue;
                        var npt = prepareNext(pManager, pAsset, empire, 37, 12, 39, 11);
                        npt._showing = true;
                        npt.setPriority(9999999 + empire.CountPopulation());
                        showTextEmpire(npt, empire.CoreKingdom);
                    }
                }
                else
                {
                    _cached_empires.Clear();
                    foreach (var empire in ModClass.EMPIRE_MANAGER)
                    {
                        if (empire == null || empire.IsArchived() || empire.CoreKingdom == null) continue;
                        _cached_empires.Add(empire);
                    }
                    _cached_empires.Sort((a, b) => (a?.countWarriors() ?? 0).CompareTo(b?.countWarriors() ?? 0));
                    for (int i = 0; i < _cached_empires.Count; i++)
                    {
                        var empire = _cached_empires[i];
                        if (empire == null || empire.IsArchived() || empire.CoreKingdom == null) continue;
                        var npt = prepareNext(pManager, pAsset, empire, 37, 12, 39, 11);
                        npt._showing = true;
                        npt.setPriority(9999999 + empire.CountPopulation());
                        showTextEmpire(npt, empire.CoreKingdom);
                    }
                }

                _render_kingdoms_no_back_buffer.Clear();
                _render_kingdoms_buffer.Clear();
                switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
                {
                    case 0:
                        if (throttle)
                        {
                            int budget = 128;
                            _cached_kingdoms_no_back.Clear();
                            _cached_kingdoms.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && (kingdom.IsInEmpire() || kingdom.HasTakenAlliance()) && isWithinCamera(kingdom.capital.city_center))
                                {

                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (kingdom.hasCapital() && !kingdom.IsInEmpire() && !kingdom.HasTakenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms.Add(kingdom);
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count && _render_kingdoms_no_back_buffer.Count < budget; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || k.isRekt() || !k.hasCapital()) continue;
                                _render_kingdoms_no_back_buffer.Add(k);
                            }
                            for (int i = 0; i < _cached_kingdoms.Count && _render_kingdoms_buffer.Count + _render_kingdoms_no_back_buffer.Count < budget; i++)
                            {
                                var k = _cached_kingdoms[i];
                                if (k == null || k.isRekt() || !k.hasCapital()) continue;
                                _render_kingdoms_buffer.Add(k);
                            }
                        }
                        else
                        {
                            _cached_kingdoms_no_back.Clear();
                            _cached_kingdoms.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && (kingdom.IsInEmpire() || kingdom.HasTakenAlliance()) && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (kingdom.hasCapital() && !kingdom.IsInEmpire() && !kingdom.HasTakenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms.Add(kingdom);
                                }
                            }
                            _render_kingdoms_no_back_buffer.AddRange(_cached_kingdoms_no_back);
                            _render_kingdoms_buffer.AddRange(_cached_kingdoms);
                        }
                        break;
                    case 1:
                        if (throttle)
                        {
                            _cached_kingdoms_no_back.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasTakenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count && _render_kingdoms_no_back_buffer.Count < 128; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || k.isRekt() || !k.hasCapital()) continue;
                                _render_kingdoms_no_back_buffer.Add(k);
                            }
                        }
                        else
                        {
                            _cached_kingdoms_no_back.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasTakenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            _render_kingdoms_no_back_buffer.AddRange(_cached_kingdoms_no_back);
                        }
                        break;
                    case 2:
                        if (throttle)
                        {
                            _cached_kingdoms_no_back.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasGivenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count && _render_kingdoms_no_back_buffer.Count < 128; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || k.isRekt() || !k.hasCapital()) continue;
                                _render_kingdoms_no_back_buffer.Add(k);
                            }
                        }
                        else
                        {
                            _cached_kingdoms_no_back.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasGivenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                }
                            }
                            _render_kingdoms_no_back_buffer.AddRange(_cached_kingdoms_no_back);
                        }
                        break;
                }

                for (int i = 0; i < _render_kingdoms_no_back_buffer.Count; i++)
                {
                    Kingdom kingdom = _render_kingdoms_no_back_buffer[i];
                    if (kingdom == null || kingdom.isRekt() || !kingdom.hasCapital()) continue;
                    if (kingdom.IsInEmpire())
                    {
                        WorldTile mouseTilePosCachedFrame = World.world.getMouseTilePosCachedFrame();
                        if ((!mouseTilePosCachedFrame?.zone_city?.kingdom?.GetEmpire()?.kingdoms_list?.Contains(kingdom)) ??
                            true)
                        {
                            continue;
                        }
                    }
                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                    nameplateText._showing = true;
                    nameplateText.setPriority(kingdom.getPopulationPeople());
                    showTextKingdomNoBack(nameplateText, kingdom);
                }

                for (int i = 0; i < _render_kingdoms_buffer.Count; i++)
                {
                    Kingdom kingdom = _render_kingdoms_buffer[i];
                    if (kingdom == null || kingdom.isRekt() || !kingdom.hasCapital()) continue;
                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                    nameplateText._showing = true;
                    nameplateText.setPriority(kingdom.getPopulationPeople());
                    showTextKingdom(nameplateText, kingdom);
                }
            }
        };
        AssetManager.nameplates_library.add(asset);

        NameplateAsset asset2 = new NameplateAsset
        {
            id = "plate_kingdomTitle",
            map_mode = MetaTypeExtension.KingdomTitle,
            path_sprite = "ui/nameplates/nameplate_kingdomTitle",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            action_main = delegate (NameplateManager pManager, NameplateAsset pAsset)
            {
                bool throttle = _shouldThrottle();
                int zoneOptionState = EmpireCraftMetaTypeLibrary.kingdomTitle.getZoneOptionState();
                if (throttle)
                {
                    if (zoneOptionState == 0)
                    {
                        for (int i = 0; i < _cached_empire_cores.Count; i++)
                        {
                            var core = _cached_empire_cores[i];
                            var repCity = EmpireCoreManager.GetRepresentativeCity(core);
                            if (core == null || repCity == null || repCity.isRekt()) continue;
                            if (!isWithinCamera(repCity.city_center)) continue;
                            var npt = prepareNext(pManager, pAsset, repCity, 37, 12, 39, 11);
                            showTextEmpireCore(npt, core);
                        }
                    }
                    for (int i = 0; i < _cached_titles.Count; i++)
                    {
                        var t = _cached_titles[i];
                        if (t == null || t.isRekt() || t.title_capital == null || t.title_capital.isRekt()) continue;
                        if (!isWithinCamera(t.title_capital.city_center)) continue;
                        var npt = prepareNext(pManager, pAsset, t.title_capital, 37, 12, 39, 11);
                        showTextTitle(npt, t.title_capital);
                    }
                    for (int i = 0; i < _cached_cities_no_title.Count; i++)
                    {
                        var c = _cached_cities_no_title[i];
                        if (c == null || c.isRekt()) continue;
                        if (!isWithinCamera(c.city_center)) continue;
                        var npt = pManager.prepareNext(AssetManager.nameplates_library._plate_city, c);
                        showTextCity(npt, c, c.city_center);
                    }
                }
                else
                {
                    _cached_empire_cores.Clear();
                    _cached_titles.Clear();
                    _cached_cities_no_title.Clear();
                    _visible_core_ids.Clear();
                    _visible_title_ids.Clear();
                    foreach (City city in World.world.cities)
                    {
                        if (city == null || city.isRekt()) continue;
                        if (!isWithinCamera(city.city_center)) continue;

                        KingdomTitle kingdomTitle = city.GetTitle();
                        EmpireCore core = city.GetEmpireCore();
                        if (zoneOptionState == 0 && core != null && kingdomTitle != null && EmpireCoreManager.ContainsTitle(core, kingdomTitle))
                        {
                            if (_visible_core_ids.Add(core.id))
                            {
                                _cached_empire_cores.Add(core);
                                NameplateText npt = prepareNext(pManager, pAsset, city, 37, 12, 39, 11);
                                showTextEmpireCore(npt, core);
                            }
                            continue;
                        }

                        if (kingdomTitle != null && !kingdomTitle.isRekt())
                        {
                            if (_visible_title_ids.Add(kingdomTitle.id))
                            {
                                _cached_titles.Add(kingdomTitle);
                                NameplateText npt = prepareNext(pManager, pAsset, city, 37, 12, 39, 11);
                                showTextTitle(npt, kingdomTitle.title_capital);
                            }
                        }
                        else
                        {
                            _cached_cities_no_title.Add(city);
                            NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, city);
                            showTextCity(nameplateText, city, city.city_center);
                        }
                    }
                    _visible_core_ids.Clear();
                    _visible_title_ids.Clear();
                }
            },
        };
        AssetManager.nameplates_library.add(asset2);
        
        NameplateAsset asset5 = new NameplateAsset
        {
            id = "plate_city",
            path_sprite = "ui/nameplates/nameplate_city",
            map_mode = MetaType.City,
            padding_left = 6,
            padding_right = 7,
            padding_top = -2,
            action_main = delegate(NameplateManager pManager, NameplateAsset _)
            {
                int num = 0;
                bool throttle = _shouldThrottle();
                if (throttle)
                {
                    for (int i = 0; i < _cached_cities.Count && num < _.max_nameplate_count; i++)
                    {
                        var current = _cached_cities[i];
                        if (current == null || current.isRekt()) continue;
                        if (isWithinCamera(current.city_center))
                        {
                            NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, current);
                            showTextCity(nameplateText, current, current.city_center);
                            num++;
                        }
                    }
                }
                else
                {
                    using ListPool<City> listPool = new ListPool<City>(World.world.cities.list);
                    listPool.Sort(sortByMembers);
                    _cached_cities.Clear();
                    if (MetaTypeLibrary.city.getZoneOptionState() == 0)
                    {
                        foreach (ref City item in listPool)
                        {
                            City current = item;
                            if (num >= _.max_nameplate_count)
                            {
                                break;
                            }
                            if (isWithinCamera(current.city_center))
                            {
                                _cached_cities.Add(current);
                                NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, current);
                                showTextCity(nameplateText, current, current.city_center);
                                num++;
                            }
                        }
                    }
                    else
                    {
                        foreach (City city in World.world.cities)
                        {
                            if (num >= _.max_nameplate_count)
                            {
                                break;
                            }
                            Actor pForceActor = null;
                            if (city.hasLeader() && !city.leader.isRekt() && city.leader.is_visible)
                            {
                                pForceActor = city.leader;
                            }
                            if (getPositionForMeta(city, out var pPosition, pForceActor))
                            {
                                _cached_cities.Add(city);
                                var npt = pManager.prepareNext(_, city);
                                showTextCity(npt, city, pPosition);
                                num++;
                            }
                        }
                    }
                }
            }
        };
        AssetManager.nameplates_library.dict.Remove("City");
        AssetManager.nameplates_library.map_modes_nameplates[asset5.map_mode] = asset5;
        AssetManager.nameplates_library.dict["City"] = asset5;
        
        
        NameplateAsset asset6 = new NameplateAsset
        {
            id = "plate_kingdom",
            path_sprite = "ui/nameplates/nameplate_kingdom",
            padding_left = 26,
            padding_right = 26,
            padding_top = -2,
            map_mode = MetaType.Kingdom,
            action_main = delegate(NameplateManager pManager, NameplateAsset pAsset)
            {
                bool throttle = _shouldThrottle();
                if (throttle)
                {
                    int budget = 128;
                    int processed = 0;
                    for (int i = 0; i < _cached_kingdoms.Count; i++)
                    {
                        var kingdom = _cached_kingdoms[i];
                        if (kingdom == null || kingdom.isRekt() || !kingdom.hasCapital()) continue;
                        if (!isWithinCamera(kingdom.capital.city_center)) continue;
                        NameplateText nameplateText = pManager.prepareNext(pAsset, kingdom);
                        showTextKingdom(nameplateText, kingdom);
                        if (++processed >= budget) break;
                    }
                    for (int i = 0; i < _cached_neutral_cities.Count; i++)
                    {
                        var city = _cached_neutral_cities[i];
                        if (city == null || city.isRekt()) continue;
                        if (!isWithinCamera(city.city_center)) continue;
                        var npt = pManager.prepareNext(AssetManager.nameplates_library._plate_city, city);
                        showTextCity(npt, city, city.city_center);
                        if (++processed >= budget) break;
                    }
                }
                else
                {
                    _cached_kingdoms.Clear();
                    _cached_neutral_cities.Clear();
                    foreach (Kingdom kingdom in World.world.kingdoms)
                    {
                        if (kingdom.hasCapital() && isWithinCamera(kingdom.capital.city_center))
                        {
                            _cached_kingdoms.Add(kingdom);
                            NameplateText  nameplateText = pManager.prepareNext(pAsset, kingdom);
                            showTextKingdom(nameplateText, kingdom);
                        }
                    }
                    if (WildKingdomsManager.neutral.cities.Count > 0)
                    {
                        foreach (City city in WildKingdomsManager.neutral.cities)
                        {
                            _cached_neutral_cities.Add(city);
                            var npt = pManager.prepareNext(AssetManager.nameplates_library._plate_city, city);
                            showTextCity(npt, city, city.city_center);
                        }
                    }
                }
            }
        };
        AssetManager.nameplates_library.dict.Remove("Kingdom");
        AssetManager.nameplates_library.map_modes_nameplates[asset6.map_mode] = asset6;
        AssetManager.nameplates_library.dict["Kingdom"] = asset6;
    }
    public static void showTextKingdom(NameplateText npt, Kingdom pMetaObject)
    {
        if (pMetaObject?.data == null) return;
        if (pMetaObject.data.banner_background_id < 0) pMetaObject.data.banner_background_id = 0;
        if (pMetaObject.data.banner_icon_id < 0) pMetaObject.data.banner_icon_id = 0;
        npt.setupMeta((MetaObjectData) pMetaObject.data, pMetaObject.getColor());
        string pNewText = $"{pMetaObject.name}  {pMetaObject.getPopulationPeople().ToString()+additionNum}";
        int num;
        if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
        {
            string[] strArray = new string[5]
            {
                pNewText,
                " | ",
                null,
                null,
                null
            };
            num = pMetaObject.countTotalWarriors();
            strArray[2] = num.ToString();
            strArray[3] = "/";
            num = pMetaObject.countWarriorsMax();
            strArray[4] = num.ToString();
            pNewText = string.Concat(strArray);
        }
        if (DebugConfig.isOn(DebugOption.ShowCityWeaponsText))
        {
            string str1 = pNewText;
            num = pMetaObject.countWeapons();
            string str2 = num.ToString();
            pNewText = $"{str1} | w{str2}";
        }
        setTextIfChanged(npt, pNewText, (Vector3)pMetaObject.capital.city_center);
        // 给文字加蓝色边框（描边）
        var outline = npt._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
        npt.priority_population = pMetaObject.units.Count;
        npt.showSpecies(pMetaObject.getSpriteIcon());
        npt._text_name.supportRichText = true;
        npt._show_banner_kingdom = true;
        npt._banner_kingdoms.enabled = true;
        npt._banner_kingdoms.load((NanoObject) pMetaObject);
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        npt.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        Clan kingClan = pMetaObject.getKingClan();
        if (kingClan != null)
        {
            npt._show_banner_clan = true;
            npt._banner_clan.enabled = true;
            npt._banner_clan.load((NanoObject) kingClan);
        }
        npt.transform.SetAsFirstSibling();
        npt.nano_object = (NanoObject) pMetaObject;
    }	
    public static void showTextKingdomNoBack(NameplateText npt, Kingdom pMetaObject)
    {
        var displayedEmpire = GetDisplayedEmpireForKingdom(pMetaObject);
        var displayColor = pMetaObject.HasTakenAlliance() && displayedEmpire?.CoreKingdom != null
            ? displayedEmpire.CoreKingdom.getColor()
            : pMetaObject.getColor();
        npt.setupMeta(pMetaObject.data, displayColor);
        string pNewText = $"{pMetaObject.name} {pMetaObject.getPopulationPeople().ToString()+additionNum} | {pMetaObject.countTotalWarriors()}/{pMetaObject.countWarriorsMax()}";
        if (pMetaObject.HasTakenAlliance() && displayedEmpire != null)
        {
            pNewText += $"\n{LM.Get("label_tributary_target")}: {displayedEmpire.GetEmpireName()}";
        }
        switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
        {
            case 0:
                if (pMetaObject.IsInEmpire())
                {
                    WorldTile mouseTilePosCachedFrame = World.world.getMouseTilePosCachedFrame();
                    if (mouseTilePosCachedFrame?.zone_city?.kingdom==pMetaObject)
                    {
                        var corruption = (int)(pMetaObject.GetCorruptionRate()*100);
                        pNewText += "\n" + (pMetaObject.GetHighestFactionRatio() == null
                            ? LM.Get("label_no_faction_influence")
                            : pMetaObject.FactionRatioToString());
                        if (pMetaObject.hasKing())
                        {
                            pNewText +=
                                $"\n{LM.Get("label_governor_influence")}: {pMetaObject.king.renown}";
                            if (pMetaObject.king.HasOfficeIdentity())
                            {
                                var officeIdentity = pMetaObject.king.GetIdentity();
                                if (officeIdentity != null)
                                {
                                    var level = officeIdentity.ViolateLevel;
                                    var levelText = level.ToString();
                                    pNewText += $" | {LM.Get("label_brutality")}: {(
                                        level>30?
                                            level>70?
                                                levelText.ColorString(pColor:new Color(1, 0,0)):
                                                levelText.ColorString(pColor:new Color(0.8f, 0.8f, 0)):
                                            levelText.ColorString(pColor:new Color(0,1,0)))}%";
                                }
                            
                            }
                        }
                        pNewText +=
                            $"\n{LM.Get("label_corruption")}:{corruption.ToString().ColorString(pColor: corruption <= 30 ? Color.green : Color.red)}%";
                    }
                }
                break;
            case 1:
                if (pMetaObject.HasTakenAlliance())
                {
                    pNewText += $"\n朝贡金额{pMetaObject.countUnits()/2} | 退出朝贡倾向:{pMetaObject.GetLeaveTakenAlliancePreference() * 100}%";
                }
                break;
            case 2:
                break;
        }
        setTextIfChanged(npt, pNewText, pMetaObject.capital.city_center);
        npt._background_image.enabled = false;
        npt.priority_population = pMetaObject.units.Count;
        npt.showSpecies(pMetaObject.getSpriteIcon());
        npt._show_banner_kingdom = false;
        npt._banner_kingdoms.enabled = false;
        npt._show_banner_clan = false;
        npt._banner_clan.enabled = false;
        npt._text_name.supportRichText = true;
        if (pMetaObject.IsInEmpire() || pMetaObject.HasTakenAlliance())
        {
            npt._text_name.color = Color.white;
        }
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        npt.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        // 给文字加蓝色边框（描边）
        var outline = npt._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
        npt.transform.SetAsFirstSibling();
        npt.nano_object = pMetaObject;
    }	
    public static bool getPositionForMeta(IMetaObject pMetaObject, out Vector3 pPosition, Actor pForceActor = null)
    {
        if (!pMetaObject.isAlive() || !pMetaObject.hasUnits())
        {
            pPosition = Vector3.zero;
            return false;
        }
        var actor = pForceActor ?? pMetaObject.getOldestVisibleUnitForNameplatesCached();
        if (actor == null)
        {
            pPosition = Vector3.zero;
            return false;
        }
        Vector3 vector = actor.current_position;
        vector.y += actor.getHeight();
        vector.y += -2f;
        pPosition = vector;
        return true;
    }
    public static void showTextCity(NameplateText npt, City pMetaObject, Vector2 pPosition)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.kingdom.getColor());
        if (pMetaObject.isCapitalCity())
        {
            npt.setNameplateSprite("ui/nameplates/nameplate_city_capital");
        }
        else
        {
            npt.setNameplateSprite("ui/nameplates/nameplate_city");
        }
        int populationPeople = pMetaObject.getPopulationPeople();
        string text = npt.getStringForNameplate(pMetaObject.name, populationPeople) + additionNum;
        if (pMetaObject?.kingdom?.IsInEmpire()??false)
        {
            var corruption = (int)(pMetaObject.GetCorruptionRate()*100);
            text +=
                $" | {LM.Get("label_corruption")}:{corruption.ToString().ColorString(pColor: corruption <= 30 ? Color.green : Color.red)}%";
        }
        if (npt.is_full)
        {
            if (DebugConfig.isOn(DebugOption.ShowWarriorsCityText))
            {
                text = text + " | " + pMetaObject.countWarriors() + "/" + pMetaObject.getMaxWarriors();
                if (Config.isEditor)
                {
                    string text2 = "  :  " + (int)(pMetaObject.getArmyMaxMultiplier() * 100f) + "%";
                    text += text2;
                }
            }
            if (DebugConfig.isOn(DebugOption.ShowCityWeaponsText))
            {
                text = text + " | w" + pMetaObject.countWeapons();
            }
            if (DebugConfig.isOn(DebugOption.ShowFoodCityText))
            {
                text = text + " | F" + pMetaObject.getTotalFood();
            }
        }
        setTextIfChanged(npt, text, pPosition);
        Subspecies mainSubspecies = null;
        try
        {
            mainSubspecies = pMetaObject.getMainSubspecies();
        }
        catch
        {
        }

        if (mainSubspecies != null)
        {
            npt.showSpecies(mainSubspecies.getActorAsset().getSpriteIcon());
        }
        if (pMetaObject.last_visual_capture_ticks != 0)
        {
            npt._show_capture_counter = true;
            npt._active_check_dirty = true;
            if (pMetaObject.being_captured_by != null && pMetaObject.being_captured_by.isAlive())
            {
                npt._conquer_text.color = pMetaObject.being_captured_by.getColor().getColorText();
            }
            npt._conquer_text.text = pMetaObject.last_visual_capture_ticks + "%";
        }
        else
        {
            npt._show_capture_counter = false;
            npt._active_check_dirty = true;
        }
        if (npt._show_capture_counter)
        {
            Vector2 anchoredPosition = ((!npt.is_full) ? new Vector2(3f, -25f) : new Vector2(0f, -1f));
            npt._container_capture.anchoredPosition = anchoredPosition;
        }
        // 给文字加蓝色边框（描边）
        var outline = npt._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        npt.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        npt._text_name.supportRichText = true;
        npt._show_banner_city = true;
        npt._banner_city.enabled = true;
        npt._banner_city.load(pMetaObject);
        npt.priority_capital = pMetaObject.isCapitalCity();
        npt.setPriority(populationPeople);
    }

    public static bool isWithinCamera(Vector2 pVector)
    {
        return World.world.move_camera.isWithinCameraViewNotPowerBar(pVector);
    }
    public static NameplateText prepareNext(NameplateManager __instance, NameplateAsset pAsset, NanoObject pMeta, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        NameplateText nameplateText;
        if (__instance._active.Count > __instance._next_index)
        {
            nameplateText = __instance._active[__instance._next_index];
        }
        else
        {
            nameplateText = __instance._pool.Count != 0 ? __instance._pool.Pop() : __instance.createNew();
            __instance._active.Add(nameplateText);
        }
        var key = pAsset.path_sprite + "|" + left + "|" + bottom + "|" + right + "|" + top;
        if (!_sliced_sprite_cache.TryGetValue(key, out var sliced))
        {
            Sprite sprite = SpriteTextureLoader.getSprite(pAsset.path_sprite);
            var text = sprite.texture;
            var rect = sprite.rect;
            var pivot = sprite.pivot;
            float ppu = sprite.pixelsPerUnit;
            sliced = Sprite.Create(text, rect, pivot, ppu, 0, SpriteMeshType.FullRect, new Vector4(left, bottom, right, top));
            _sliced_sprite_cache[key] = sliced;
        }
        var img = nameplateText._background_image;
        img.sprite = sliced;
        img.type = Image.Type.Sliced;
        nameplateText.layout_group.padding.left = pAsset.padding_left;
        nameplateText.layout_group.padding.right = pAsset.padding_right;
        nameplateText.layout_group.padding.top = pAsset.padding_top;
        nameplateText._text_name.supportRichText = true;
        __instance._next_index++;
        prepare(nameplateText, pAsset, pMeta, __instance._tween_scale, __instance._nameplate_mode, __instance._nano_object_set, __instance._selected_nano_object);
        img.sprite = sliced;
        img.type = Image.Type.Sliced;
        
        
        
        return nameplateText;
    }
    public static void prepare(NameplateText nameplateText, NameplateAsset pAsset, NanoObject pMeta, float pGlobalScale, NameplateRenderingType pNameplateMode, bool pNanoObjectSet, NanoObject pSelectedNanoObject)
    {
        if (pNanoObjectSet)
        {
            pNameplateMode = ((pSelectedNanoObject == pMeta) ? NameplateRenderingType.Full : NameplateRenderingType.BannerOnly);
        }
        ResetReusableNameplateVisuals(nameplateText);
        if (pNameplateMode != nameplateText._last_mode)
        {
            nameplateText.clearCaches();
            nameplateText._active_check_dirty = true;
            nameplateText._last_mode = pNameplateMode;
        }
        ApplyReusableNameplateMode(nameplateText, pAsset, pMeta);
        nameplateText.updateScale(pMeta, pGlobalScale, pNanoObjectSet, pSelectedNanoObject);
        nameplateText.resetElements();
        nameplateText.setShowing(pVal: true);
        nameplateText.setAssetAndMeta(pAsset, pMeta);
        if (((IFavoriteable)pMeta).isFavorite())
        {
            nameplateText.showFavoriteIcon();
        }
        else
        {
            nameplateText._show_icon_favorite = false;
        }
        nameplateText.checkSetActive(nameplateText._icon_favorite, nameplateText._show_icon_favorite);
        if (pAsset != null)
        {
            switch (pAsset.map_mode)
            {
                case MetaTypeExtension.KingdomTitle:
                    City capital = pMeta as City;
                    if (capital == null)
                    {
                        capital = (pMeta as KingdomTitle)?.title_capital;
                    }
                    KingdomTitle title = capital?.GetTitle();
                    if (capital == null || capital.isRekt() || title == null || title.isRekt()) break;
                    if (title.data.banner_background_id < 0) title.data.banner_background_id = 0;
                    if (title.data.banner_icon_id < 0) title.data.banner_icon_id = 0;

                    nameplateText.setupMeta(capital.data, title.getColor());
                    nameplateText._text_name.fontStyle = FontStyle.Bold;
                    nameplateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
                    nameplateText._text_name.transform.localScale = Vector3.one * 1.5f;
                    nameplateText._text_name.color = Color.white;
                    
                    nameplateText._banner_kingdoms.enabled = true;
                    nameplateText._banner_kingdoms.gameObject.SetActive(true);
                    nameplateText._banner_kingdoms.background.sprite = title.getElementBackground();
                    nameplateText._banner_kingdoms.icon.sprite = title.getElementIcon();
                    var color = title.kingdomColor.getColorBanner();
                    color = new Color(color.r, color.g, color.b, 0.5f);
                    nameplateText._banner_kingdoms.background.color = color;
                    nameplateText._banner_kingdoms.icon.color = color;
                    nameplateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
                    nameplateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one * 1.5f;
                    
                    nameplateText._banner_city.enabled = false;
                    nameplateText._banner_city.gameObject.SetActive(false);
                    
                    nameplateText._banner_clan.enabled = false;
                    nameplateText._banner_clan.gameObject.SetActive(false);
                    break;
                case MetaTypeExtension.Empire:
                    if (!EmpireCraftWorldLawLibrary.empirecraft_law_simplify_nameplates.isEnabled())
                    {
                        nameplateText._show_banner_city = false;
                        nameplateText._show_banner_clan = false;
                        nameplateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
                        nameplateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
                        nameplateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
                        nameplateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
                        nameplateText._show_banner_kingdom = false;
                        nameplateText._show_banner_culture = false;
                        nameplateText._background_image.enabled = true;
                        
                        nameplateText._banner_kingdoms.enabled = false;
                        nameplateText._banner_kingdoms.gameObject.SetActive(false);
                        
                        nameplateText._banner_city.enabled = false;
                        nameplateText._banner_city.gameObject.SetActive(false);
                    
                        nameplateText._banner_clan.enabled = false;
                        nameplateText._banner_clan.gameObject.SetActive(false);
                    }
                    break;
            }
        }
    }
    private static void ResetReusableNameplateVisuals(NameplateText nameplateText)
    {
        if (nameplateText == null)
        {
            return;
        }

        nameplateText._text_name.supportRichText = true;
        nameplateText._text_name.fontStyle = FontStyle.Normal;
        nameplateText._text_name.color = Color.white;
        nameplateText._text_name.transform.localPosition = Vector3.zero;
        nameplateText._text_name.transform.localScale = Vector3.one;

        var outline = nameplateText._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }

        nameplateText._background_image.transform.localPosition = Vector3.zero;
        nameplateText._background_image.transform.localScale = Vector3.one;
        nameplateText._background_image.enabled = true;

        if (nameplateText._banner_kingdoms != null)
        {
            nameplateText._banner_kingdoms.enabled = true;
            nameplateText._banner_kingdoms.gameObject.SetActive(true);
            nameplateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
            nameplateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one;
        }
        if (nameplateText._banner_city != null)
        {
            nameplateText._banner_city.enabled = true;
            nameplateText._banner_city.gameObject.SetActive(true);
            nameplateText._banner_city.gameObject.transform.localPosition = Vector3.zero;
            nameplateText._banner_city.gameObject.transform.localScale = Vector3.one;
        }
        if (nameplateText._banner_clan != null)
        {
            nameplateText._banner_clan.enabled = true;
            nameplateText._banner_clan.gameObject.SetActive(true);
            nameplateText._banner_clan.gameObject.transform.localPosition = Vector3.zero;
            nameplateText._banner_clan.gameObject.transform.localScale = Vector3.one;
        }
    }

    private static void ApplyReusableNameplateMode(NameplateText nameplateText, NameplateAsset pAsset, NanoObject pMeta)
    {
        if (nameplateText == null)
        {
            return;
        }

        var outline = nameplateText._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }

        switch (nameplateText._last_mode)
        {
            case NameplateRenderingType.Full:
                nameplateText._background_image.transform.localScale = Vector3.one;
                nameplateText._background_image.enabled = true;
                nameplateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one;
                nameplateText._text_name.fontStyle = FontStyle.Normal;
                nameplateText._text_name.transform.localScale = Vector3.one;
                break;
            case NameplateRenderingType.BannerOnly:
                if (pMeta != null && pMeta.meta_type == MetaTypeExtension.KingdomTitle)
                {
                    nameplateText._background_image.transform.localScale = Vector3.one;
                    nameplateText._background_image.enabled = false;
                    nameplateText._banner_kingdoms.enabled = false;
                }
                else
                {
                    nameplateText._text_name.fontStyle = FontStyle.Normal;
                    nameplateText._text_name.transform.localScale = Vector3.one;
                    float scale = pAsset != null ? pAsset.banner_only_mode_scale : 1f;
                    nameplateText._background_image.transform.localScale = new Vector3(scale, scale, 1f);
                    nameplateText._background_image.enabled = false;
                }
                break;
        }
    }
    public static int sortByMembers(City pObject1, City pObject2)
    {
        if (pObject1.isFavorite() && !pObject2.isFavorite())
        {
            return -1;
        }
        if (!pObject1.isFavorite() && pObject2.isFavorite())
        {
            return 1;
        }
        return pObject2.units.Count.CompareTo(pObject1.units.Count);
    }

    private static void setTextIfChanged(NameplateText npt, string text, Vector3 position)
    {
        if (npt == null)
        {
            return;
        }

        if (IsCameraMoving())
        {
            npt.setText(text, position);
            return;
        }

        int cacheKey = npt.GetInstanceID();
        if (_text_cache.TryGetValue(cacheKey, out string lastText)
            && _text_position_cache.TryGetValue(cacheKey, out Vector3 lastPosition)
            && lastText == text
            && (lastPosition - position).sqrMagnitude <= 0.0001f)
        {
            return;
        }

        npt.setText(text, position);
        _text_cache[cacheKey] = text;
        _text_position_cache[cacheKey] = position;
    }

    [Hotfixable]
    public static void showTextEmpire(NameplateText plateText, Kingdom pMetaObject)
    {
        if (pMetaObject == null) return;
        if (!pMetaObject.isAlive()) return;
        Empire empire = pMetaObject.GetEmpire();
        if (empire == null) return;
        plateText.setPriority(99999999);
        plateText._showing = true;
        plateText.setupMeta(pMetaObject.data, pMetaObject.getColor());
        string text = empire.data.name + "  " + empire.CountPopulation() + additionNum;
        text = text.ColorString(pColor: new Color(1, 1, 1));
        int difference = (empire.data.PreviousYearsMoney.Count > 2
            ? (empire.data.PreviousYearsMoney.Last() -
               empire.data.PreviousYearsMoney[empire.data.PreviousYearsMoney.Count - 2])
            : 0);
        string moneyText =
            $"\n{LM.Get("label_treasury")}:{empire.CoreKingdom.GetMoney()}" +
            $"({difference.ToString().ColorString(pColor:difference<0?Color.red : Color.green)})";
        switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
        {
            case 0:
                if (empire.IsAllowToMakeYearName())
                {
                    if (empire.HasYearName())
                    {
                        text = empire.data.name + "\u200A" + empire.GetYearNameWithTime() + "\u200A" +
                               empire.CountPopulation();
                    }
                }

                text = text + " | " + empire.countWarriors() + $"{additionNum}/" + empire.countWarriorsMax() + additionNum;
                if (!EmpireCraftWorldLawLibrary.empirecraft_law_simplify_nameplates.isEnabled())
                {
                    FixedFaction faction = empire.CoreKingdom.GetRegime().GetDominateFaction();
                    if (faction != null)
                    {
                        var tf = empire.RunningTemporaryFaction;
                        text =
                            $"\n{(empire.EmpireClan?.name ?? LM.Get("label_no_royal_clan")).ColorString(pColor: Color.yellow)} | {LM.Get("label_dominant_faction")}: {faction.Name}" +
                            moneyText + "\n"+ $"{LM.Get("label_mandate")}:{empire.Mandate}" + "\n" +
                            text +
                            $"\n{LM.Get("label_claim")}:{(tf!=null ? LM.Get(tf.type.ToString()) : LM.Get("label_none"))}".ColorString(
                                pColor: new Color(0.5f, 0.9f, 0.5f)) +
                            (tf!=null?tf.ShowAsPlot?$"({LM.Get("tf_starting")})"
                                : $"({(int)(tf.progress / tf.progressMax * 100)}/100)"
                                : "");
                    }
                }
                break;
            case 1:
                text += "\n朝贡同盟".ColorString(pColor:new Color(0.5f, 0.9f, 0.5f)) + moneyText;
                break;
            case 2:
                if (!empire.CoreKingdom.HasGivenAlliance())
                {
                    text += "\n岁币同盟".ColorString(pColor: new Color(0.9f, 0.2f, 0.8f)) + moneyText;
                } 
                break;

        }

        setTextIfChanged(plateText, text, GetEmpireDisplayPosition(empire));
        plateText._text_name.supportRichText = true;
        plateText._text_name.fontStyle = FontStyle.Bold;
        // 给文字加蓝色边框（描边）
        var outline = plateText._text_name.GetComponent<Outline>();
        if (outline == null)
        {
            outline = plateText._text_name.gameObject.AddComponent<Outline>();
        }
        else
        {
            outline.enabled = true;
        }

        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(2f, -2f);
        
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        plateText.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        plateText._background_image.enabled = EmpireCraftWorldLawLibrary.empirecraft_law_simplify_nameplates.isEnabled();
        plateText._text_name.color = Color.white;
        
        plateText.priority_population = pMetaObject.units.Count;
        plateText._show_icon_species = false;
        plateText._show_banner_kingdom = false;
        plateText._show_banner_clan = false;
        plateText.transform.SetAsLastSibling();
        plateText.nano_object = empire.CoreKingdom;
    }

    public static void showTextEmpireCore(NameplateText plateText, EmpireCore core)
    {
        if (core == null) return;
        City repCity = EmpireCoreManager.GetRepresentativeCity(core);
        if (repCity == null || repCity.isRekt()) return;

        plateText._showing = true;
        plateText._text_name.supportRichText = true;
        plateText._text_name.fontStyle = FontStyle.Bold;
        plateText._text_name.color = Color.white;
        plateText._text_name.transform.localPosition = Vector3.zero;
        plateText._text_name.transform.localScale = Vector3.one * 1.5f;
        setTextIfChanged(plateText, EmpireCoreManager.GetDisplayName(core), GetEmpireCoreDisplayPosition(core));

        var outline = plateText._text_name.GetComponent<Outline>();
        if (outline == null)
        {
            outline = plateText._text_name.gameObject.AddComponent<Outline>();
        }
        outline.enabled = false;

        Sprite sprite = SpriteTextureLoader.getSprite("ui/nameplates/nameplate_empire");
        plateText._background_image.sprite = sprite;
        plateText._background_image.type = Image.Type.Simple;
        plateText._background_image.enabled = true;
        plateText._background_image.transform.localPosition = Vector3.zero;
        plateText._background_image.transform.localScale = Vector3.one * 1.5f;
        plateText._show_banner_kingdom = false;
        plateText._show_banner_city = false;
        plateText._show_banner_clan = false;
        plateText.priority_population = EmpireCoreManager.GetCities(core).Count;
        plateText.transform.SetAsLastSibling();
        plateText.nano_object = repCity;
        plateText._banner_kingdoms.enabled = false;
        plateText._banner_kingdoms.gameObject.SetActive(false);
    }

    private static Vector3 GetEmpireDisplayPosition(Empire empire)
    {
        if (empire == null)
        {
            return new Vector2(99, 99);
        }
        long empireId = empire.id;
        float now = Time.unscaledTime;
        if (!IsCameraMoving()
            && _empire_position_cache.TryGetValue(empireId, out Vector3 cachedPosition)
            && _empire_position_cache_time.TryGetValue(empireId, out float cachedTime)
            && now - cachedTime <= 0.25f)
        {
            return cachedPosition;
        }

        List<City> cities = empire.AllCities();
        Vector3 fallback = empire.CoreKingdom?.capital?.city_center ?? new Vector2(99, 99);
        if (cities == null || cities.Count <= 0)
        {
            _empire_position_cache[empireId] = fallback;
            _empire_position_cache_time[empireId] = now;
            return fallback;
        }
        Vector3 result = GetDominantCitiesDisplayPosition(cities, fallback);
        _empire_position_cache[empireId] = result;
        _empire_position_cache_time[empireId] = now;
        return result;
    }

    private static Vector3 GetEmpireCoreDisplayPosition(EmpireCore core)
    {
        if (core == null) return new Vector2(99, 99);
        return EmpireCoreManager.GetRepresentativeCity(core)?.city_center ?? new Vector2(99, 99);
    }

    private static Vector3 GetDominantCitiesDisplayPosition(List<City> cities, Vector3 fallback)
    {
        if (cities == null || cities.Count <= 0)
        {
            return fallback;
        }

        _empire_city_visited.Clear();
        _empire_city_members.Clear();
        _empire_best_component_cities.Clear();
        int bestWeight = -1;
        for (int i = 0; i < cities.Count; i++)
        {
            City city = cities[i];
            if (city == null || city.isRekt()) continue;
            _empire_city_members.Add(city.getID());
        }

        for (int i = 0; i < cities.Count; i++)
        {
            City city = cities[i];
            if (city == null || city.isRekt()) continue;
            long cityId = city.getID();
            if (!_empire_city_visited.Add(cityId)) continue;

            _empire_component_cities.Clear();
            _empire_city_queue.Clear();
            _empire_city_queue.Enqueue(city);
            _empire_component_cities.Add(city);

            int componentWeight = 0;
            while (_empire_city_queue.Count > 0)
            {
                City current = _empire_city_queue.Dequeue();
                if (current == null || current.isRekt()) continue;

                componentWeight += current.zones?.Count ?? 0;
                if (current.neighbours_cities == null) continue;

                foreach (City neighbour in current.neighbours_cities)
                {
                    if (neighbour == null || neighbour.isRekt()) continue;
                    long neighbourId = neighbour.getID();
                    if (!_empire_city_members.Contains(neighbourId)) continue;
                    if (!_empire_city_visited.Add(neighbourId)) continue;
                    _empire_city_queue.Enqueue(neighbour);
                    _empire_component_cities.Add(neighbour);
                }
            }

            if (componentWeight > bestWeight)
            {
                bestWeight = componentWeight;
                _empire_best_component_cities.Clear();
                _empire_best_component_cities.AddRange(_empire_component_cities);
            }
        }

        if (_empire_best_component_cities.Count <= 0)
        {
            return fallback;
        }

        float avgX = 0f;
        float avgY = 0f;
        int zoneCount = 0;
        TileZone nearestZone = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < _empire_best_component_cities.Count; i++)
        {
            City city = _empire_best_component_cities[i];
            if (city == null || city.isRekt() || city.zones == null) continue;

            for (int j = 0; j < city.zones.Count; j++)
            {
                TileZone zone = city.zones[j];
                if (zone?.centerTile == null) continue;
                avgX += zone.centerTile.posV3.x;
                avgY += zone.centerTile.posV3.y;
                zoneCount++;
            }
        }

        if (zoneCount <= 0)
        {
            return _empire_best_component_cities[0]?.city_center ?? fallback;
        }

        avgX /= zoneCount;
        avgY /= zoneCount;

        for (int i = 0; i < _empire_best_component_cities.Count; i++)
        {
            City city = _empire_best_component_cities[i];
            if (city == null || city.isRekt() || city.zones == null) continue;

            for (int j = 0; j < city.zones.Count; j++)
            {
                TileZone zone = city.zones[j];
                if (zone?.centerTile == null) continue;
                float dist = Toolbox.SquaredDist(zone.centerTile.posV3.x, zone.centerTile.posV3.y, avgX, avgY);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestZone = zone;
                }
            }
        }

        if (nearestZone?.centerTile != null)
        {
            Vector3 center = nearestZone.centerTile.posV3;
            center.y += 2f;
            return center;
        }

        return _empire_best_component_cities[0]?.city_center ?? fallback;
    }

    public static void showTextTitle(NameplateText plateText, City capital)
    {
        if (ModClass.IS_CLEAR) return;
        if (capital == null) return;
        if (!capital.hasTitle()) return;
        try
        {
            plateText.nano_object = capital;
            string text = capital.GetTitle().data.name;
            setTextIfChanged(plateText, text, capital.city_center);
            plateText._background_image.enabled = false;
            plateText._show_banner_culture = false;
            plateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
            plateText._show_banner_city = false;
            plateText._show_banner_clan = false;
            plateText._show_banner_kingdom = true;
            plateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
            plateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one * 1.5f;
        }
        catch
        {
            // ignored
        }
    }
}
