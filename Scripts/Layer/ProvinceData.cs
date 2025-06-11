using EmpireCraft.Scripts.Enums;
using System;
using System.Collections.Generic;
using System.ComponentModel;

// Token: 0x0200023D RID: 573
public class ProvinceData : MetaObjectData
{
    // Token: 0x170000D7 RID: 215
    // (get) Token: 0x06001148 RID: 4424 RVA: 0x000C8189 File Offset: 0x000C6389
    // (set) Token: 0x06001149 RID: 4425 RVA: 0x000C8191 File Offset: 0x000C6391
    public string motto { get; set; }

    [DefaultValue(-1L)]
    public long leaderID { get; set; } = -1L;

    [DefaultValue(-1L)]
    public long last_leader_id { get; set; } = -1L;

    // Token: 0x170000D8 RID: 216
    // (get) Token: 0x0600114A RID: 4426 RVA: 0x000C819A File Offset: 0x000C639A
    // (set) Token: 0x0600114B RID: 4427 RVA: 0x000C81A2 File Offset: 0x000C63A2
    public int banner_background_id { get; set; }

    // Token: 0x170000D9 RID: 217
    // (get) Token: 0x0600114C RID: 4428 RVA: 0x000C81AB File Offset: 0x000C63AB
    // (set) Token: 0x0600114D RID: 4429 RVA: 0x000C81B3 File Offset: 0x000C63B3
    public int banner_icon_id { get; set; }

    // Token: 0x170000DA RID: 218
    // (get) Token: 0x0600114E RID: 4430 RVA: 0x000C81BC File Offset: 0x000C63BC
    // (set) Token: 0x0600114F RID: 4431 RVA: 0x000C81C4 File Offset: 0x000C63C4
    public string founder_actor_name { get; set; }

    // Token: 0x170000DB RID: 219
    // (get) Token: 0x06001150 RID: 4432 RVA: 0x000C81CD File Offset: 0x000C63CD
    // (set) Token: 0x06001151 RID: 4433 RVA: 0x000C81D5 File Offset: 0x000C63D5
    [DefaultValue(-1L)]
    public long founder_actor_id { get; set; } = -1L;

    // Token: 0x170000DC RID: 220
    // (get) Token: 0x06001152 RID: 4434 RVA: 0x000C81DE File Offset: 0x000C63DE
    // (set) Token: 0x06001153 RID: 4435 RVA: 0x000C81E6 File Offset: 0x000C63E6
    public string founder_kingdom_name { get; set; }

    // Token: 0x170000DD RID: 221
    // (get) Token: 0x06001154 RID: 4436 RVA: 0x000C81EF File Offset: 0x000C63EF
    // (set) Token: 0x06001155 RID: 4437 RVA: 0x000C81F7 File Offset: 0x000C63F7
    [DefaultValue(-1L)]
    public long founder_kingdom_id { get; set; } = -1L;

    // Token: 0x04002305 RID: 8965
    public List<long> cities;

    // Token: 0x0400230A RID: 8970
    public double timestamp_member_joined;

    // Token: 0x0400230B RID: 8971
    [DefaultValue(provinceLevel.provincelevel_duke)]
    public provinceLevel province_level;

    [DefaultValue(3)]
    public int province_level_limitation = 3;
}
