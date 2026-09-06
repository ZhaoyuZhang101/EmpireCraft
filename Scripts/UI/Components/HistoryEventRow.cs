using System;
using System.Collections.Generic;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace EmpireCraft.Scripts.UI.Components;

public sealed class HistoryEventRowMarker
{
    public readonly float Width;
    public readonly Action<AutoHoriLayoutGroup> Build;

    public HistoryEventRowMarker(float width, Action<AutoHoriLayoutGroup> build)
    {
        Width = width;
        Build = build;
    }
}

public static class HistoryEventRow
{
    private const float CardWidth = 196f;
    private const float CardHeight = 28f;
    private const float DateWidth = 38f;
    private const float ItemSpacing = 2f;
    private const float RightPadding = 4f;

    public static AutoHoriLayoutGroup Add(AutoVertLayoutGroup parent, string date, string content,
        IReadOnlyList<HistoryEventRowMarker> markers = null, string secondaryContent = null)
    {
        var validMarkers = new List<HistoryEventRowMarker>();
        if (markers != null)
        {
            foreach (HistoryEventRowMarker marker in markers)
            {
                if (marker?.Build != null) validMarkers.Add(marker);
            }
        }

        float markerWidth = 0f;
        foreach (HistoryEventRowMarker marker in validMarkers) markerWidth += Mathf.Max(0f, marker.Width);
        int childCount = 2 + validMarkers.Count;
        float contentWidth = Mathf.Max(24f,
            CardWidth - DateWidth - markerWidth - ItemSpacing * (childCount - 1) - RightPadding);

        AutoHoriLayoutGroup row = parent.BeginHoriGroup(pSpacing: ItemSpacing,
            pAlignment: TextAnchor.MiddleLeft, pSize: new Vector2(CardWidth, CardHeight));
        SimpleText dateText = row.AddTextIntoHoriLayout(date ?? "", true, TextAnchor.MiddleCenter,
            new Vector2(DateWidth, 22f));
        dateText.UseFixedFontSize(8, HorizontalWrapMode.Overflow);

        if (secondaryContent == null)
        {
            AddMarquee(row.AddTextIntoHoriLayout(Normalize(content), true, TextAnchor.MiddleLeft,
                new Vector2(contentWidth, 22f)), 8);
        }
        else
        {
            AutoVertLayoutGroup details = row.BeginVertGroup(new Vector2(contentWidth, 24f), pSpacing: -2,
                pAlignment: TextAnchor.MiddleLeft, pPadding: new RectOffset(0, 0, 0, 0));
            AddMarquee(details.AddTextIntoVertLayout(Normalize(content), true, TextAnchor.MiddleLeft,
                new Vector2(contentWidth, 11f)), 7);
            AddMarquee(details.AddTextIntoVertLayout(Normalize(secondaryContent), true, TextAnchor.MiddleLeft,
                new Vector2(contentWidth, 11f)), 7);
        }

        foreach (HistoryEventRowMarker marker in validMarkers) marker.Build(row);
        row.transform.AddStretchBackground("clanFrame", new Vector2(CardWidth, CardHeight));
        return row;
    }

    private static void AddMarquee(SimpleText text, int fontSize)
    {
        text.UseFixedFontSize(fontSize, HorizontalWrapMode.Overflow);
        HoverMarqueeText.Attach(text);
    }

    private static string Normalize(string content)
    {
        // Old peerage saves could contain an empty rich-text color tag.
        return (content ?? "").Replace("<color=>", "<color=#FFFFFFFF>");
    }
}
