using System;
using System.Collections.Generic;
using UnityEngine;

// Token: 0x020000C5 RID: 197
public class EmpireBannerLibrary : GenericBannerLibrary
{
    // Token: 0x06000640 RID: 1600 RVA: 0x00052F48 File Offset: 0x00051148
    public override void init()
    {
        base.init();
    }

    // Token: 0x06000641 RID: 1601 RVA: 0x00052F50 File Offset: 0x00051150
    public override BannerAsset get(string pID)
    {
        if (this.dict.ContainsKey(pID))
        {
            return base.get(pID);
        }
        this.loadNewAssetRuntime(pID);
        return base.get(pID);
    }

    // Token: 0x06000642 RID: 1602 RVA: 0x00052F77 File Offset: 0x00051177
    public static string getFullPathBackground(string pID)
    {
        return PATH_BANNER_KINGDOMS + pID + PATH_BACKGROUND;
    }

    // Token: 0x06000643 RID: 1603 RVA: 0x00052F89 File Offset: 0x00051189
    public static string getFullPathIcon(string pID)
    {
        return PATH_BANNER_KINGDOMS + pID + PATH_ICON;
    }

    // Token: 0x06000644 RID: 1604 RVA: 0x00052F9C File Offset: 0x0005119C
    public BannerAsset loadNewAssetRuntime(string pID)
    {
        string fullPathBackground = EmpireBannerLibrary.getFullPathBackground(pID);
        string fullPathIcon = EmpireBannerLibrary.getFullPathIcon(pID);
        Sprite[] spriteList = SpriteTextureLoader.getSpriteList(fullPathBackground, false);
        Sprite[] spriteList2 = SpriteTextureLoader.getSpriteList(fullPathIcon, false);
        List<string> list = new List<string>();
        List<string> list2 = new List<string>();
        foreach (Sprite sprite in spriteList)
        {
            string item = fullPathBackground + "/" + sprite.name;
            list.Add(item);
        }
        foreach (Sprite sprite2 in spriteList2)
        {
            string item2 = fullPathIcon + "/" + sprite2.name;
            list2.Add(item2);
        }
        BannerAsset bannerAsset = new BannerAsset
        {
            id = pID,
            backgrounds = list,
            icons = list2
        };
        this.add(bannerAsset);
        return bannerAsset;
    }

    // Token: 0x0400063F RID: 1599
    public const string PATH_BANNER_KINGDOMS = "banners_kingdoms/";

    // Token: 0x04000640 RID: 1600
    public const string PATH_BACKGROUND = "/background";

    // Token: 0x04000641 RID: 1601
    public const string PATH_ICON = "/icon";
}
