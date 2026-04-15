using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.Layer;
using NeoModLoader.General;
using NeoModLoader.services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EmpireCraft.Scripts.Regimes;
using EmpireCraft.Scripts.System;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class TranslateHelper
    {
        public static string GetPeerageTranslate(PeeragesLevel pl)
        {
            return LM.Get("default_" + pl.ToString());
        }
        public static void LogNewEmpire(Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.become_new_empire_log, empire.CoreKingdom.king.name, empire.CoreKingdom.data.name)
            {
                color_special1 = empire.CoreKingdom.getColor().getColorText(),
                color_special2 = empire.CoreKingdom.getColor().getColorText(),
            }.add();
        }

        public static void LogministerTryAqcuireEmpire(Actor minister, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.minister_try_aqcuire_empire_log, minister.GetTitle(), minister.name, empire.name)
            {
                color_special1 = minister.getColor()._color_text,
                color_special2 = minister.getColor()._color_text,
                color_special3 = empire.getColor()._color_text,
            }.add();
        }

        /// <summary>
        /// 记录“大臣获封称号”
        ///     $empire$ → 帝国名
        ///     $minister$ → 大臣名
        ///     $title$ → 新称号
        /// </summary>
        public static void LogPowerfulMinisterAcquireTitle(Actor minister, Empire empire, string title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_powerful_minister_aquire_title,
                minister.data.name,
                empire.GetEmpireName(),
                title)
            {
                color_special1 = minister.kingdom.getColor()._color_text,
                color_special2 = empire.CoreKingdom.getColor()._color_text

            }.add();
            empire.RecordHistory(EmpireHistoryType.powerful_minister_history, new Dictionary<string, string>()
            {
                ["actor"] = minister.getName(),
                ["empire"] = empire.GetEmpireName(),
                ["title"] = title
            });
        }
        public static void LogCreateTitle(Kingdom kingdom, KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.king_create_title_log,
                kingdom.data.name,
                kingdom.king.data.name,
                title.data.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = kingdom.getColor()._color_text,
                color_special3 = title.getColor()._color_text
            }.add();
        }
        
        public static void LogKingChooseHeir(Kingdom kingdom,string relation, Actor pActor)
        {
            if (kingdom != null&&pActor!=null)
                new WorldLogMessage(EmpireCraftWorldLogLibrary.king_choose_heir_log,
                    (kingdom.GetKingdomName() ?? "") + (kingdom.GetOffice()?.GetName() ?? ""),
                    relation,
                    pActor.name)
                {
                    color_special1 = kingdom.getColor()._color_text,
                    color_special3 = pActor.getColor()._color_text
                }.RecordIntoEmpire(pActor.GetEmpire());
        }
        
        public static void LogProvinceChangeToKingdom(Kingdom province, string name)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.province_change_to_kingdom_log,
                province.data.name,
                name
                )
            {
                color_special1 = province.getColor()._color_text,
                color_special2 = province.getColor()._color_text

            }.RecordIntoEmpire();
        }
        public static string GetEmpireHistoryFormatedText(this WorldLogMessage pMessage)
        {
            WorldLogAsset asset = pMessage.getAsset();
            string localeId;
            if (asset.random_ids > 0)
            {
                int pIndex = pMessage.timestamp % asset.random_ids + 1;
                localeId = asset.getLocaleID(pIndex);
            }
            else
                localeId = asset.getLocaleID();
            string text = LocalizedTextManager.getText(localeId);
            if (asset.text_replacer != null)
                asset.text_replacer(pMessage, ref text);
            return text.ColorString(pColor:asset.color);
        }
        public static void RecordIntoEmpire(this WorldLogMessage worldLog, Empire pEmpire = null)
        {
            worldLog.add();
            LogService.LogInfo("世界提示完毕,开始记录入历史");
            pEmpire?.RecordHistory(directContent: worldLog.GetEmpireHistoryFormatedText());
        }
        public static void LogOfficerJoinFaction(OfficeObject office, Actor pActor, FixedFaction faction)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.officer_join_faction,
                office?.GetOfficeName() ?? "",
                pActor.name,
                faction.Name
            )
            {
                color_special1 = pActor.getColor()._color_text,
                color_special2 = pActor.getColor()._color_text,
                color_special3 = pActor.getColor()._color_text

            }.RecordIntoEmpire(faction.Empire);
        }
        
        public static void LogOfficerBecomeFactionLeader(Actor pActor, FixedFaction faction)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.officer_become_faction_leader,
                pActor.getName(),
                faction.Name
            )
            {
                color_special1 = new Color(1f, 0, 0.5f),
                color_special2 = new Color(0.7f, 0.0f, 0.9f),

            }.RecordIntoEmpire(faction.Empire);
        }
        public static void LogNewJingShi(Empire empire, Actor pActor)
        {

            new WorldLogMessage(EmpireCraftWorldLogLibrary.new_jingshi_log,
                empire.data.name,
                pActor.data.name
                )
            {
                color_special1 = empire.CoreKingdom.getColor()._color_text,
                color_special2 = empire.CoreKingdom.getColor()._color_text

            }.RecordIntoEmpire(pActor.GetEmpire());
        }
        public static void LogDestroyTitle(Kingdom kingdom, KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.destroy_title_log,
                kingdom.king.data.name,
                title.data.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = title.getColor()._color_text

            }.add();
        }
        public static void LogKingdomAcquireTitle(Kingdom attacker, Kingdom defender, KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.history_kingdom_attack_for_title,
                attacker.data.name,
                defender.data.name,
                title.data.name)
            {
                color_special1 = attacker.getColor()._color_text,
                color_special2 = defender.getColor()._color_text,
                color_special3 = title.getColor()._color_text

            }.RecordIntoEmpire(attacker.GetEmpire());
        }
        public static void LogReligionWarTransfer(City city, Religion religion)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.religion_war_transfer_log,
                city.data.name,religion.data.name
                )
            {
                color_special1 = city.getColor()._color_text,
                color_special2 = religion.getColor()._color_text

            }.add();
        }
        public static void LogOfficeMove(Actor actor, PeerageType type, int officeLevel)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.官员品级调动,
                actor.data.name, LM.Get($"Huaxia_honoraryofficial_{type.ToString()}_{officeLevel}"), ""+(officeLevel+1)
                )
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = actor.getColor()._color_text,
                color_special3 = actor.getColor()._color_text

            }.RecordIntoEmpire();
        }
        public static void LogControlledEmpire(Actor actor, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.cotrolled_country_log,
                actor.data.name,
                empire.data.name
                )
            {
                color_special1 = empire.getColor()._color_text,
                color_special2 = empire.getColor()._color_text

            }.add();
        }
        public static void LogJoinEmpireWar(Kingdom kingdom, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.join_empire_war_log,
                kingdom.data.name,
                empire.data.name
                )
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = empire.getColor()._color_text

            }.RecordIntoEmpire(empire);
        }
        public static void LogEmpireJoinWar(Empire empire, Kingdom kingdom)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.join_empire_war_log,
                empire.data.name,
                kingdom.data.name
                )
            {
                color_special1 = empire.getColor()._color_text,
                color_special2 = kingdom.getColor()._color_text

            }.RecordIntoEmpire(empire);
        }
        public static void LogJoinReligionWar(Kingdom kingdom, Religion religion)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.join_religion_war_log,
                kingdom.data.name,
                religion.data.name
                )
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = religion.getColor()._color_text

            }.RecordIntoEmpire(kingdom.GetEmpire());
        }
        public static void LogOfficerBuildSpecificClan(Actor actor, SpecificClan sc)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.officer_build_specific_clan,
                actor.name,
                sc.name
                )
            {
                location = actor.current_position,
                color_special1 = actor.kingdom.getColor()._color_text,
                color_special2 = actor.clan.getColor()._color_text

            }.add();
        }
        public static void LogKingdomChangeCapitalToTitle(Kingdom kingdom,KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.history_kingdom_change_capital_to_title,
                kingdom.data.name,
                title.data.name,
                kingdom.capital.data.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = title.getColor()._color_text,
                color_special3 = kingdom.getColor()._color_text

            }.RecordIntoEmpire(kingdom.GetEmpire());
        }
        public static void LogKingdomJoinEmpire(Kingdom kingdom,Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.history_kingdom_join_empire,
                kingdom.data.name,
                empire.data.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = empire.getColor()._color_text

            }.RecordIntoEmpire(empire);
        }
        public static void LogNewEmperor(Actor emperor, City city, string year_name, bool isNew = false)
        {
            var empire = emperor.GetEmpire();
            new WorldLogMessage(EmpireCraftWorldLogLibrary.history_new_emperor,
                emperor.data.name,
                city.GetCityName(),
                year_name)
            {
                color_special1 = emperor.kingdom.getColor()._color_text,
                color_special2 = emperor.kingdom.getColor()._color_text,
                color_special3 = emperor.kingdom.getColor()._color_text

            }.RecordIntoEmpire(empire);
            if (empire != null)
            {
                empire.data.currentHistory.is_first = isNew;
            }
        }
        public static void LogNewEmperorWest(Actor emperor, City city, bool isNew = false)
        {
            var empire = emperor.GetEmpire();
            new WorldLogMessage(EmpireCraftWorldLogLibrary.history_new_emperor_west,
                emperor.data.name,
                city.GetCityName())
            {
                color_special1 = emperor.kingdom.getColor()._color_text,
                color_special2 = emperor.kingdom.getColor()._color_text

            }.RecordIntoEmpire(empire);
            if (empire != null)
            {
                empire.data.currentHistory.is_first = isNew;
            }
        }

        /// <summary>
        /// 加入朝贡国的提示
        /// </summary>
        /// <param name="kingdom">朝贡国</param>
        /// <param name="empire">宗主国</param>
        public static void LogJoinTakenAlliance(Kingdom kingdom, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.成为朝贡国,
                kingdom.name,
                empire.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = empire.CoreKingdom.getColor()._color_text

            }.add();
        }
        public static void LogCityAddToTitle(City city, KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.city_add_to_title_log,
                city.data.name,
                title.data.name)
            {
                color_special1 = city.getColor()._color_text,
                color_special2 = title.getColor()._color_text

            }.add();
        }

        public static void LogKingTakeTitle(Kingdom kingdom,KingdomTitle title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.king_take_title_log,
                kingdom.data.name,
                kingdom.king.getName(),
                title.data.name)
                {
                    color_special1 = kingdom.getColor()._color_text,
                    color_special2 = kingdom.getColor()._color_text,
                    color_special3 = title.getColor()._color_text

                }.add();
        }

        public static void LogBecomeKingdom(Kingdom kingdom,string title)
        {
            if (kingdom != null&&title!=null) 
            {
                new WorldLogMessage(EmpireCraftWorldLogLibrary.become_kingdom_log,
                kingdom.king.data.name,
                title,
                kingdom.data.name)
                {
                    color_special1 = kingdom.getColor()._color_text,
                    color_special2 = kingdom.getColor()._color_text,
                    color_special3 = kingdom.getColor()._color_text

                }.RecordIntoEmpire(kingdom.GetEmpire());
            }

        }
        public static void LogChangeCityName(Actor pActor, City pCity, string beforeName, string afterName)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.change_city_name_log,
                pActor.data.name,
                beforeName,
                afterName)
            {
                color_special1 = pActor.getColor()._color_text,
                color_special2 = pCity.getColor()._color_text,
                color_special3 = pCity.getColor()._color_text

            }.RecordIntoEmpire();
        }
        public static void LogChangeKingdomName(Actor pActor, Kingdom pKingdom, string beforeName, string afterName)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.change_kingdom_name_log,
                pActor.data.name,
                beforeName,
                afterName)
            {
                color_special1 = pActor.getColor()._color_text,
                color_special2 = pKingdom.getColor()._color_text,
                color_special3 = pKingdom.getColor()._color_text

            }.RecordIntoEmpire();
        }

        public static void LogCombineKingdom(Actor pActor)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.combine_kingdom_log,
                pActor.name)
            {
                color_special1 = pActor.getColor()._color_text

            }.RecordIntoEmpire(pActor.GetEmpire());
        }
        /// <summary>
        /// 新年号
        ///     $empire$ → 帝国名
        ///     $emperor$ → 皇帝名
        ///     $year_name$ → 年号
        /// </summary>
        public static void LogEmperorCreateNewYearName(Actor minister, Empire empire, string title)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_powerful_minister_aquire_title,
                minister.data.name,
                empire.GetEmpireName(),
                title)
            {
                color_special1 = minister.kingdom.getColor()._color_text,
                color_special2 = empire.CoreKingdom.getColor()._color_text

            }.RecordIntoEmpire(empire);
        }

        /// <summary>
        /// 记录“大臣获取了天命”
        ///     $title$ → 称号
        ///     $minister$ → 大臣名
        ///     $empire$ → 新帝国名
        /// </summary>
        public static void LogministerAqcuireEmpire(Actor minister, Empire new_empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.minister_aqcuire_empire_log,
                minister.GetTitle(),
                minister.name,
                new_empire.GetEmpireName())
            {
                color_special1 = minister.kingdom.getColor()._color_text,
                color_special2 = new_empire.CoreKingdom.getColor()._color_text,
            }.RecordIntoEmpire(new_empire);
        }
        /// <summary>
        /// 记录“恢复历史帝国”
        ///     $clan$ -> 家族
        ///     $empire$ → 帝国名
        /// </summary>
        public static void LogRestoreHistoricalEmpire(Clan clan, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.restore_historcial_empire,
                empire.name)
            {
                color_special1 = clan.getColor()._color_text,
                color_special2 = empire.CoreKingdom.getColor()._color_text
            }.RecordIntoEmpire(empire);
        }
        /// <summary>
        /// 记录“追封先帝”
        ///     $actor$ -> 在任皇帝
        ///     $name$ → 被追封人姓名
        /// </summary>
        public static void LogEmpeorNamingPreviousEmperor(Actor actor, string name)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.emperor_posthumous_name,
                actor.data.name, name)
            {
                color_special1 = actor.kingdom.getColor()._color_text,
                color_special2 = actor.kingdom.getColor()._color_text
            }.RecordIntoEmpire(actor.GetEmpire());
        }
    }
}
