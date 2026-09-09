using EmpireCraft.Scripts.Diagnostics;
using EmpireCraft.Scripts.UI.Components;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Windows;

public class OnlineUpdateWindow : AutoLayoutWindow<OnlineUpdateWindow>
{
    private AutoVertLayoutGroup _panel;
    private SimpleText _version;
    private SimpleText _status;
    private SimpleText _notes;
    private long _lastRevision = -1;
    private float _nextRefresh;

    protected override void Init()
    {
        layout.spacing = 4;
        layout.padding = new RectOffset(4, 4, 6, 4);
    }

    public override void OnNormalEnable()
    {
        base.OnNormalEnable();
        Build();
        Refresh(true);
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        Refresh(false);
    }

    private void Build()
    {
        if (_panel != null) Destroy(_panel.gameObject);

        _panel = this.BeginVertGroup(
            new Vector2(202, 180),
            pSpacing: 4,
            pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(4, 4, 4, 4));

        _version = _panel.AddTextIntoVertLayout("", true, TextAnchor.MiddleCenter, new Vector2(194, 22));
        _version.UseFixedFontSize(8, HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate);
        SetFixedHeight(_version.gameObject, 22);

        _status = _panel.AddTextIntoVertLayout("", true, TextAnchor.MiddleCenter, new Vector2(194, 42));
        _status.UseFixedFontSize(8, HorizontalWrapMode.Wrap);
        SetFixedHeight(_status.gameObject, 42);
        HoverVerticalScrollText.Attach(_status);

        _notes = _panel.AddTextIntoVertLayout("", true, TextAnchor.UpperLeft, new Vector2(194, 70));
        _notes.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        SetFixedHeight(_notes.gameObject, 70);
        HoverVerticalScrollText.Attach(_notes);

        AutoHoriLayoutGroup buttons = _panel.BeginHoriGroup(new Vector2(194, 24), TextAnchor.MiddleCenter, 4);
        SetFixedHeight(buttons.gameObject, 24);
        buttons.AddButtonIntoHoriLayout(
            "online_update_check",
            LM.Get("online_update_check"),
            EmpireCraftUpdateService.CheckForUpdates,
            size: new Vector2(92, 20),
            showTip: true);
        buttons.AddButtonIntoHoriLayout(
            "online_update_download",
            LM.Get("online_update_download"),
            EmpireCraftUpdateService.DownloadAndInstallOnExit,
            size: new Vector2(92, 20),
            showTip: true);

        _panel.transform.AddStretchBackground("regimeFrame", new Vector2(202, 180));
    }

    private void Refresh(bool force)
    {
        if (_version == null || _status == null || _notes == null) return;
        EmpireCraftUpdateSnapshot snapshot = EmpireCraftUpdateService.GetSnapshot();
        if (!force && snapshot.Revision == _lastRevision) return;
        _lastRevision = snapshot.Revision;

        _version.text.text = string.Format(
            LM.Get("online_update_versions"),
            snapshot.CurrentVersion ?? LM.Get("label_none"),
            snapshot.AvailableVersion ?? LM.Get("label_none"));
        _status.text.text = BuildStatus(snapshot);

        string notes = string.IsNullOrWhiteSpace(snapshot.ReleaseNotes)
            ? LM.Get("online_update_no_notes")
            : snapshot.ReleaseNotes;
        _notes.text.text = LM.Get("online_update_notes") + "\n" + notes;
    }

    private static void SetFixedHeight(GameObject target, float height)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        LayoutElement layoutElement = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        layoutElement.minHeight = height;
        layoutElement.preferredHeight = height;
        layoutElement.flexibleHeight = 0f;
    }

    private static string BuildStatus(EmpireCraftUpdateSnapshot snapshot)
    {
        string text = snapshot.Status switch
        {
            EmpireCraftUpdateStatus.Checking => LM.Get("online_update_checking"),
            EmpireCraftUpdateStatus.UpToDate => LM.Get("online_update_up_to_date"),
            EmpireCraftUpdateStatus.UpdateAvailable => LM.Get("online_update_available"),
            EmpireCraftUpdateStatus.Downloading => string.Format(LM.Get("online_update_downloading"), snapshot.ProgressPercent),
            EmpireCraftUpdateStatus.InstallScheduled => LM.Get("online_update_install_scheduled"),
            EmpireCraftUpdateStatus.ManualInstallRequired => string.Format(
                LM.Get("online_update_manual_install"), snapshot.LocalPackagePath ?? ""),
            EmpireCraftUpdateStatus.Failed => LM.Get("online_update_failed"),
            _ => LM.Get("online_update_idle")
        };

        if (!string.IsNullOrWhiteSpace(snapshot.Error) &&
            snapshot.Status is EmpireCraftUpdateStatus.Failed or EmpireCraftUpdateStatus.ManualInstallRequired)
            text += "\n" + snapshot.Error;
        return text;
    }
}
