# -*- coding: utf-8 -*-
"""手机终端网页用的图标：从模组现成的图标里挑一套，原样(不缩放)编成 data URI，写进 Terminal/index.html 的
/*ICONS-BEGIN*/ … /*ICONS-END*/ 之间。内嵌而不另开文件：游戏里的终端是旧版、走外网线路时也照样显示。

用法(在模组根目录)：python Tools/IconForge/terminal_page_icons.py
换图标：改下面的 ICONS(网页里用的名字 → GameResources 下的文件)，再跑一遍。"""
import base64, io, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, 'GameResources')
PAGE = os.path.join(ROOT, 'Terminal', 'index.html')
BEGIN, END = '/*ICONS-BEGIN*/', '/*ICONS-END*/'

ICONS = {
    # 底下的页签
    'tab_reports': 'TabDiet.png',
    'tab_map': 'ui/icons/godpowers/empire_layer.png',
    'tab_realm': 'TabEmpire.png',
    'tab_decrees': 'TabConstitution.png',
    'tab_annals': 'TabDynasty.png',
    # 政令各组
    'g_court': 'TabClaimScale.png',
    'g_staff': 'TabBureau.png',
    'g_palace': 'ChineseCrown.png',
    'g_home': 'TabBuild.png',
    'g_law': 'TabSentencing.png',
    'g_spy': 'ui/virtualPersonIcon.png',
    'g_diplomacy': 'ui/icons/godpowers/diplomacy.png',
    'g_vassal': 'TabDependencies.png',
    'g_orders': 'ui/iconHistory.png',
    'g_pending': 'EmperorQuest.png',
    # 宗庙与史册
    'a_lineage': 'TabDynasty.png',
    'a_clan': 'ui/specificClanIcon.png',
    'a_history': 'ui/iconHistory.png',
    'a_dejure': 'ui/icons/iconTitleLayer.png',
    'a_name': 'ui/icons/godpowers/territory_font.png',
    # 指标与行首
    'k_treasury': 'ui/icons/godpowers/wealth_layer.png',
    'k_army': 'TabMilitary.png',
    'k_people': 'ui/icons/empirelist/stat_population.png',
    'k_cities': 'ui/icons/empirelist/stat_cities.png',
    'k_years': 'ui/icons/empirelist/stat_years.png',
    'k_mandate': 'ui/icons/empirelist/stat_mandate.png',
    'k_war': 'TabWarfare.png',
    'k_officials': 'ui/icons/godpowers/governance_layer.png',
    'k_new': 'ui/icons/godpowers/zhou_advance.png',
    'k_trust': 'ui/realNumToggle.png',
    'i_crown': 'crown2.png',
    'i_dead': 'ui/deadIcon.png',
    'i_lock': 'ui/icons/iconLock.png',
    'i_ok': 'ui/iconInstitutionEnacted.png',
    'i_city': 'ui/icons/godpowers/city_view.png',
    'i_capital': 'ui/icons/godpowers/build_palace.png',
    'i_title': 'TitleAcquire.png',
    # 舆图上军队的牌子：各兵种
    'arm_infantry': 'ui/icons/corps/arm_infantry.png',
    'arm_archer': 'ui/icons/corps/arm_archer.png',
    'arm_chariot': 'ui/icons/corps/arm_chariot.png',
    'arm_cavalry': 'ui/icons/corps/arm_cavalry.png',
    'arm_musket': 'ui/icons/corps/arm_musket.png',
    'arm_rifle': 'ui/icons/corps/arm_rifle.png',
    'arm_artillery': 'ui/icons/corps/arm_artillery.png',
    'arm_mechanized': 'ui/icons/corps/arm_mechanized.png',
    'arm_tank': 'ui/icons/corps/arm_tank.png',
    'arm_helicopter': 'ui/icons/corps/arm_helicopter.png',
    'arm_fighter': 'ui/icons/corps/arm_fighter.png',
    'arm_bomber': 'ui/icons/corps/arm_bomber.png',
    'arm_militia': 'ui/icons/corps/arm_militia.png',
    'arm_heavyartillery': 'ui/icons/corps/arm_heavyartillery.png',
    'i_title_main': 'TitleCreate.png',
    # 宫廷、内政里的各件事
    'i_campaign': 'ui/icons/godpowers/corps_command.png',
    'i_return': 'ui/icons/plots/plot_empire_move_back_to_capital.png',
    'i_tour': 'ui/icons/godpowers/city_view.png',
    'i_era': 'ui/icons/plots/plot_emperor_year_name.png',
    'i_heir': 'ui/icons/plots/plot_empress_dowager_install_son.png',
    'i_abdicate': 'ui/icons/plots/plot_usurp_imperial_legitimacy.png',
    'i_empire': 'ui/icons/godpowers/create_empire.png',
    'i_rename_realm': 'ui/icons/godpowers/territory_font.png',
    'i_rename_ruler': 'ui/icons/plots/plot_emperor_posthumous_name.png',
    'i_purge': 'ui/icons/plots/plot_kingdom_expose_crime.png',
    'i_recruit': 'ui/icons/godpowers/population_add.png',
    'i_road': 'ui/icons/godpowers/road_paint.png',
    'i_chief': 'ui/setOfficer.png',
    'i_office': 'ui/changeOfficer.png',
    'i_seal': 'TabDiet.png',
    # 舆图右边的层级四档与图例
    'ly_world': 'ui/icons/godpowers/tianxia_layer.png',
    'ly_empire': 'ui/icons/godpowers/empire_layer.png',
    'ly_realm': 'ui/icons/godpowers/realm_culture_kingdom.png',
    'ly_city': 'ui/icons/godpowers/city_view.png',
    'ly_legend': 'TabColor.png',
    # 状态条、加载界面
    'h_ruler': 'ui/icons/empirelist/head_monarch.png',
    'l_terrain': 'ui/icons/iconTileLoessPlateau.png',
    # 图上点中东西后能办的事
    'm_explore': 'ui/icons/plots/plot_develop_wilderness.png',
    'm_chart': 'ui/icons/godpowers/market_layer.png',
    'm_vassal': 'ui/icons/godpowers/force_vassal.png',
    'm_release': 'ui/iconRemove.png',
    # 外交里的各件事
    'd_declare': 'TabWarfare.png',
    'd_peace': 'ui/icons/plots/plot_force_stop_war.png',
    'd_ally': 'ui/icons/godpowers/zhou_conference.png',
    'd_leave_alliance': 'ui/icons/godpowers/release_vassal.png',
    'd_marry': 'ui/specificClanIcon.png',
    'd_pact': 'ui/icons/plots/plot_empirecraft_road_treaty.png',
    'd_opinion_up': 'ui/icons/godpowers/diplomacy.png',
    'd_trade': 'TabClaimScale.png',
    # 天平上的条件种类
    'tk_money': 'ui/icons/godpowers/wealth_layer.png',
    'tk_city': 'ui/icons/empirelist/stat_cities.png',
    'tk_vassal': 'TabDependencies.png',
    'tk_title': 'TitleAcquire.png',
    'tk_corps': 'TabMilitary.png',
}

