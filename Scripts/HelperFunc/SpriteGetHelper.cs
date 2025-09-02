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
        public static Sprite _emperor_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_normal");
        public static Sprite _emperor_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_angry");
        public static Sprite _emperor_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_surprised");
        public static Sprite _emperor_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_emperor_happy");
        public static Sprite _emperor_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_empror_sad");

        public static Sprite _officer_sprite_normal = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_normal");
        public static Sprite _officer_sprite_angry = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_angry");
        public static Sprite _officer_sprite_surprised = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_surprised");
        public static Sprite _officer_sprite_happy = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_happy");
        public static Sprite _officer_sprite_sad = SpriteTextureLoader.getSprite("civ/icons/minimap_officer_sad");
    }
}
