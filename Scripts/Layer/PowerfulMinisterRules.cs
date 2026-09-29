using System;

namespace EmpireCraft.Scripts.Layer
{
    // Pure balance rules, shared by the monthly update and regression tests.
    public static class PowerfulMinisterRules
    {
        public const int EntryInfluence = 300;
        public const int MonthlyCost = 10;
        public const int StageIntervalMonths = 6;
        public const int RegencyRecoveryMonths = 12;
        public const int ReleaseControlBelow = 80;
        public const int VulnerableEmperorInfluence = 100;
        public const int EarlyReignMonths = 36;
        // 正统低于此值视为正统崩溃：权臣不再需要野心/中枢支持等"意愿"条件，
        // 但控制朝廷 → 封公 → 九锡 → 篡位 的流程和阶段间隔照走。
        public const int MandateCollapseThreshold = 30;

        public static bool IsMandateCollapsed(int mandate) => mandate < MandateCollapseThreshold;

        public static bool ShouldPreferEmpressDowager(bool isMinorEmperor, bool validMother,
            int motherInfluence, bool hasMinisterCandidate, int ministerInfluence)
        {
            return isMinorEmperor && validMother &&
                   (!hasMinisterCandidate || motherInfluence > ministerInfluence);
        }

        public static int ApplyEmpressDowagerRate(int monthlyChange, bool isEmpressDowager)
        {
            if (!isEmpressDowager || monthlyChange <= 0) return monthlyChange;
            return Math.Max(1, (int)Math.Round(monthlyChange * 0.5,
                MidpointRounding.AwayFromZero));
        }

        public static bool IsVulnerableNewEmperor(int influence, int monthsOnThrone)
        {
            return influence < VulnerableEmperorInfluence && monthsOnThrone >= 0 &&
                monthsOnThrone < EarlyReignMonths;
        }

        public static int MonthlyChange(bool isRegent, bool isChiefAndDominantLeader,
            bool hasCentralSupport, bool strongEmperor, bool regencyEnding)
        {
            return MonthlyChange(isRegent, isChiefAndDominantLeader, hasCentralSupport,
                strongEmperor, regencyEnding, false);
        }

        public static int MonthlyChange(bool isRegent, bool isChiefAndDominantLeader,
            bool hasCentralSupport, bool strongEmperor, bool regencyEnding, bool vulnerableNewEmperor)
        {
            return MonthlyChange(isRegent, isChiefAndDominantLeader, hasCentralSupport, strongEmperor,
                regencyEnding, vulnerableNewEmperor, false);
        }

        // 正统崩溃时强势皇帝、失去中枢支持、摄政到期都压不住权臣：控制度只增不减。
        public static int MonthlyChange(bool isRegent, bool isChiefAndDominantLeader,
            bool hasCentralSupport, bool strongEmperor, bool regencyEnding, bool vulnerableNewEmperor,
            bool mandateCollapsed)
        {
            if (mandateCollapsed) return isRegent || vulnerableNewEmperor ? 6 : isChiefAndDominantLeader ? 4 : 2;
            if (strongEmperor) return -3;
            if (regencyEnding || (!isRegent && !hasCentralSupport)) return -2;
            return isRegent || vulnerableNewEmperor ? 6 : isChiefAndDominantLeader ? 4 : 2;
        }

        public static int ApplyMandate(int monthlyChange, int mandate)
        {
            if (monthlyChange <= 0) return monthlyChange;
            mandate = Math.Max(0, Math.Min(100, mandate));
            double multiplier = mandate <= 50 ? 2.0 - mandate / 50.0 : 1.0 - (mandate - 50) / 100.0;
            return Math.Max(1, (int)Math.Round(monthlyChange * multiplier, MidpointRounding.AwayFromZero));
        }

        public static int OppositionPenalty(int ministerInfluence)
        {
            return -((1000 - Math.Max(0, Math.Min(1000, ministerInfluence)) + 4) / 5);
        }

        public static bool ShouldRiseAgainstMinister(int ministerInfluence, int localInfluence, bool hasLocalPower)
        {
            return ministerInfluence < 1000 && localInfluence > 500 && hasLocalPower;
        }

        public static int Advance(int progress, int influence, int months, int monthlyChange, out int cost)
        {
            progress = Math.Max(0, Math.Min(100, progress));
            months = Math.Max(0, months);
            cost = 0;
            if (monthlyChange < 0)
                return (int)Math.Max(0L, progress + (long)months * monthlyChange);
            if (monthlyChange == 0 || progress == 100) return progress;
            int neededMonths = (100 - progress + monthlyChange - 1) / monthlyChange;
            int paidMonths = Math.Min(neededMonths, Math.Min(months, Math.Max(0, influence) / MonthlyCost));
            cost = paidMonths * MonthlyCost;
            return Math.Min(100, progress + paidMonths * monthlyChange);
        }

        public static bool CanAdvance(bool controlsCourt, int progress, bool hasCentralSupport,
            bool strongEmperor, bool regencyEnding, bool usurpingDisposition, int monthsSinceStage)
        {
            return CanAdvance(controlsCourt, progress, hasCentralSupport, strongEmperor,
                regencyEnding, usurpingDisposition, monthsSinceStage, false);
        }

        public static bool CanAdvance(bool controlsCourt, int progress, bool hasCentralSupport,
            bool strongEmperor, bool regencyEnding, bool usurpingDisposition, int monthsSinceStage,
            bool hasNineBestowments)
        {
            return CanAdvance(controlsCourt, progress, hasCentralSupport, strongEmperor, regencyEnding,
                usurpingDisposition, monthsSinceStage, hasNineBestowments, false);
        }

        // 流程条件(控制朝廷、控制度满、阶段间隔)任何时候都要满足；意愿条件(中枢支持、没有强势皇帝、
        // 摄政未到期、有野心/邪恶特质)在正统崩溃时一律免除——即"无条件篡位，但流程不能省"。
        public static bool CanAdvance(bool controlsCourt, int progress, bool hasCentralSupport,
            bool strongEmperor, bool regencyEnding, bool usurpingDisposition, int monthsSinceStage,
            bool hasNineBestowments, bool mandateCollapsed)
        {
            if (!controlsCourt || progress < 100) return false;
            if (!hasNineBestowments && monthsSinceStage < StageIntervalMonths) return false;
            return mandateCollapsed ||
                   hasCentralSupport && !strongEmperor && !regencyEnding && usurpingDisposition;
        }
    }
}