# 模组里没有的几个操作符号(全屏、退出全屏、放大、缩小、全览、关)：照像素图标的画法现画——象牙白的笔画，四周一圈深色描边
DRAWN = {
    'c_full': ['####..####', '##......##', '#.#....#.#', '#..#..#..#', '..........', '..........', '#..#..#..#', '#.#....#.#', '##......##', '####..####'],
    'c_unfull': ['#..#..#..#', '.#.#..#.#.', '..##..##..', '####..####', '..........', '..........', '####..####', '..##..##..', '.#.#..#.#.', '#..#..#..#'],
    'c_in': ['....##....', '....##....', '....##....', '....##....', '##########', '##########', '....##....', '....##....', '....##....', '....##....'],
    'c_out': ['..........', '..........', '..........', '..........', '##########', '##########', '..........', '..........', '..........', '..........'],
    'c_fit': ['###....###', '#........#', '#........#', '..........', '....##....', '....##....', '..........', '#........#', '#........#', '###....###'],
    'c_close': ['##......##', '###....###', '.###..###.', '..######..', '...####...', '...####...', '..######..', '.###..###.', '###....###', '##......##'],
}
INK, EDGE = (242, 238, 226, 255), (20, 22, 15, 255)


def drawn(rows):
    from PIL import Image
    size = len(rows) + 2
    img = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    lit = {(x + 1, y + 1) for y, row in enumerate(rows) for x, ch in enumerate(row) if ch == '#'}
    for x, y in lit:
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                if (x + dx, y + dy) not in lit and 0 <= x + dx < size and 0 <= y + dy < size:
                    img.putpixel((x + dx, y + dy), EDGE)
    for x, y in lit:
        img.putpixel((x, y), INK)
    out = io.BytesIO()
    img.save(out, 'PNG', optimize=True)
    return out.getvalue()


def main():
    parts = []
    total = 0
    for name, rel in ICONS.items():
        path = os.path.join(RES, rel.replace('/', os.sep))
        if not os.path.exists(path):
            raise SystemExit('missing ' + rel)
        data = open(path, 'rb').read()
        total += len(data)
        parts.append('%s:"data:image/png;base64,%s"' % (name, base64.b64encode(data).decode('ascii')))
    for name, rows in DRAWN.items():
        data = drawn(rows)
        total += len(data)
        parts.append('%s:"data:image/png;base64,%s"' % (name, base64.b64encode(data).decode('ascii')))
    text = io.open(PAGE, encoding='utf-8', newline='').read()
    if BEGIN not in text or END not in text:
        raise SystemExit('markers not found in index.html')
    head, rest = text.split(BEGIN, 1)
    tail = rest.split(END, 1)[1]
    text = head + BEGIN + 'var ICON={' + ','.join(parts) + '};' + END + tail
    io.open(PAGE, 'w', encoding='utf-8', newline='').write(text)
    print('%d icons, %d bytes of png' % (len(parts), total))


if __name__ == '__main__':
    main()
