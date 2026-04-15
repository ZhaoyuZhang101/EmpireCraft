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
    private static readonly Dictionary<string, Sprite> _sliced_sprite_cache = new Dictionary<string, Sprite>(256);
    private static readonly List<Empire> _cached_empires = new List<Empire>();
    private static readonly List<Kingdom> _cached_kingdoms = new List<Kingdom>();
    private static readonly List<Kingdom> _cached_kingdoms_no_back = new List<Kingdom>();
    private static readonly List<City> _cached_cities = new List<City>();
    private static readonly List<City> _cached_cities_no_title = new List<City>();
    private static readonly List<City> _cached_neutral_cities = new List<City>();
    private static readonly List<KingdomTitle> _cached_titles = new List<KingdomTitle>();
    private static bool _shouldThrottle()
    {
        var cam = MoveCamera.instance?.main_camera;
        if (cam == null) return false;
        if (Time.timeScale == 0f) return false;
        var now = World.world.getCurWorldTime();
        var pos = cam.transform.position;
        var moved = UnityEngine.Vector3.Distance(pos, _last_cam_pos);
        var heavy = (World.world.kingdoms.Count > 120) || (World.world.cities.Count > 250);
        if (_last_plate_update_ts > 0)
        {
            var interval = heavy ? 0.35 : 0.2;
            if (now - _last_plate_update_ts < interval && moved < 0.5) return true;
        }
        _last_cam_pos = pos;
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
                if (_shouldThrottle())
                {
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
                else
                {
                    _cached_empires.Clear();
                    foreach (var empire in ModClass.EMPIRE_MANAGER)
                    {
                        if (empire == null || empire.IsArchived() || empire.CoreKingdom == null) continue;
                        _cached_empires.Add(empire);
                        var npt = prepareNext(pManager, pAsset, empire, 37, 12, 39, 11);
                        npt._showing = true;
                        npt.setPriority(9999999 + empire.CountPopulation());
                        showTextEmpire(npt, empire.CoreKingdom);
                    }
                }

                switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
                {
                    case 0:
                        if (_shouldThrottle())
                        {
                            int budget = 128;
                            int processed = 0;
                            if (_cached_kingdoms_no_back.Count == 0 && _cached_kingdoms.Count == 0)
                            {
                                _cached_kingdoms_no_back.Clear();
                                _cached_kingdoms.Clear();
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                                    {
                                        _cached_kingdoms_no_back.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        nameplateText._showing = true;
                                        nameplateText.setPriority(kingdom.getPopulationPeople());
                                        showTextKingdomNoBack(nameplateText, kingdom);
                                        if (++processed >= budget) break;
                                    }
                                }
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (kingdom.hasCapital() && !kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                                    {
                                        _cached_kingdoms.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        nameplateText.setPriority(kingdom.getPopulationPeople());
                                        nameplateText._showing = true;
                                        showTextKingdom(nameplateText, kingdom);
                                        if (++processed >= budget) break;
                                    }
                                }
                            }
                            else
                            {
                                _cached_kingdoms_no_back.Clear();
                                _cached_kingdoms.Clear();
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center) && !_cached_kingdoms_no_back.Contains(kingdom))
                                    {
                                        _cached_kingdoms_no_back.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        nameplateText._showing = true;
                                        nameplateText.setPriority(kingdom.getPopulationPeople());
                                        showTextKingdomNoBack(nameplateText, kingdom);
                                        if (++processed >= budget) break;
                                    }
                                }
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (kingdom.hasCapital() && !kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center) && !_cached_kingdoms.Contains(kingdom))
                                    {
                                        _cached_kingdoms.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        nameplateText.setPriority(kingdom.getPopulationPeople());
                                        nameplateText._showing = true;
                                        showTextKingdom(nameplateText, kingdom);
                                        if (++processed >= budget) break;
                                    }
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || !k.hasCapital()) continue;
                                var nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, k);
                                nameplateText._showing = true;
                                nameplateText.setPriority(k.getPopulationPeople());
                                showTextKingdomNoBack(nameplateText, k);
                                if (++processed >= budget) break;
                            }
                            for (int i = 0; i < _cached_kingdoms.Count; i++)
                            {
                                var k = _cached_kingdoms[i];
                                if (k == null || !k.hasCapital()) continue;
                                var nameplateText =
                                    pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, k);
                                nameplateText._showing = true;
                                nameplateText.setPriority(k.getPopulationPeople());
                                showTextKingdom(nameplateText, k);
                                if (++processed >= budget) break;
                            }
                        }
                        else
                        {
                            _cached_kingdoms_no_back.Clear();
                            _cached_kingdoms.Clear();
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms_no_back.Add(kingdom);
                                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                    nameplateText.setPriority(kingdom.getPopulationPeople());
                                    nameplateText._showing = true;
                                    showTextKingdomNoBack(nameplateText, kingdom);
                                }
                            }
                            foreach (Kingdom kingdom in World.world.kingdoms)
                            {
                                if (kingdom.hasCapital() && !kingdom.IsInEmpire() && isWithinCamera(kingdom.capital.city_center))
                                {
                                    _cached_kingdoms.Add(kingdom);
                                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                     nameplateText.setPriority(kingdom.getPopulationPeople());
                                    showTextKingdom(nameplateText, kingdom);
                                }
                            }
                        }
                        break;
                    case 1:
                        if (_shouldThrottle())
                        {
                            if (_cached_kingdoms_no_back.Count == 0)
                            {
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasTakenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                    {
                                        _cached_kingdoms_no_back.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        showTextKingdomNoBack(nameplateText, kingdom);
                                    }
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || !k.hasCapital()) continue;
                                var nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, k);
                                nameplateText.setPriority( k.getPopulationPeople());
                                showTextKingdomNoBack(nameplateText, k);
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
                                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                     nameplateText.setPriority(kingdom.getPopulationPeople());
                                    showTextKingdomNoBack(nameplateText, kingdom);
                                }
                            }
                        }
                        break;
                    case 2:
                        if (_shouldThrottle())
                        {
                            if (_cached_kingdoms_no_back.Count == 0)
                            {
                                foreach (Kingdom kingdom in World.world.kingdoms)
                                {
                                    if (!kingdom.IsEmpire() && kingdom.hasCapital() && kingdom.HasGivenAlliance() && isWithinCamera(kingdom.capital.city_center))
                                    {
                                        _cached_kingdoms_no_back.Add(kingdom);
                                        NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                        showTextKingdomNoBack(nameplateText, kingdom);
                                    }
                                }
                            }
                            for (int i = 0; i < _cached_kingdoms_no_back.Count; i++)
                            {
                                var k = _cached_kingdoms_no_back[i];
                                if (k == null || !k.hasCapital()) continue;
                                var nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, k);
                                nameplateText.setPriority(k.getPopulationPeople());
                                showTextKingdomNoBack(nameplateText, k);
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
                                    NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_kingdom, kingdom);
                                     nameplateText.setPriority(kingdom.getPopulationPeople());
                                    showTextKingdomNoBack(nameplateText, kingdom);
                                }
                            }
                        }
                        break;
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
                if (_shouldThrottle())
                {
                    for (int i = 0; i < _cached_titles.Count; i++)
                    {
                        var t = _cached_titles[i];
                        if (t == null || t.isRekt() || t.title_capital == null || t.title_capital.isRekt()) continue;
                        var npt = prepareNext(pManager, pAsset, t, 37, 12, 39, 11);
                        showTextTitle(npt, t.title_capital);
                    }
                    for (int i = 0; i < _cached_cities_no_title.Count; i++)
                    {
                        var c = _cached_cities_no_title[i];
                        if (c == null || c.isRekt()) continue;
                        var npt = pManager.prepareNext(AssetManager.nameplates_library._plate_city, c);
                        showTextCity(npt, c, c.city_center);
                    }
                }
                else
                {
                    _cached_titles.Clear();
                    _cached_cities_no_title.Clear();
                    foreach (KingdomTitle kingdomTitle in ModClass.KINGDOM_TITLE_MANAGER)
                    {
                        if (!kingdomTitle.isRekt() && !kingdomTitle.title_capital.isRekt() && isWithinCamera(kingdomTitle.title_capital.city_center))
                        {
                            _cached_titles.Add(kingdomTitle);
                            NameplateText npt = prepareNext(pManager, pAsset, kingdomTitle, 37, 12, 39, 11);
                            showTextTitle(npt, kingdomTitle.title_capital);
                        }
                    }
                    foreach (City city in World.world.cities)
                    {
                        if (!city.hasTitle() && isWithinCamera(city.city_center))
                        {
                            _cached_cities_no_title.Add(city);
                            NameplateText nameplateText = pManager.prepareNext(AssetManager.nameplates_library._plate_city, city);
                            showTextCity(nameplateText, city, city.city_center);
                        }
                    }
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
                if (_shouldThrottle())
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
                if (_shouldThrottle())
                {
                    int budget = 128;
                    int processed = 0;
                    for (int i = 0; i < _cached_kingdoms.Count; i++)
                    {
                        var kingdom = _cached_kingdoms[i];
                        if (kingdom == null || kingdom.isRekt() || !kingdom.hasCapital()) continue;
                        NameplateText nameplateText = pManager.prepareNext(pAsset, kingdom);
                        showTextKingdom(nameplateText, kingdom);
                        if (++processed >= budget) break;
                    }
                    for (int i = 0; i < _cached_neutral_cities.Count; i++)
                    {
                        var city = _cached_neutral_cities[i];
                        if (city == null || city.isRekt()) continue;
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
        npt.setText(pNewText, (Vector3) pMetaObject.capital.city_center);
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
        npt._banner_kingdoms.load((NanoObject) pMetaObject);
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        npt.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        Clan kingClan = pMetaObject.getKingClan();
        if (kingClan != null)
        {
            npt._show_banner_clan = true;
            npt._banner_clan.load((NanoObject) kingClan);
        }
        npt.nano_object = (NanoObject) pMetaObject;
    }	
    public static void showTextKingdomNoBack(NameplateText npt, Kingdom pMetaObject)
    {
        npt.setupMeta(pMetaObject.data, pMetaObject.getColor());
        string pNewText = $"{pMetaObject.name} {pMetaObject.getPopulationPeople().ToString()+additionNum} | {pMetaObject.countTotalWarriors()}/{pMetaObject.countWarriorsMax()}";
        switch (EmpireCraftMetaTypeLibrary.empire.getZoneOptionState())
        {
            case 0:
                if (pMetaObject.IsInEmpire())
                {
                    var corruption = (int)(pMetaObject.GetCorruptionRate()*100);
                    pNewText +=
                        $"\n腐败值：{corruption.ToString().ColorString(pColor: corruption <= 30 ? Color.green : Color.red)}%";
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
        npt.setText(pNewText, pMetaObject.capital.city_center);
        npt._background_image.enabled = false;
        npt.priority_population = pMetaObject.units.Count;
        npt.showSpecies(pMetaObject.getSpriteIcon());
        npt._show_banner_kingdom = false;
        npt._show_banner_clan = false;
        npt._text_name.supportRichText = true;
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        npt.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        // 给文字加蓝色边框（描边）
        var outline = npt._text_name.GetComponent<Outline>();
        if (outline != null)
        {
            outline.enabled = false;
        }
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
                $" | 腐败值：{corruption.ToString().ColorString(pColor: corruption <= 30 ? Color.green : Color.red)}%";
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
        npt.setText(text, pPosition);
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
        return nameplateText;
    }
    public static void prepare(NameplateText nameplateText, NameplateAsset pAsset, NanoObject pMeta, float pGlobalScale, NameplateRenderingType pNameplateMode, bool pNanoObjectSet, NanoObject pSelectedNanoObject)
    {
        if (pNanoObjectSet)
        {
            pNameplateMode = ((pSelectedNanoObject == pMeta) ? NameplateRenderingType.Full : NameplateRenderingType.BannerOnly);
        }
        if (pNameplateMode != nameplateText._last_mode)
        {
            nameplateText.clearCaches();
            nameplateText._active_check_dirty = true;
            nameplateText._last_mode = pNameplateMode;
            
            // 给文字加蓝色边框（描边）
            var outline = nameplateText._text_name.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
            switch (nameplateText._last_mode)
            {
                case NameplateRenderingType.Full:
                    nameplateText._background_image.transform.localScale = new Vector3(1f, 1f, 1f);
                    nameplateText._background_image.enabled = true;
                    nameplateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one;
                    nameplateText._text_name.fontStyle = FontStyle.Normal;
                    nameplateText._text_name.transform.localScale = Vector3.one;
                    break;
                case NameplateRenderingType.BannerOnly:
                    if (pMeta.meta_type == MetaTypeExtension.KingdomTitle)
                    {
                        nameplateText._background_image.transform.localScale = Vector3.one;
                        nameplateText._background_image.enabled = false;
                        nameplateText._banner_kingdoms.enabled = false;
                    }
                    else
                    {
                        nameplateText._text_name.fontStyle = FontStyle.Normal;
                        nameplateText._text_name.transform.localScale = Vector3.one;
                        nameplateText._background_image.transform.localScale = new Vector3(pAsset.banner_only_mode_scale, pAsset.banner_only_mode_scale, 1f);
                        nameplateText._background_image.enabled = false;
                    }
                    break;
            }
        }
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
        int difference = (empire.data.PreviousYearsMoney.Count > 2
            ? (empire.data.PreviousYearsMoney.Last() -
               empire.data.PreviousYearsMoney[empire.data.PreviousYearsMoney.Count - 2])
            : 0);
        string moneyText =
            $"\n国库:{empire.CoreKingdom.GetMoney()}" +
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
                            $"\n{(empire.EmpireClan?.name ?? "无皇室").ColorString(pColor: Color.yellow)} | 主导: {faction.Name}" +
                            moneyText + "\n"+ $"正统性: {empire.Mandate}" + "\n" +
                            text.ColorString(pColor: pMetaObject.getColor()._color_banner) +
                            $"\n诉求：{(tf!=null ? tf.type.ToString(): "无")}".ColorString(
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

        plateText.setText(text, pMetaObject?.capital?.city_center??new Vector2(99, 99));
        plateText._text_name.supportRichText = true;
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

        outline.effectColor = new Color(1f, 1f, 0.0f, a:0.2f);           // 边框颜色：蓝色
        outline.effectDistance = new Vector2(1f, -1f); // 边框粗细（X/Y 像素偏移，可以自己调）
        
        float scale = (MoveCamera.instance.orthographic_size_max-MoveCamera.instance.main_camera.orthographicSize+100)*0.001f*3;
        plateText.forceScale((scale>0.4f?0.4f:scale)*Vector2.one);
        plateText._background_image.enabled = EmpireCraftWorldLawLibrary.empirecraft_law_simplify_nameplates.isEnabled();
        plateText._text_name.color = Color.white;
        
        plateText.priority_population = pMetaObject.units.Count;
        plateText._show_icon_species = false;
        plateText._show_banner_kingdom = false;
        plateText._show_banner_clan = false;
        plateText.nano_object = empire.CoreKingdom;
    }

    public static void showTextTitle(NameplateText plateText, City capital)
    {
        if (ModClass.IS_CLEAR) return;
        if (capital == null) return;
        if (!capital.hasTitle()) return;
        try
        {
            plateText.setupMeta(capital.data, capital.GetTitle().getColor());
            string text = capital.GetTitle().data.name;
            plateText.setText(text, capital.city_center);
            plateText._text_name.fontStyle = FontStyle.Bold;
            plateText._text_name.transform.localPosition = Vector3.zero;
            plateText._text_name.transform.localScale = Vector3.one * 1.5f;
            plateText._text_name.color = Color.white;
            
            // 给文字加蓝色边框（描边）
            var outline = plateText._text_name.GetComponent<Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
            
            plateText._banner_kingdoms.dead_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.left_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.winner_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.loser_image.gameObject.SetActive(value: false);
            plateText._banner_kingdoms.background.sprite = capital.GetTitle()?.getElementBackground();
            plateText._banner_kingdoms.icon.sprite = capital.GetTitle()?.getElementIcon();
            var color = capital.GetTitle().kingdomColor.getColorBanner();
            color = new Color(color.r, color.g, color.b, 0.5f);
            plateText._banner_kingdoms.background.color = color;
            plateText._banner_kingdoms.icon.color = color;
            plateText._banner_kingdoms.gameObject.transform.localPosition = Vector3.zero;
            plateText._banner_kingdoms.gameObject.transform.localScale = Vector3.one*1.5f;
            plateText._show_banner_kingdom = true;
            plateText._background_image.enabled = false;
            plateText.nano_object = capital;
            
        }
        catch
        {
        }

    }
}
