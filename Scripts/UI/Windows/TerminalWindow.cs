using System.Collections.Generic;
using System.Linq;
using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Terminal;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;

namespace EmpireCraft.Scripts.UI.Windows;

// 终端(「卷册」宽窗口；模组设置左栏的「终端」按钮打开)：终端模式的电脑这一头，见 TerminalHost。
//   · 左栏：开没开、手机该填的地址与六位配对码 → 开关终端模式、换配对码；
//   · 右栏：主指标(席位几个、领着君主的几个、世界流速) → 席位卡(一部配过对的手机一行，点一行选中；选中的可以收回君主、除名)
//     → 在世君主卡(点一位，交给选中的席位；别的席位领着的标出来、点不动)。
// 手机上没有所领之君时自己也能挑；这里是离开电脑前先替它定好(用户 2026-11-11 "离开电脑前先选定一个政体")
public class TerminalWindow : WideScrollWindow<TerminalWindow>
{
    private const int MaxListed = 60;
    private string _seat;

    protected override void OnOpen() => _seat = null;

    // 配对码、席位、谁领着谁变了(见 TerminalHost.NotifyWindows)：开着就重画
    public static void RefreshIfOpen()
    {
        if (Instance != null && Instance.gameObject.activeInHierarchy) Instance.Refresh();
    }

    private TerminalHost.Seat Selected(List<TerminalHost.Seat> seats) =>
        seats.FirstOrDefault(seat => seat.Token == _seat) ?? seats.FirstOrDefault();

    protected override void DrawRail(AutoVertLayoutGroup rail)
    {
        bool on = TerminalHost.Enabled;
        string status = !on ? LM.Get("terminal_status_off")
            : TerminalHost.Running ? LM.Get(TerminalHost.Lan ? "terminal_status_lan" : "terminal_status_local")
            : string.IsNullOrEmpty(TerminalHost.Error) ? LM.Get("terminal_status_starting")
            : string.Format(LM.Get("terminal_status_failed"), TerminalHost.Port);
        rail.AddRailTitle(LM.Get("terminal_title"), status);

        float width = UIHelper.RailWidth - 8f;
        if (on && TerminalHost.Running)
        {
            var link = rail.BeginRailCard();
            int row = 0;
            foreach (string address in TerminalHost.Addresses().Take(3))
                link.AddRailRow(width, LM.Get("terminal_address"), address, WideKit.Cyan, row++);
            link.AddRailRow(width, LM.Get("terminal_code"), TerminalHost.Code, WideKit.Gold, row);
        }

        if (on)
        {
            // 外网地址：第三方隧道给的固定网址，填了二维码就用它(留空就是只在同一个网络里用)
            var outside = rail.BeginRailCard();
            // 框里显示补全后的整条地址(只填了 100.x.x.x 的，这里能看到补成了什么)；不成个地址的原样留着并说明
            outside.AddRailInput(LM.Get("terminal_public_url"), TerminalHost.PublicBad ? TerminalHost.PublicText : TerminalHost.PublicUrl, value =>
            {
                ModSettingsWindow.SetText("Terminal_Public_Url_Text", value ?? "");
                Refresh();
            });
            if (TerminalHost.PublicBad) RailNote(outside, LM.Get("terminal_public_url_bad").ColorString("#E8A04C"));
            else if (TerminalHost.PublicUrl.Length == 0) RailNote(outside, LM.Get("terminal_public_url_hint"));
            // 本机挂着组网工具：一点就拿它给的地址作外网地址，不用手抄
            string mesh = TerminalHost.MeshAddress();
            if (mesh != null && TerminalHost.PublicUrl != TerminalIntel.OutsideUrl(mesh, TerminalHost.Port))
                rail.AddRailButton("terminal_use_mesh", string.Format(LM.Get("terminal_use_mesh"), mesh), () =>
                {
                    ModSettingsWindow.SetText("Terminal_Public_Url_Text", mesh);
                    Refresh();
                });
        }
        // 联机大厅(见 TerminalHost.Lobby)：接入后别人在大厅网页上看得到这个房间，不用自己引端口
        bool joined = on && TerminalHost.LobbyEnabled;
        if (on)
        {
            var lobby = rail.BeginRailCard();
            // 走的哪条线路(设置里另填了大厅地址的写"自定")
            string line = LM.Get(TerminalHost.LobbyCustom ? "terminal_line_custom" : "terminal_line_" + TerminalHost.LineKey);
            string lobbyStatus = !joined ? LM.Get("terminal_lobby_off")
                : TerminalHost.LobbyOnline ? string.Format(LM.Get("terminal_lobby_online_line"), line)
                : string.IsNullOrEmpty(TerminalHost.LobbyError) ? LM.Get("terminal_lobby_connecting")
                : string.Format(LM.Get("terminal_lobby_failed"), TerminalHost.LobbyError);
            if (joined)
            {
                lobby.AddRailRow(width, LM.Get("terminal_lobby_title"), lobbyStatus, TerminalHost.LobbyOnline ? WideKit.Gold : WideKit.Cyan, 0);
                lobby.AddRailRow(width, LM.Get("terminal_room_mode"),
                    LM.Get(TerminalHost.RoomOpen ? "terminal_room_open" : "terminal_room_private"), WideKit.Cyan, 1);
                lobby.AddRailInput(LM.Get("terminal_room_name"), TerminalHost.RoomName, value =>
                {
                    ModSettingsWindow.SetText("Terminal_Room_Name_Text", value ?? "");
                    Refresh();
                });
            }
            // 左栏放不下长说明(用户 2026-11-11 截图"ui超出了")：接入后大厅网址、怎么进都在右边「手机连接」卡里；
            // 没接入时只留一句话，状态行也不占地方(下面的按钮写着"接入联机大厅")
            else RailNote(lobby, LM.Get("terminal_lobby_hint_off"));
        }
        var notes = rail.BeginRailCard();
        RailNote(notes, LM.Get(on ? "terminal_hint_on" : "terminal_hint_off"));

        rail.AddRailButton("terminal_toggle", LM.Get(on ? "terminal_turn_off" : "terminal_turn_on"), () =>
        {
            ModSettingsWindow.SetSwitch("terminal_mode_toggle", !on);
            Refresh();
        });
        // 接入、退出联机大厅(走哪条线路在右边「房间公开到哪条线路」那张卡里挑，接入后也能当场换)
        if (on && joined)
            rail.AddRailButton("terminal_lobby_toggle", LM.Get("terminal_lobby_leave"), () =>
            {
                ModSettingsWindow.SetSwitch("terminal_lobby_toggle", false);
                Refresh();
            });
        else if (on)
            rail.AddRailButton("terminal_lobby_toggle", LM.Get("terminal_lobby_join"), () =>
            {
                ModSettingsWindow.SetSwitch("terminal_lobby_toggle", true);
                Refresh();
            });
        if (joined)
            rail.AddRailButton("terminal_room_open_toggle", LM.Get(TerminalHost.RoomOpen ? "terminal_room_make_private" : "terminal_room_make_open"), () =>
            {
                ModSettingsWindow.SetSwitch("terminal_room_open_toggle", !TerminalHost.RoomOpen);
                Refresh();
            });
        if (on)
            rail.AddRailButton("terminal_new_code", LM.Get("terminal_new_code"), () =>
            {
                TerminalHost.NewCode();
                Refresh();
            });
        rail.AddRailButton("terminal_refresh", LM.Get("terminal_refresh"), () => Refresh());
    }

