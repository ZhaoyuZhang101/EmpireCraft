using EmpireCraft.Scripts.Data;
using EmpireCraft.Scripts.Enums;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.GameLibrary;
using EmpireCraft.Scripts.GeneralSystems.EmpireLaw;
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
        public static void LogMinisterSelectEmpire(Empire empire, OfficeObject office, Kingdom kingdom, Actor actor)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.minister_select_emperor_log,
                empire.data.name,
                (office?.GetOfficeName(kingdom)??"")+" "+actor.data.name
                )
            {
                color_special1 = empire.getColor()._color_text,
                color_special2 = actor.getColor()._color_text

            }.RecordIntoEmpire(empire);
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
            pEmpire?.RecordHistory(directContent: worldLog.GetEmpireHistoryFormatedText());
        }
        public static void LogLawEnforcement(LawEnforcementContext context)
        {
            if (context == null) return;

            Actor actor = context.Actor;
            Kingdom kingdom = context.Kingdom;
            Empire empire = kingdom?.GetEmpire();
            if (actor == null || kingdom == null || empire == null) return;
            if (!ShouldBroadcastLawActor(actor)) return;

            string actorName = GetActorFullLogName(actor);

            string lawName = context.Law != null ? context.Law.Name : context.LawType.ToString();
            if (string.IsNullOrEmpty(lawName))
            {
                lawName = context.LawType.ToString();
            }

            string crimeDetailText = GetLawCrimeDetailText(lawName, context.CrimeDate);
            string punishmentText = GetPunishmentText(context);
            WorldLogMessage worldLog = new WorldLogMessage(
                EmpireCraftWorldLogLibrary.empire_law_enforced_log,
                actorName,
                crimeDetailText,
                punishmentText)
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = kingdom.getColor()._color_text,
                color_special3 = kingdom.getColor()._color_text
            };

            if (ShouldRecordLawEnforcementHistory(actor, empire))
            {
                worldLog.RecordIntoEmpire(empire);
                return;
            }

            worldLog.add();
        }

        public static void LogLawArrest(Actor actor, Kingdom kingdom, string crimeName, string crimeDate)
        {
            if (actor == null || kingdom == null) return;

            Empire empire = kingdom.GetEmpire();
            if (empire == null) return;
            if (!ShouldBroadcastLawActor(actor)) return;

            string actorName = GetActorFullLogName(actor);

            WorldLogMessage worldLog = new WorldLogMessage(
                EmpireCraftWorldLogLibrary.empire_law_arrest_log,
                actorName,
                string.IsNullOrEmpty(crimeDate) ? Date.getDate(World.world.getCurWorldTime()) : crimeDate,
                crimeName)
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = kingdom.getColor()._color_text,
                color_special3 = kingdom.getColor()._color_text
            };

            if (ShouldRecordLawEnforcementHistory(actor, empire))
            {
                worldLog.RecordIntoEmpire(empire);
                return;
            }

            worldLog.add();
        }

        private static string GetPunishmentText(LawEnforcementContext context)
        {
            if (context == null || context.AppliedPunishments == null || context.AppliedPunishments.Count <= 0)
            {
                return LM.Get("empire_law_default_punishment");
            }

            List<string> punishmentNames = new List<string>();
            foreach (PunishmentLevel punishment in context.AppliedPunishments)
            {
                string punishmentName = GetPunishmentName(punishment);
                if (!string.IsNullOrEmpty(punishmentName))
                {
                    punishmentNames.Add(punishmentName);
                }
            }

            if (punishmentNames.Count <= 0)
            {
                return LM.Get("empire_law_default_punishment");
            }

            return string.Join(", ", punishmentNames);
        }

        private static string GetLawCrimeDetailText(string crimeName, string crimeDate)
        {
            string localizedCrimeName = GetTemporaryFactionCrimeText(crimeName);
            string templateKey = string.IsNullOrWhiteSpace(crimeDate)
                ? "empire_law_crime_detail_without_date"
                : "empire_law_crime_detail_with_date";
            string template = LM.Get(templateKey);
            if (string.IsNullOrWhiteSpace(template) || template == templateKey)
            {
                return string.IsNullOrWhiteSpace(crimeDate)
                    ? localizedCrimeName
                    : $"{crimeDate} {localizedCrimeName}";
            }

            return template
                .Replace("$date$", crimeDate ?? "")
                .Replace("$crime$", localizedCrimeName ?? "");
        }

        private static bool ShouldBroadcastLawActor(Actor actor)
        {
            if (actor == null || actor.isRekt())
            {
                return false;
            }

            if (actor.IsEmperor())
            {
                return false;
            }

            return actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null || actor.isKing() || actor.HasTitle() || actor.isCityLeader();
        }

        private static string GetPunishmentName(PunishmentLevel punishment)
        {
            string localeKey = "punishment_" + punishment.ToString();
            string text = LM.Get(localeKey);
            if (!string.IsNullOrEmpty(text) && text != localeKey)
            {
                return text;
            }

            return punishment.ToString();
        }

        private static bool ShouldRecordLawEnforcementHistory(Actor actor, Empire empire)
        {
            if (actor == null || empire == null) return false;

            if (actor.isOfficer() || actor.IsOnOffice() || actor.GetOffice() != null)
            {
                return true;
            }

            if (actor.IsEmperor() || actor.isKing() || actor.HasTitle())
            {
                return true;
            }

            if (actor.HasSpecificClan() && empire.Emperor != null && empire.Emperor.HasSpecificClan())
            {
                return actor.GetSpecificClan() == empire.Emperor.GetSpecificClan();
            }

            return false;
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

        private static WorldLogMessage CreateTemporaryFactionMessage(WorldLogAsset asset, Empire empire, string pSpecial1, string pSpecial2 = "", string pSpecial3 = "")
        {
            return new WorldLogMessage(asset, pSpecial1, pSpecial2, pSpecial3)
            {
                color_special1 = empire?.CoreKingdom?.getColor()?._color_text ?? Toolbox.color_log_good,
                color_special2 = empire?.CoreKingdom?.getColor()?._color_text ?? Toolbox.color_log_good,
                color_special3 = empire?.CoreKingdom?.getColor()?._color_text ?? Toolbox.color_log_good
            };
        }

        private static string GetTemporaryFactionClaimText(string claimName)
        {
            if (string.IsNullOrWhiteSpace(claimName))
            {
                return "";
            }

            string localized = LM.Get(claimName);
            return !string.IsNullOrEmpty(localized) && localized != claimName ? localized : claimName;
        }

        private static string GetTemporaryFactionCrimeText(string crimeName)
        {
            if (string.IsNullOrWhiteSpace(crimeName))
            {
                return "";
            }

            string localized = LM.Get(crimeName);
            return !string.IsNullOrEmpty(localized) && localized != crimeName ? localized : crimeName;
        }

        private static string GetBaseActorLogName(Actor actor)
        {
            if (actor == null)
            {
                return "";
            }

            string actorName = actor.getName();
            if (string.IsNullOrWhiteSpace(actorName))
            {
                actorName = actor.name;
            }

            return actorName ?? "";
        }

        private static string GetActorLogOfficeName(Actor actor)
        {
            if (actor == null)
            {
                return "";
            }

            OfficeObject office = actor.GetOffice();
            if (office != null)
            {
                string officeName = office.GetOfficeName(office.meta_object);
                if (!string.IsNullOrWhiteSpace(officeName))
                {
                    return officeName;
                }
            }

            string title = actor.GetTitle();
            return string.IsNullOrWhiteSpace(title) ? "" : title;
        }

        private static string FormatActorFullLogName(string officeName, string actorName)
        {
            if (string.IsNullOrWhiteSpace(actorName))
            {
                return officeName ?? "";
            }

            if (string.IsNullOrWhiteSpace(officeName))
            {
                return actorName;
            }

            string template = LM.Get("log_actor_full_name_format");
            if (string.IsNullOrWhiteSpace(template) || template == "log_actor_full_name_format")
            {
                return officeName + " " + actorName;
            }

            return template
                .Replace("$office$", officeName)
                .Replace("$actor$", actorName);
        }

        public static string GetActorFullLogName(Actor actor)
        {
            if (actor == null)
            {
                return "";
            }

            return FormatActorFullLogName(GetActorLogOfficeName(actor), GetBaseActorLogName(actor));
        }

        public static string GetTemporaryFactionTargetText(MetaType targetType, long targetId)
        {
            if (targetId < 0)
            {
                return null;
            }

            switch (targetType)
            {
                case MetaType.Kingdom:
                    return World.world?.kingdoms?.get(targetId)?.GetKingdomName();
                case MetaType.City:
                    return World.world?.cities?.get(targetId)?.GetCityName();
                case MetaType.Religion:
                    return World.world?.religions?.get(targetId)?.data?.name;
                case MetaType.Unit:
                    return GetActorFullLogName(World.world?.units?.get(targetId));
                case MetaType.None:
                    return null;
                default:
                    if (targetType == MetaTypeExtension.KingdomTitle)
                    {
                        return ModClass.KINGDOM_TITLE_MANAGER.get(targetId)?.data?.name;
                    }

                    return null;
            }
        }

        public static void LogTemporaryFactionPreparing(Empire empire, string claimName, string targetName = null, string crimeName = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(EmpireCraftWorldLogLibrary.temporary_faction_prepare_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? EmpireCraftWorldLogLibrary.temporary_faction_prepare_no_target_log
                : EmpireCraftWorldLogLibrary.temporary_faction_prepare_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName),
                targetName ?? "").RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionSucceeded(Empire empire, string claimName, string targetName = null, string crimeName = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(EmpireCraftWorldLogLibrary.temporary_faction_success_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? EmpireCraftWorldLogLibrary.temporary_faction_success_no_target_log
                : EmpireCraftWorldLogLibrary.temporary_faction_success_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName),
                targetName ?? "").RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionFailed(Empire empire, string claimName, string targetName = null, string crimeName = null)
        {
            if (empire == null || string.IsNullOrWhiteSpace(claimName))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(crimeName) && !string.IsNullOrWhiteSpace(targetName))
            {
                CreateTemporaryFactionMessage(EmpireCraftWorldLogLibrary.temporary_faction_failed_crime_log, empire,
                    targetName,
                    GetTemporaryFactionCrimeText(crimeName),
                    GetTemporaryFactionClaimText(claimName)).RecordIntoEmpire(empire);
                return;
            }

            var asset = string.IsNullOrWhiteSpace(targetName)
                ? EmpireCraftWorldLogLibrary.temporary_faction_failed_no_target_log
                : EmpireCraftWorldLogLibrary.temporary_faction_failed_log;

            CreateTemporaryFactionMessage(asset, empire,
                empire.GetEmpireName() ?? empire.data?.name ?? "",
                GetTemporaryFactionClaimText(claimName),
                targetName ?? "").RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionOfficialFall(Empire empire, Actor actor, string crimeName)
        {
            if (empire == null || actor == null || string.IsNullOrWhiteSpace(crimeName))
            {
                return;
            }

            new WorldLogMessage(EmpireCraftWorldLogLibrary.temporary_faction_official_fall_log,
                actor.getName(),
                crimeName)
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = empire.CoreKingdom?.getColor()?._color_text ?? Toolbox.color_log_warning,
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionReduceFeudatory(Empire empire, Kingdom kingdom)
        {
            if (empire == null || kingdom == null) return;
            new WorldLogMessage(EmpireCraftWorldLogLibrary.temporary_faction_reduce_feudatory_log,
                kingdom.GetKingdomName())
            {
                color_special1 = kingdom.getColor()._color_text
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRevokeWarRight(Empire empire, Kingdom kingdom)
        {
            if (empire == null || kingdom == null) return;
            new WorldLogMessage(EmpireCraftWorldLogLibrary.temporary_faction_revoke_war_right_log,
                kingdom.GetKingdomName())
            {
                color_special1 = kingdom.getColor()._color_text
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRevokeMilitaryRegion(Empire empire, Kingdom kingdom)
        {
            if (empire == null || kingdom == null) return;
            new WorldLogMessage(EmpireCraftWorldLogLibrary.temporary_faction_revoke_military_region_log,
                kingdom.GetKingdomName())
            {
                color_special1 = kingdom.getColor()._color_text
            }.RecordIntoEmpire(empire);
        }

        public static void LogTemporaryFactionRaiseTax(Empire empire)
        {
            if (empire == null) return;
            new WorldLogMessage(EmpireCraftWorldLogLibrary.temporary_faction_raise_tax_log,
                empire.GetEmpireName() ?? empire.data?.name ?? "")
            {
                color_special1 = empire.CoreKingdom?.getColor()?._color_text ?? Toolbox.color_log_warning,
            }.RecordIntoEmpire(empire);
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
        public static void LogJoinRebellionWar(Kingdom joiner, Kingdom beginner, Empire empire)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.join_rebellion_war_log,
                joiner.data.name,
                beginner.data.name
                )
            {
                color_special1 = joiner.getColor()._color_text,
                color_special2 = beginner.getColor()._color_text

            }.RecordIntoEmpire(empire);
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

        /// <summary>
        /// 拉入派系提示
        /// </summary>
        /// <param name="invitor">邀请者</param>
        /// <param name="target">目标</param>
        /// <param name="faction">派系</param>
        public static void LogInviteIntoFaction(Kingdom invitor, Kingdom target, FixedFaction faction)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.邀请入派系,
                invitor.name,
                target.name,
                faction.Name)
            {
                color_special1 = invitor.getColor()._color_text,
                color_special2 = target.getColor()._color_text,
                color_special3 = invitor.getColor()._color_text,

            }.add();
        }

        /// <summary>
        /// 罪加罪行
        /// </summary>
        /// <param name="actor">执行者</param>
        /// <param name="victim">受害者</param>
        /// <param name="crime">罪行</param>
        public static void LogExposeCrime(Kingdom actor, Kingdom victim, string crime)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.追加罪行,
                actor.name,
                victim.name,
                crime)
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = victim.getColor()._color_text,
                color_special3 = victim.getColor()._color_text,

            }.RecordIntoEmpire();
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

        public static void LogKingdomChangeMainTitle(Kingdom kingdom, KingdomTitle newTitle)
        {
            if (kingdom == null || newTitle == null) return;
            new WorldLogMessage(EmpireCraftWorldLogLibrary.kingdom_change_main_title_log,
                kingdom.data.name,
                newTitle.data.name)
            {
                color_special1 = kingdom.getColor()._color_text,
                color_special2 = kingdom.getColor()._color_text,

            }.RecordIntoEmpire(kingdom.GetEmpire());
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

        public static void LogRoyalKingBecomeEmperor(Empire empire,KingdomTitle title, Actor actor)
        {
            var language = PlayerConfig.detectLanguage();
            var text = "";
            if (language == "en")
            {
                text = $"{LM.Get("default_" + actor.GetPeeragesLevel())} of {title.name}" + " " + actor.name;
            }
            else
            {
                text = title.name+""+LM.Get("King") + " " + actor.name;
            }
            new WorldLogMessage(EmpireCraftWorldLogLibrary.royal_king_become_emperor_log,
                text,
                empire.data.name)
            {
                color_special1 = actor.getColor()._color_text,
                color_special2 = empire.getColor()._color_text

            }.RecordIntoEmpire(empire);
        }

        public static void LogEmpireTakeBackTitle(Actor a, string titles, string crime)
        {
            new WorldLogMessage(EmpireCraftWorldLogLibrary.empire_take_back_title_log,
                a.data.name,
                crime,
                titles)
            {
                color_special1 = a.getColor()._color_text,
                color_special2 = a.getColor()._color_text,
                color_special3 = a.getColor()._color_text

            }.RecordIntoEmpire(a.GetEmpire());
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
