using System.Collections.Generic;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

public class BugReportWindow : AutoLayoutWindow<BugReportWindow>
{
    private readonly List<GameObject> _groups = new();
    private SimpleText _status;

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (GameObject group in _groups)
        {
            if (group != null)
                Destroy(group);
        }

        _groups.Clear();

        AutoVertLayoutGroup panel = this.BeginVertGroup(
            new Vector2(196, 132),
            pSpacing: 4,
            pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(3, 3, 3, 3)
        );

        SimpleText recipient = panel.AddTextIntoVertLayout(
            LM.Get("bug_report_recipient") +
            BugReportService.Recipient,
            true,
            TextAnchor.MiddleCenter,
            new Vector2(188, 18)
        );

        recipient.UseFixedFontSize(
            8,
            HorizontalWrapMode.Overflow
        );

        SimpleText notice = panel.AddTextIntoVertLayout(
            LM.Get("bug_report_privacy_notice"),
            true,
            TextAnchor.MiddleLeft,
            new Vector2(188, 40)
        );

        notice.UseFixedFontSize(
            7,
            HorizontalWrapMode.Wrap
        );

        notice.RefreshAutoHeight(40, 4);

        _status = panel.AddTextIntoVertLayout(
            GetInitialStatus(),
            true,
            TextAnchor.MiddleCenter,
            new Vector2(188, 22)
        );

        _status.UseFixedFontSize(
            7,
            HorizontalWrapMode.Wrap
        );

        AutoHoriLayoutGroup buttons =
            panel.BeginHoriGroup(
                new Vector2(188, 22),
                TextAnchor.MiddleCenter,
                4
            );

        buttons.AddButtonIntoHoriLayout(
            "bug_report_send",
            LM.Get("bug_report_send"),
            SendReport,
            size: new Vector2(88, 20),
            showTip: true
        );

        buttons.AddButtonIntoHoriLayout(
            "bug_report_open_log",
            LM.Get("bug_report_open_log"),
            OpenLog,
            size: new Vector2(88, 20),
            showTip: true
        );

        panel.transform.AddStretchBackground(
            "regimeFrame",
            new Vector2(196, 132)
        );

        _groups.Add(panel.gameObject);
    }

    private string GetInitialStatus()
    {
        return global::System.IO.File.Exists(
            BugReportService.FindPlayerLog()
        )
            ? LM.Get("bug_report_log_found")
            : LM.Get("bug_report_log_missing");
    }

    private void SendReport()
    {
        BugReportSendResult result =
            BugReportService.Send();

        string key = result.Status switch
        {
            BugReportSendStatus.Sent =>
                "bug_report_sent",

            BugReportSendStatus.PlayerLogMissing =>
                "bug_report_log_missing",

            _ =>
                "bug_report_failed"
        };

        string text = LM.Get(key);

        if (
            result.Status ==
                BugReportSendStatus.Failed &&
            !string.IsNullOrWhiteSpace(result.Error)
        )
        {
            text += "\n" + result.Error;
        }

        SetStatus(text);
    }

    private void OpenLog()
    {
        SetStatus(
            BugReportService.OpenPlayerLogFolder()
                ? LM.Get("bug_report_log_opened")
                : LM.Get("bug_report_log_missing")
        );
    }

    private void SetStatus(string text)
    {
        if (_status == null)
            return;

        _status.text.text = text;
        _status.RefreshAutoHeight(22, 4);
    }
}