    // 「房间公开到哪条线路」(用户 2026-11-11 "终端也要切换路线的选项""选择把房间公开至哪个路线")：一条线路一行，
    // 点哪条房间就公开到哪条的大厅——还没接入的顺带接入，已经接着别的线路的当场换过去(连接自己断开重连，房间号不变)。
    // 设置里另填了大厅地址的只列那一条
    private void DrawLines(AutoVertLayoutGroup page)
    {
        var card = page.BeginCard(PageWidth, "terminal_card_lines", WideKit.Gold);
        card.AddParagraph(PageWidth, LM.Get("terminal_lines_note"), 6, WideKit.Muted);
        bool joined = TerminalHost.LobbyEnabled;
        if (TerminalHost.LobbyCustom)
        {
            card.AddToggleRow(PageWidth, 0, LM.Get("terminal_line_custom"), TerminalHost.LobbyWeb, joined,
                LM.Get(joined ? "terminal_line_here" : "terminal_line_pick"), () =>
                {
                    ModSettingsWindow.SetSwitch("terminal_lobby_toggle", true);
                    Refresh();
                });
            return;
        }
        for (int i = 0; i < TerminalHost.Lobbies.Length; i++)
        {
            (string key, string socket) = TerminalHost.Lobbies[i];
            bool here = joined && key == TerminalHost.LineKey;
            string state = !here ? "" : " · " + LM.Get(TerminalHost.LobbyOnline ? "terminal_lobby_online"
                : string.IsNullOrEmpty(TerminalHost.LobbyError) ? "terminal_lobby_connecting" : "terminal_lobby_retrying");
            card.AddToggleRow(PageWidth, i, LM.Get("terminal_line_" + key), TerminalHost.WebOf(socket) + state, here,
                LM.Get(here ? "terminal_line_here" : joined ? "terminal_line_switch" : "terminal_line_pick"), () =>
                {
                    if (here) return;
                    ModSettingsWindow.SetText("Terminal_Lobby_Line_Text", key);
                    ModSettingsWindow.SetSwitch("terminal_lobby_toggle", true);
                    Refresh();
                });
        }
    }

    private static void RailNote(AutoVertLayoutGroup card, string text) =>
        card.AddRailText(text.ColorString(WideKit.Muted), 6,
            Mathf.Ceil((UIHelper.EstimateTextWidth(text, 6) * 1.1f + 6f) / (UIHelper.RailWidth - 8f)) * 8f + 2f, wrap: true);

