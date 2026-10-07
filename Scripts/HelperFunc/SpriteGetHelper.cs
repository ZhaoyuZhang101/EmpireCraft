using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.HelperFunc
{
    public static class SpriteGetHelper
    {
        private static readonly Dictionary<string, Sprite> UiSprites = new();

        // 部分原版切片只挂在预制体上，没有 Resources 路径。按精确名称复用，避免空白背景。
        public static Sprite GetUiSprite(string path, string name)
        {
            Sprite sprite = SpriteTextureLoader.getSprite(path);
            if (sprite != null) return sprite;
            if (UiSprites.TryGetValue(name, out sprite) && sprite != null) return sprite;
            foreach (Sprite candidate in Resources.FindObjectsOfTypeAll<Sprite>())
                if (candidate.name == name) { UiSprites[name] = candidate; return candidate; }
            return null;
        }
        public static Sprite _emperor_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_normal");
        public static Sprite _emperor_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_angry");
        public static Sprite _emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_surprised");
        public static Sprite _emperor_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_happy");
        public static Sprite _emperor_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_emperor_sad");

        public static Sprite _officer_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_normal");
        public static Sprite _officer_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_angry");
        public static Sprite _officer_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_surprised");
        public static Sprite _officer_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_happy");
        public static Sprite _officer_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/LvLing/minimap_officer_sad");
    }
}
