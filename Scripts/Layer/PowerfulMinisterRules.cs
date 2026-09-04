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
            return controlsCourt && progress >= 100 && hasCentralSupport && !strongEmperor &&
                !regencyEnding && usurpingDisposition &&
                (hasNineBestowments || monthsSinceStage >= StageIntervalMonths);
        }
    }
}
