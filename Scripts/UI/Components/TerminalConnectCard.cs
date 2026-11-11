using EmpireCraft.Scripts.GameClassExtensions;
using EmpireCraft.Scripts.Terminal;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

// 「手机连接」卡(用户 2026-11-11 进游戏开了终端模式后："开启后倒是告诉我手机怎么连啊，不行给我生成个二维码我扫描也成")：
// 终端模式一开，设置页和终端窗口里当场给出二维码、地址、配对码和连不上时先查什么。
// 二维码里是带配对码的整条地址，手机扫了直接进；配成一部手机后配对码会换，卡片跟着重画(见 TerminalHost.NotifyWindows)
public static class TerminalConnectCard
{
    private const float CodeSize = 96f;
    private const int Quiet = 4;
    private static Texture2D _texture;
    private static string _textureFor;

    public static void Draw(AutoVertLayoutGroup page, float width)
    {
        if (!TerminalHost.Enabled) return;
        TerminalHost.EnsureNow();
        var card = page.BeginCard(width, "terminal_card_connect", WideKit.Gold);
        if (!TerminalHost.Running)
        {
            card.AddParagraph(width, string.Format(LM.Get("terminal_connect_failed"), TerminalHost.Port), 7, WideKit.Orange);
            return;
        }
        if (!TerminalHost.Lan) card.AddParagraph(width, LM.Get("terminal_connect_local"), 7, WideKit.Orange);
        Texture2D code = Code(TerminalHost.PairUrl());
        if (code != null) AddCode(card.transform, code);
        card.AddParagraph(width, LM.Get("terminal_connect_scan"));
        card.AddValueRow(width, 0, LM.Get("terminal_address"), "http://" + TerminalHost.PrimaryAddress(), WideKit.Cyan);
        card.AddValueRow(width, 1, LM.Get("terminal_code"), TerminalHost.Code, WideKit.Gold);
        card.AddParagraph(width, LM.Get("terminal_connect_manual"), 6, WideKit.Muted);
        card.AddParagraph(width, LM.Get("terminal_connect_firewall"), 6, WideKit.Muted);
    }

    private static void AddCode(Transform parent, Texture2D code)
    {
        var holder = new GameObject("TerminalQr", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
        var rect = holder.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(CodeSize, CodeSize);
        LayoutElement element = holder.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = element.minHeight = element.preferredHeight = CodeSize;
        RawImage image = holder.GetComponent<RawImage>();
        image.texture = code;
        image.raycastTarget = false;
    }

    // 白底黑码，四周留四格白边；一格一个像素，点采样放大
    private static Texture2D Code(string url)
    {
        if (_texture != null && _textureFor == url) return _texture;
        bool[,] modules = TerminalQr.Encode(url);
        if (modules == null) return null;
        int size = modules.GetLength(0), side = size + Quiet * 2;
        var pixels = new Color32[side * side];
        var white = new Color32(255, 255, 255, 255);
        var black = new Color32(0, 0, 0, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = white;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            if (modules[y, x]) pixels[(side - 1 - (y + Quiet)) * side + x + Quiet] = black;
        if (_texture != null) Object.Destroy(_texture);
        _texture = new Texture2D(side, side, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _texture.SetPixels32(pixels);
        _texture.Apply(false);
        _textureFor = url;
        return _texture;
    }
}
