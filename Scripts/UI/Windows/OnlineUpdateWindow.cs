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
    private SimpleText _manualModeHelp;
    private SimpleText _notes;
    private AdvancedButton _manualModeToggle;
    private AdvancedButton _downloadButton;
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
            new Vector2(202, 274),
            pSpacing: 4,
            pAlignment: TextAnchor.UpperCenter,
            pPadding: new RectOffset(4, 4, 4, 4));

        _version = _panel.AddTextIntoVertLayout("", true, TextAnchor.MiddleCenter, new Vector2(194, 22));
        _version.UseFixedFontSize(8, HorizontalWrapMode.Wrap, VerticalWrapMode.Truncate);
        SetFixedHeight(_version.gameObject, 22);

        _status = _panel.AddTextIntoVertLayout("", true, TextAnchor.MiddleCenter, new Vector2(194, 48));
        _status.UseFixedFontSize(8, HorizontalWrapMode.Wrap);
        SetFixedHeight(_status.gameObject, 48);
        HoverVerticalScrollText.Attach(_status);

        AutoHoriLayoutGroup manualMode = _panel.BeginHoriGroup(
            new Vector2(194, 20), TextAnchor.MiddleCenter, 2);
        SetFixedHeight(manualMode.gameObject, 20);
        manualMode.AddTextIntoHoriLayout(
            LM.Get("online_update_manual_mode"), true, TextAnchor.MiddleLeft, new Vector2(170, 18));
        _manualModeToggle = manualMode.AddButtonIntoHoriLayout(
            "online_update_manual_mode", "", ToggleManualPackageMode,
            size: new Vector2(18, 18), isToggle: true, showTip: true, iconType: 1,
            hideBackground: true);
        _manualModeToggle.SetStatus(EmpireCraftUpdateService.ManualPackageMode);

        _manualModeHelp = _panel.AddTextIntoVertLayout(
            LM.Get("online_update_manual_mode_explanation"), true,
            TextAnchor.UpperLeft, new Vector2(194, 48));
        _manualModeHelp.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        SetFixedHeight(_manualModeHelp.gameObject, 48);
        HoverVerticalScrollText.Attach(_manualModeHelp);

        _notes = _panel.AddTextIntoVertLayout("", true, TextAnchor.UpperLeft, new Vector2(194, 52));
        _notes.UseFixedFontSize(7, HorizontalWrapMode.Wrap);
        SetFixedHeight(_notes.gameObject, 52);
        HoverVerticalScrollText.Attach(_notes);

        AutoHoriLayoutGroup buttons = _panel.BeginHoriGroup(new Vector2(194, 24), TextAnchor.MiddleCenter, 4);
        SetFixedHeight(buttons.gameObject, 24);
        buttons.AddButtonIntoHoriLayout(
            "online_update_check",
            LM.Get("online_update_check"),
            EmpireCraftUpdateService.CheckForUpdates,
            size: new Vector2(92, 20),
            showTip: true);
        _downloadButton = buttons.AddButtonIntoHoriLayout(
            "online_update_download",
            GetDownloadButtonText(),
            EmpireCraftUpdateService.DownloadAndInstallOnExit,
            size: new Vector2(92, 20),
            showTip: true);

        AutoHoriLayoutGroup folderButton = _panel.BeginHoriGroup(
            new Vector2(194, 24), TextAnchor.MiddleCenter, 4);
        SetFixedHeight(folderButton.gameObject, 24);
        folderButton.AddButtonIntoHoriLayout(
            "online_update_open_mods",
            LM.Get("online_update_open_mods"),
            OpenModsFolder,
            size: new Vector2(188, 20),
            showTip: true);

        _panel.transform.AddStretchBackground("regimeFrame", new Vector2(202, 274));
    }

    private void OpenModsFolder()
    {
        if (_status == null) return;
        _status.text.text = LM.Get(EmpireCraftUpdateService.OpenModsFolder()
            ? "online_update_mods_opened"
            : "online_update_mods_open_failed");
    }

    private void ToggleManualPackageMode()
    {
        bool enabled = !EmpireCraftUpdateService.ManualPackageMode;
        EmpireCraftUpdateService.SetManualPackageMode(enabled);
        _manualModeToggle?.SetStatus(enabled);
        RefreshDownloadButton();
        Refresh(true);
    }

    private static string GetDownloadButtonText()
    {
        return LM.Get(EmpireCraftUpdateService.ManualPackageMode
            ? "online_update_download_zip"
            : "online_update_download");
    }

    private void RefreshDownloadButton()
    {
        if (_downloadButton == null) return;
        bool manual = EmpireCraftUpdateService.ManualPackageMode;
        string localeKey = manual ? "online_update_download_zip" : "online_update_download";
        if (_downloadButton.Text != null) _downloadButton.Text.text = LM.Get(localeKey);
        if (_downloadButton.TipButton != null)
        {
            _downloadButton.TipButton.textOnClick = localeKey;
            _downloadButton.TipButton.textOnClickDescription = localeKey + "_description";
        }
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
        _manualModeToggle?.SetStatus(EmpireCraftUpdateService.ManualPackageMode);
        RefreshDownloadButton();

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
            EmpireCraftUpdateStatus.InstallPending => LM.Get("online_update_install_pending"),
            EmpireCraftUpdateStatus.InstallSucceeded => LM.Get("online_update_install_succeeded"),
            EmpireCraftUpdateStatus.InstallFailed => string.Format(
                LM.Get("online_update_install_failed"), snapshot.LocalPackagePath ?? ""),
            EmpireCraftUpdateStatus.ManualPackageReady => string.Format(
                LM.Get("online_update_manual_package_ready"), snapshot.LocalPackagePath ?? ""),
            EmpireCraftUpdateStatus.ManualInstallRequired => string.Format(
                LM.Get("online_update_manual_install"), snapshot.LocalPackagePath ?? ""),
            EmpireCraftUpdateStatus.Failed => LM.Get("online_update_failed"),
            _ => LM.Get("online_update_idle")
        };

        if (!string.IsNullOrWhiteSpace(snapshot.Error) &&
            snapshot.Status is EmpireCraftUpdateStatus.Failed or EmpireCraftUpdateStatus.InstallFailed or
                EmpireCraftUpdateStatus.ManualInstallRequired)
            text += "\n" + snapshot.Error;
        return text;
    }
}
