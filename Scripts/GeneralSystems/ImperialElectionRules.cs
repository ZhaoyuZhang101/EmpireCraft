using System;

namespace EmpireCraft.Scripts.GeneralSystems;

// 选帝侯会选的纯算式(只依赖 System，便于单测；见 HolyRomanSystem.Election)：
//   · 计票：每位在座选帝侯投给自己打分最高的候选；首轮过半(票数 ×2 > 在座数)当选；否则首轮前两名决选，各选帝侯在两人中
//     投分高者，过半当选；决选平票为双重选举(对立国王)；
//   · 拉票：候选每年拿出 min(国库两成, 年收一成五)；选帝侯收的钱折成分数 0.4 × min(1, 收的钱 / max(100, 选帝侯年收))；
//   · 大空位：每年重开会选的成功几率 35% + 每过一年 15%(满五年由帝国代理定夺)。
public static class ImperialElectionRules
{
    public const float BribeWeight = 0.4f, BribeFloor = 100f, CampaignTreasuryShare = 0.2f, CampaignIncomeShare = 0.15f;
    public const float InterregnumBase = 0.35f, InterregnumPerYear = 0.15f;

    public sealed class Result
    {
        // 当选者与对手(候选序号，-1 = 没有)、两人得票(决选时为决选票数)
        public int winner = -1, rival = -1, winnerVotes, rivalVotes;
        public bool runoff, tie;
        // 首轮各候选得票
        public int[] firstRound = new int[0];
    }

    // scores[选帝侯, 候选]：已含随机项的打分
    public static Result Tally(float[,] scores)
    {
        int electors = scores.GetLength(0), candidates = scores.GetLength(1);
        var result = new Result { firstRound = new int[candidates] };
        if (electors == 0 || candidates == 0) return result;
        for (int e = 0; e < electors; e++)
        {
            int best = 0;
            for (int c = 1; c < candidates; c++)
                if (scores[e, c] > scores[e, best]) best = c;
            result.firstRound[best]++;
        }
        // 首轮前两名(得票相同按序号先后)
        int first = -1, second = -1;
        for (int c = 0; c < candidates; c++)
        {
            if (first < 0 || result.firstRound[c] > result.firstRound[first])
            {
                second = first;
                first = c;
            }
            else if (second < 0 || result.firstRound[c] > result.firstRound[second]) second = c;
        }
        if (result.firstRound[first] * 2 > electors || second < 0)
        {
            result.winner = first;
            result.rival = second;
            result.winnerVotes = result.firstRound[first];
            result.rivalVotes = second < 0 ? 0 : result.firstRound[second];
            return result;
        }
        result.runoff = true;
        int a = 0, b = 0;
        for (int e = 0; e < electors; e++)
            if (scores[e, first] >= scores[e, second]) a++;
            else b++;
        result.tie = a == b;
        result.winner = a >= b ? first : second;
        result.rival = a >= b ? second : first;
        result.winnerVotes = Math.Max(a, b);
        result.rivalVotes = Math.Min(a, b);
        return result;
    }

    // 收了钱的加分
    public static float BribeScore(long received, long electorIncome) =>
        received <= 0 ? 0f : BribeWeight * Math.Min(1f, received / Math.Max(BribeFloor, electorIncome));

    // 候选一年拉票拿得出的钱
    public static long CampaignBudget(long treasury, long income) =>
        Math.Max(0L, Math.Min((long)(Math.Max(0L, treasury) * CampaignTreasuryShare), (long)(Math.Max(0L, income) * CampaignIncomeShare)));

    // 大空位期间这一年能会选成功的几率(years = 已虚悬几年)
    public static float InterregnumChance(int years) => Math.Min(1f, InterregnumBase + InterregnumPerYear * Math.Max(0, years));
}
