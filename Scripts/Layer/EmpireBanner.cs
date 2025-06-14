using EmpireCraft.Scripts.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace EmpireCraft.Scripts.Layer;

public class EmpireBanner : BannerGeneric<Empire, EmpireData>
{

    public override MetaType meta_type
    {
        get
        {
            return MetaType.None;
        }
    }


    public override void tooltipAction()
    {
        Tooltip.show(this, "empire", new TooltipData
        {
            nano_object = this.meta_object
        });
    }

    public override void setupBanner()
    {
        base.setupBanner();
        this.part_background.sprite = this.meta_object.getBackgroundSprite();
        this.part_icon.sprite = this.meta_object.getIconSprite();
        this.part_background.color = this.meta_object.getColor().getColorMain2();
        this.part_icon.color = this.meta_object.getColor().getColorBanner();
        this.part_frame.sprite = this.frame_forced;
    }

    // Token: 0x04003877 RID: 14455  
    public Sprite frame_normal;

    // Token: 0x04003878 RID: 14456  
    public Sprite frame_forced;
}

