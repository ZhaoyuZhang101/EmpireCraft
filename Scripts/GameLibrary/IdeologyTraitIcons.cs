using System;
using EmpireCraft.Scripts.GeneralSystems;
using UnityEngine;

namespace EmpireCraft.Scripts.GameLibrary;

// Small pixel badges are registered with the same sprite loader as the mod's PNG icons.
public static class IdeologyTraitIcons
{
    private const int Size = 24;
    private static readonly string[][] Marks =
    {
        new[] { "0011100", "0100010", "1010101", "1001001", "1010101", "0100010", "0011100" },
        new[] { "0001000", "0011100", "0111110", "1111111", "1110111", "0100010", "0011100" },
        new[] { "1000001", "1100011", "0110110", "0011100", "0110110", "1100011", "1000001" },
        new[] { "0001000", "0010100", "0100010", "1111111", "0100010", "0010100", "0001000" },
        new[] { "0011100", "0100010", "1001001", "1011101", "1001001", "0100010", "0011100" },
        new[] { "0011100", "0100010", "1111111", "0100010", "0110110", "0100010", "0011100" },
        new[] { "0001000", "1111111", "0101010", "0101010", "1000001", "0100010", "0011100" },
        new[] { "0011100", "0101010", "1111111", "1011101", "1111111", "0101010", "0011100" },
        new[] { "0001000", "0011100", "1111111", "0111110", "0011100", "0110110", "1100011" },
        new[] { "0011100", "0100010", "0101010", "0111110", "0101010", "0100010", "0011100" },
        new[] { "1111111", "1000001", "1011101", "1010101", "1010101", "1000001", "1111111" },
        new[] { "1000001", "1100011", "1110111", "1011101", "0111110", "0011100", "0001000" },
        new[] { "1111111", "0100010", "1111111", "0100010", "1111111", "0100010", "1111111" }
    };

    private static readonly Color32[] Colors =
    {
        new(216, 72, 86, 255), new(232, 125, 99, 255), new(77, 181, 159, 255),
        new(95, 178, 219, 255), new(231, 188, 73, 255), new(176, 198, 93, 255),
        new(218, 209, 159, 255), new(218, 104, 117, 255), new(197, 70, 80, 255),
        new(153, 192, 226, 255), new(147, 162, 200, 255), new(202, 170, 101, 255),
        new(179, 98, 106, 255)
    };

    public static string Path(PartyIdeology ideology) => $"ui/icons/actor_traits/ideology_{ideology}";

    public static void Register()
    {
        foreach (PartyIdeology ideology in Enum.GetValues(typeof(PartyIdeology)))
        {
            int index = (int)ideology;
            var pixels = new Color32[Size * Size];
            Color32 ink = Colors[index];
            Color32 shadow = new(29, 37, 42, 255);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int dx = x - 11;
                int dy = y - 11;
                int radius = dx * dx + dy * dy;
                pixels[y * Size + x] = radius <= 105 ? shadow : new Color32(0, 0, 0, 0);
                if (radius is >= 74 and <= 105) pixels[y * Size + x] = ink;
            }
            for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
            {
                if (Marks[index][y][x] != '1') continue;
                int px = 5 + x * 2;
                int py = 17 - y * 2;
                pixels[py * Size + px] = ink;
                pixels[py * Size + px + 1] = ink;
                pixels[(py - 1) * Size + px] = ink;
                pixels[(py - 1) * Size + px + 1] = ink;
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            texture.SetPixels32(pixels);
            texture.Apply();
            SpriteTextureLoader.addSprite(Path(ideology), ImageConversion.EncodeToPNG(texture));
            UnityEngine.Object.Destroy(texture);
        }
    }
}