    protected override void DrawPage(AutoVertLayoutGroup page)
    {
        List<TerminalHost.Seat> seats = TerminalHost.Seats();
        TerminalHost.Seat selected = Selected(seats);
        int ruling = seats.Count(seat => seat.Ruler != null);
        page.AddPageHeader(PageWidth, LM.Get("terminal_title"), string.Format(LM.Get("terminal_seat_count"), seats.Count));
        page.AddKpiRow(PageWidth,
            (LM.Get("terminal_kpi_seats"), seats.Count + "/" + TerminalPairing.MaxSeats, WideKit.Cyan),
            (LM.Get("terminal_kpi_ruling"), ruling.ToString(), WideKit.Gold),
            (LM.Get("terminal_kpi_speed"), TerminalHost.Slowed ? TerminalHost.SpeedPercent + "%" : "100%", WideKit.Green));

        if (!TerminalHost.Enabled)
        {
            page.AddEmptyCard(PageWidth, LM.Get("terminal_empty_off"), LM.Get("terminal_hint_off"));
            return;
        }
        TerminalConnectCard.Draw(page, PageWidth);
        DrawLines(page);
        if (seats.Count == 0)
        {
            page.AddEmptyCard(PageWidth, LM.Get("terminal_empty_seats"), LM.Get("terminal_hint_on"));
            return;
        }

        var seatCard = page.BeginCard(PageWidth, "terminal_card_seats", WideKit.Gold);
        // 各席位正在议改流速(见 TerminalHost.Speed)
        string voting = TerminalHost.VoteLine();
        if (voting != null) seatCard.AddParagraph(PageWidth, voting, 7, WideKit.Orange);
        for (int i = 0; i < seats.Count; i++)
        {
            TerminalHost.Seat seat = seats[i];
            string who = seat.Ruler != null
                ? string.Format(LM.Get("terminal_seat_ruling"), seat.Realm?.data?.name ?? "", seat.Ruler.getName())
                : LM.Get("terminal_seat_vacant");
            string online = LM.Get(seat.Watched ? "terminal_seat_online" : "terminal_seat_away");
            seatCard.AddToggleRow(PageWidth, i, TerminalHost.SeatLabel(seat), who + " · " + online, seat == selected,
                LM.Get(seat == selected ? "terminal_seat_selected" : "terminal_seat_select"), () =>
                {
                    _seat = seat.Token;
                    Refresh();
                });
        }
        if (selected != null)
            seatCard.AddButtonRow(PageWidth,
                ("terminal_seat_recall", LM.Get("terminal_seat_recall"), () =>
                {
                    TerminalHost.Assign(selected, -1);
                    Refresh();
                }),
                ("terminal_seat_remove", LM.Get("terminal_seat_remove"), () =>
                {
                    TerminalHost.Remove(selected);
                    _seat = null;
                    Refresh();
                }));
        if (selected != null) seatCard.AddParagraph(PageWidth, LM.Get("terminal_seat_remove_hint"), 6, WideKit.Muted);

        List<TerminalHost.RulerInfo> rulers = TerminalHost.Rulers();
        if (rulers.Count == 0)
        {
            page.AddEmptyCard(PageWidth, LM.Get("terminal_empty_rulers"));
            return;
        }
        string firstWorld = string.IsNullOrEmpty(rulers[0].Tianxia) ? LM.Get("terminal_no_tianxia") : rulers[0].Tianxia;
        var rulerCard = page.BeginCard(PageWidth, firstWorld, WideKit.Gold,
            string.Format(LM.Get("terminal_ruler_for"), TerminalHost.SeatLabel(selected)));
        int line = 0;
        for (int i = 0; i < rulers.Count && i < MaxListed; i++)
        {
            TerminalHost.RulerInfo info = rulers[i];
            // 一个天下一张卡(名单已排成 天下 → 帝国或王国 → 行政区)；行政区的行缩进
            if (i > 0 && info.Tianxia != rulers[i - 1].Tianxia)
            {
                rulerCard = page.BeginCard(PageWidth, string.IsNullOrEmpty(info.Tianxia) ? LM.Get("terminal_no_tianxia") : info.Tianxia, WideKit.Gold);
                line = 0;
            }
            bool province = info.Kind == "member" || info.Kind == "official";
            bool mine = selected.RulerId == info.Id, taken = !mine && info.TakenBy != null;
            // 名单已按 天下 → 帝国或王国 → 行政区 排好(见 TerminalHost.Rulers)，行里写明归属
            string detail = string.Format(LM.Get("terminal_ruler_detail"), LM.Get("terminal_role_" + info.Kind), info.Cities, info.Age) +
                            (info.Kind == "member" && !string.IsNullOrEmpty(info.Empire) ? " · " + info.Empire : "") +
                            (string.IsNullOrEmpty(info.Tianxia) ? "" : " · " + info.Tianxia);
            string state = mine ? LM.Get("terminal_ruler_mine")
                : taken ? string.Format(LM.Get("terminal_ruler_taken"), info.TakenBy) : LM.Get("terminal_ruler_pick");
            rulerCard.AddToggleRow(PageWidth, line++, (province ? "└ " : "") + info.Realm + " · " + info.Ruler, detail, mine, state, () =>
            {
                if (taken) return;
                TerminalHost.Assign(selected, mine ? -1 : info.Id);
                Refresh();
            });
        }
    }
}
