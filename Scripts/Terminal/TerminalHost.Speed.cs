using System.Collections.Generic;
using NeoModLoader.General;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace EmpireCraft.Scripts.Terminal;

// 流速之议(用户 2026-11-11 "玩家可以共同设置调整时间流速, 当有玩家要调整流速的时候可以发起一次投票, 全票通过就行"
// "发起投票时每个人的奏报都会显示当前有投票点进去就可以选择投票, 可以查看目前有多少人同意多少人拒绝")：
//   · 哪位在位的君主都可以提议把世界流速改成 SpeedChoices 里的一档；同时只议一桩；
//   · 有票的是眼下领着君主、手机还连着的席位(投过票的掉线了仍算他那一票)；提议的人算赞成；
//   · 全体有票的都赞成才改：有一位反对就当场作罢(用户 2026-11-11 选"一票反对就结束")，人人都赞成了当场改，
//     到 VoteSeconds 秒还没齐也作罢；赞成的在了结前可以改成反对；
//   · 各席位的状态里带着这桩议(几人赞成、几人反对、共几位、自己表没表态、还剩几秒)，手机奏报页顶上显示、点进去表态；
//   · 议成了改设置里的终端流速(同在设置里手改)；没成的，提议人隔 VoteRestSeconds 秒才能再提。
// 电脑前不受此限：设置里随时可以直接改流速。都在主线程(命令队列与每秒那一趟)
public static partial class TerminalHost
{
    public static readonly int[] SpeedChoices = { 5, 10, 25, 50, 100 };
    private const float VoteSeconds = 90f, VoteRestSeconds = 30f;

    private sealed class SpeedVote
    {
        public int Id, Percent;
        public string ByToken, ByName;
        public float Until;
        // 令牌 → 赞成与否
        public readonly Dictionary<string, bool> Ballots = new();
    }

    private static SpeedVote _vote;
    private static int _voteSeq;
    // 提议没成的人何时能再提(令牌 → 时刻)
    private static readonly Dictionary<string, float> VoteRest = new();

    private static string VoterName(Seat seat) =>
        TerminalIntel.Strip(((seat.Realm?.data?.name ?? "") + " " + (seat.Ruler?.getName() ?? "")).Trim());

    // 有票的席位：领着君主、手机连着的；这桩议里投过票的掉线了也算
    private static List<Seat> Voters()
    {
        var voters = new List<Seat>();
        foreach (Seat seat in Seats())
            if (seat.Ruler != null && (seat.Watched || _vote != null && _vote.Ballots.ContainsKey(seat.Token))) voters.Add(seat);
        return voters;
    }

    private static void Tally(List<Seat> voters, out int yes, out int no)
    {
        yes = no = 0;
        foreach (Seat seat in voters)
            if (_vote.Ballots.TryGetValue(seat.Token, out bool agree))
            {
                if (agree) yes++;
                else no++;
            }
    }

    // 各席位这就重拼状态(议起、有人表态、了结，都要马上看得到)
    private static void TouchAll()
    {
        foreach (Seat seat in SeatByToken.Values) seat.NextState = 0f;
    }

    // 手机上下的两道令：speed_propose(choice = 百分比)、speed_vote(target = 议的编号，choice = yes / no)
    private static void SpeedDecree(Seat seat, string id, string target, string choice)
    {
        float now = Time.realtimeSinceStartup;
        if (id == "speed_propose")
        {
            if (!int.TryParse(choice, out int percent) || global::System.Array.IndexOf(SpeedChoices, percent) < 0) return;
            if (percent == SpeedPercent)
            {
                Notify(seat, string.Format(LM.Get("terminal_vote_same"), percent));
                return;
            }
            if (_vote != null)
            {
                Notify(seat, LM.Get("terminal_vote_busy"));
                return;
            }
            if (VoteRest.TryGetValue(seat.Token, out float rest) && now < rest)
            {
                Notify(seat, LM.Get("terminal_vote_rest"));
                return;
            }
            _vote = new SpeedVote { Id = ++_voteSeq, Percent = percent, ByToken = seat.Token, ByName = VoterName(seat), Until = now + VoteSeconds };
            _vote.Ballots[seat.Token] = true;
            foreach (Seat other in Voters())
                Notify(other, other == seat ? string.Format(LM.Get("terminal_vote_started"), percent)
                    : string.Format(LM.Get("terminal_vote_called"), _vote.ByName, percent));
            TouchAll();
            SettleVote(false);
            return;
        }
        if (_vote == null || target != _vote.Id.ToString() || choice != "yes" && choice != "no")
        {
            Notify(seat, LM.Get("terminal_vote_over"));
            return;
        }
        _vote.Ballots[seat.Token] = choice == "yes";
        seat.Acks++;
        TouchAll();
        SettleVote(false);
    }

    // 每秒一回：到点了结；有人掉线后剩下的都表了态也了结
    private static void RunSpeedVote(float now)
    {
        if (_vote != null) SettleVote(now >= _vote.Until);
    }

    private static void SettleVote(bool timeUp)
    {
        List<Seat> voters = Voters();
        Tally(voters, out int yes, out int no);
        // 没人反对、还有人没表态、也没到点：接着等
        if (voters.Count > 0 && no == 0 && yes + no < voters.Count && !timeUp) return;
        SpeedVote vote = _vote;
        _vote = null;
        bool passed = voters.Count > 0 && yes == voters.Count;
        if (passed)
        {
            SpeedPercent = vote.Percent;
            try
            {
                // 同在设置里手改：存进设置，窗口里的数跟着变
                EmpireCraft.Scripts.UI.Windows.ModSettingsWindow.SetText("Terminal_Speed_Text", vote.Percent.ToString());
            }
            catch (global::System.Exception)
            {
                // 设置没存上：这一局照改后的走
            }
        }
        else VoteRest[vote.ByToken] = Time.realtimeSinceStartup + VoteRestSeconds;
        string text = passed ? string.Format(LM.Get("terminal_vote_passed"), vote.Percent)
            : string.Format(LM.Get("terminal_vote_failed"), SpeedPercent, yes, no, voters.Count);
        foreach (Seat seat in Seats())
            if (seat.Ruler != null) Notify(seat, text);
        TouchAll();
    }

    // 除名的席位：他那一票不算了
    private static void DropBallot(string token)
    {
        _vote?.Ballots.Remove(token);
    }

    // 拼进席位状态里的两项：可选的几档流速、眼下这桩议(没有为 null)
    private static void AddSpeed(JObject state, Seat seat)
    {
        state["speedChoices"] = new JArray(SpeedChoices);
        if (_vote == null)
        {
            state["speedVote"] = null;
            return;
        }
        List<Seat> voters = Voters();
        Tally(voters, out int yes, out int no);
        state["speedVote"] = new JObject
        {
            ["id"] = _vote.Id, ["percent"] = _vote.Percent, ["by"] = _vote.ByName, ["yes"] = yes, ["no"] = no, ["need"] = voters.Count,
            ["mine"] = _vote.Ballots.TryGetValue(seat.Token, out bool agree) ? agree ? "yes" : "no" : "",
            ["left"] = Mathf.Max(0, Mathf.CeilToInt(_vote.Until - Time.realtimeSinceStartup))
        };
    }

    // 终端窗口里的一行：眼下这桩议(没有为 null)
    public static string VoteLine()
    {
        if (_vote == null) return null;
        List<Seat> voters = Voters();
        Tally(voters, out int yes, out int no);
        return string.Format(LM.Get("terminal_vote_line"), _vote.ByName, _vote.Percent, yes, no, voters.Count);
    }
}
