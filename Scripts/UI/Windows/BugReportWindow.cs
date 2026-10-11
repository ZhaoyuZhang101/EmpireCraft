using System.Collections.Generic;
using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
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
    private AdvancedButton _saveDataToggle;
    private TextInput _description;
    private bool _includeSaveData;
    private long _lastSendRevision = -1;
    private float _nextStatusRefresh;

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Rebuild();
        RefreshSendStatus(true);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextStatusRefresh)
            return;

        _nextStatusRefresh = Time.unscaledTime + 0.25f;
        RefreshSendStatus(false);
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
            new Vector2(196, 234),
            pSpacing: 4,
            pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(3, 3, 3, 3)
        );

        SimpleText recipient = panel.AddTextIntoVertLayout(
            LM.Get("bug_report_recipient") +
            BugReportService.GetModAuthor(),
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
            new Vector2(188, 44)
        );

        notice.UseFixedFontSize(
            7,
            HorizontalWrapMode.Wrap
        );

        notice.RefreshAutoHeight(44, 4);

        _status = panel.AddTextIntoVertLayout(
            GetInitialStatus(),
            true,
            TextAnchor.MiddleCenter,
            new Vector2(188, 28)
        );

        _status.UseFixedFontSize(
            7,
            HorizontalWrapMode.Wrap
        );

        AutoHoriLayoutGroup saveDataOption =
            panel.BeginHoriGroup(
                new Vector2(188, 18),
                TextAnchor.MiddleCenter,
                4
            );

        _saveDataToggle = panel.transform.AddNormalOptionIntoHori(
            saveDataOption,
            "bug_report_include_save_data",
            ToggleSaveData,
            _includeSaveData,
            size: new Vector2(12, 12)
        );

        // 问题描述：多行输入，发送时写进报告摘要的开头(重建窗口时保留已写的内容)
        string draft = _description != null ? _description.input.text : "";
        _description = Instantiate(TextInput.Prefab, panel.transform);
        _description.Setup("", _ => { });
        _description.SetSize(new Vector2(184, 44));
        _description.input.GetComponent<RectTransform>().sizeDelta = new Vector2(184, 44);
        _description.input.lineType = InputField.LineType.MultiLineNewline;
        _description.input.characterLimit = BugReportService.MaximumDescriptionLength;
        _description.input.textComponent.alignment = TextAnchor.UpperLeft;
        _description.input.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
        _description.input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
        if (_description.input.placeholder == null)
            _description.input.SetupPlaceholder(_description.text.font, LM.Get("bug_report_description_placeholder"),
                new Color(1f, 1f, 1f, 0.45f));
        _description.input.text = draft;
        panel.AddChild(_description.gameObject);

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
            size: new Vector2(180, 20),
            showTip: true
        );

        AutoHoriLayoutGroup fileButtons =
            panel.BeginHoriGroup(
                new Vector2(188, 22),
                TextAnchor.MiddleCenter,
                4
            );

        fileButtons.AddButtonIntoHoriLayout(
            "bug_report_open_log",
            LM.Get("bug_report_open_log"),
            OpenLog,
            size: new Vector2(88, 20),
            showTip: true
        );

        fileButtons.AddButtonIntoHoriLayout(
            "bug_report_open_save",
            LM.Get("bug_report_open_save"),
            OpenSave,
            size: new Vector2(88, 20),
            showTip: true
        );

        panel.transform.AddStretchBackground(
            "regimeFrame",
            new Vector2(196, 234)
        );

        _groups.Add(panel.gameObject);
    }

    private string GetInitialStatus()
    {
        string logStatus = global::System.IO.File.Exists(
            BugReportService.FindPlayerLog()
        )
            ? LM.Get("bug_report_log_found")
            : LM.Get("bug_report_log_missing");

        string saveStatus;
        if (!_includeSaveData)
        {
            saveStatus = LM.Get("bug_report_save_disabled");
        }
        else
        {
            saveStatus = global::System.IO.File.Exists(
                BugReportService.FindEmpireCraftSaveData()
            )
                ? LM.Get("bug_report_save_found")
                : LM.Get("bug_report_save_missing");
        }

        return logStatus + "\n" + saveStatus;
    }

    private void ToggleSaveData()
    {
        _includeSaveData = !_includeSaveData;
        _saveDataToggle?.SetStatus(_includeSaveData);
        RefreshSendStatus(true);
    }

    private void SendReport()
    {
        BugReportSendResult result =
            BugReportService.BeginSend(_includeSaveData, _description?.input.text ?? "");

        ApplySendResult(result);
    }

    private void RefreshSendStatus(bool force)
    {
        BugReportSendResult result =
            BugReportService.GetSendSnapshot();

        if (!force && result.Revision == _lastSendRevision)
            return;

        _lastSendRevision = result.Revision;
        ApplySendResult(result);
    }

    private void ApplySendResult(BugReportSendResult result)
    {
        if (result == null || result.Status == BugReportSendStatus.Idle)
        {
            SetStatus(GetInitialStatus());
            return;
        }

        string key = result.Status switch
        {
            BugReportSendStatus.Sending =>
                "bug_report_sending",

            BugReportSendStatus.Sent when result.SaveDataIncluded =>
                "bug_report_sent_with_save",

            BugReportSendStatus.Sent =>
                "bug_report_sent",

            BugReportSendStatus.PlayerLogMissing =>
                "bug_report_log_missing",

            BugReportSendStatus.SaveDataMissing =>
                "bug_report_save_required_missing",

            BugReportSendStatus.PayloadTooLarge =>
                "bug_report_too_large",

            _ =>
                "bug_report_failed"
        };

        string text = LM.Get(key);

        if (result.Status == BugReportSendStatus.Sent &&
            !string.IsNullOrWhiteSpace(result.ReportId))
        {
            text += "\n" + string.Format(
                LM.Get("bug_report_report_id"),
                result.ReportId
            );
        }

        if (
            (result.Status == BugReportSendStatus.Failed ||
             result.Status == BugReportSendStatus.PayloadTooLarge) &&
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

    private void OpenSave()
    {
        SetStatus(
            BugReportService.OpenEmpireCraftSaveFolder()
                ? LM.Get("bug_report_save_opened")
                : LM.Get("bug_report_save_missing")
        );
    }

    private void SetStatus(string text)
    {
        if (_status == null)
            return;

        _status.text.text = text;
        _status.RefreshAutoHeight(28, 4);
    }
}
