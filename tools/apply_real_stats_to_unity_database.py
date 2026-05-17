import sqlite3
import csv
import argparse
import shutil
import re
import datetime
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"
CSV_PATH = PROJECT_ROOT / "data" / "source" / "champion_player_stats.csv"

DEFENSE_BOOSTS = {
    "Jrue Holiday": {"perimeter_defense": 10, "defensive_consistency": 8},
    "Derrick White": {"perimeter_defense": 8, "block": 5},
    "Alex Caruso": {"perimeter_defense": 10, "steal": 8},
    "Luguentz Dort": {"perimeter_defense": 10, "strength": 5},
    "Draymond Green": {"interior_defense": 10, "perimeter_defense": 8, "help_defense_tendency": 20},
    "Chet Holmgren": {"interior_defense": 10, "block": 8},
    "Kristaps Porzingis": {"interior_defense": 8, "block": 6},
    "Al Horford": {"interior_defense": 6, "defensive_consistency": 6}
}

SHOOTING_BOOSTS = {
    "Stephen Curry": {"three_point": 8, "three_tendency": 10, "ball_handle": 5},
    "Klay Thompson": {"three_point": 6, "three_tendency": 10},
    "Sam Hauser": {"three_point": 5, "three_tendency": 10},
    "Isaiah Joe": {"three_point": 5, "three_tendency": 10},
    "Michael Porter Jr.": {"three_point": 5, "three_tendency": 6}
}

STAR_BOOSTS = {
    "Nikola Jokic": {"passing": 10, "post_scoring": 10, "offensive_consistency": 8},
    "Shai Gilgeous-Alexander": {"drive": 8, "draw_foul": 10, "draw_foul_tendency": 10},
    "Jayson Tatum": {"two_point": 5, "shot_tendency": 6},
    "Jaylen Brown": {"drive": 5, "layup": 5}
}

def normalize_name(name: str) -> str:
    if not name: return ""
    n = name.lower()
    n = n.replace('.', '')
    n = n.replace('-', ' ')
    n = re.sub(r'\s+', ' ', n).strip()
    return n

def clamp_int(val, min_val=25, max_val=99) -> int:
    return int(max(min_val, min(max_val, round(val))))

def scale(val, src_min, src_max, dst_min, dst_max) -> float:
    v = max(src_min, min(src_max, float(val)))
    pct = (v - src_min) / (src_max - src_min) if src_max > src_min else 0
    return dst_min + pct * (dst_max - dst_min)

def apply_boosts(player_name: str, attrs: dict, tends: dict):
    for boost_dict in [DEFENSE_BOOSTS, SHOOTING_BOOSTS, STAR_BOOSTS]:
        if player_name in boost_dict:
            for k, v in boost_dict[player_name].items():
                if k in attrs:
                    attrs[k] += v
                elif k in tends:
                    tends[k] += v

def calculate_attributes(row):
    pos = row['position']
    mpg = float(row['mpg'])
    gp = int(row['gp'])
    ppg = float(row['ppg'])
    rpg = float(row['rpg'])
    apg = float(row['apg'])
    spg = float(row['spg'])
    bpg = float(row['bpg'])
    fg_pct = float(row['fg_pct'])
    three_pct = float(row['three_pct'])
    ft_pct = float(row['ft_pct'])

    attrs = {}

    # 1. three_point
    tp = scale(three_pct, 0.250, 0.430, 50, 95)
    if pos in ["C", "PF"] and ppg < 8 and three_pct >= 0.600:
        tp = min(tp, 55)
    elif three_pct >= 0.600:
        tp = 50 if pos in ["C", "PF"] else 70
    attrs['three_point'] = tp

    # 2. free_throw
    attrs['free_throw'] = scale(ft_pct, 0.500, 0.930, 45, 96)

    # 3. two_point
    twop = scale(fg_pct, 0.380, 0.600, 55, 90)
    if ppg > 20: twop += 4
    if ppg > 25: twop += 6
    attrs['two_point'] = twop

    # 4. layup
    layup = scale(fg_pct, 0.400, 0.650, 55, 92)
    if pos in ["PG", "SG", "SF"] and ppg > 20: layup += 5
    attrs['layup'] = layup

    # 5. close_shot
    cs = scale(fg_pct, 0.430, 0.700, 55, 95)
    if pos in ["PF", "C"]: cs += 8
    attrs['close_shot'] = cs

    # 6. post_scoring
    base_post = {"PG": 35, "SG": 40, "SF": 55, "PF": 70, "C": 75}.get(pos, 50)
    attrs['post_scoring'] = base_post + scale(ppg * 0.5 + fg_pct * 20, 10, 30, -5, 10)

    # 7. passing
    attrs['passing'] = scale(apg, 0.5, 10.0, 45, 97)

    # 8. ball_handle
    bh = scale(apg + ppg * 0.08, 1.0, 9.0, 45, 96)
    if pos in ["PG", "SG"]: bh += 5
    if pos == "C" and apg < 5: bh -= 10
    attrs['ball_handle'] = bh

    # 9. drive
    drv = scale(ppg * 0.6 + fg_pct * 40, 20, 55, 45, 95)
    if pos in ["PG", "SG", "SF"]: drv += 5
    if pos == "C": drv -= 8
    attrs['drive'] = drv

    # 10. draw_foul
    attrs['draw_foul'] = scale(ppg * 0.8 + ft_pct * 20, 15, 45, 45, 95)

    # 11. perimeter_defense
    pd = scale(spg, 0.2, 1.8, 50, 92)
    if pos in ["PG", "SG", "SF"]: pd += 5
    if pos in ["PF", "C"]: pd -= 5
    attrs['perimeter_defense'] = pd

    # 12. interior_defense
    id_val = scale(bpg * 2 + rpg * 0.25, 0.5, 7.0, 45, 95)
    if pos in ["PF", "C"]: id_val += 8
    if pos in ["PG", "SG"]: id_val -= 8
    attrs['interior_defense'] = id_val

    # 13. steal
    attrs['steal'] = scale(spg, 0.2, 1.8, 45, 95)

    # 14. block
    attrs['block'] = scale(bpg, 0.1, 2.2, 40, 95)

    # 15. rebounds
    reb_base = scale(rpg, 1.5, 12.0, 45, 96)
    attrs['defensive_rebound'] = reb_base
    oreb = reb_base - 8
    if pos in ["PF", "C"]: oreb += 6
    attrs['offensive_rebound'] = oreb

    # 16. speed
    spd = {"PG": 88, "SG": 84, "SF": 80, "PF": 72, "C": 65}.get(pos, 75)
    attrs['speed'] = spd

    # 17. strength
    strg = {"PG": 55, "SG": 60, "SF": 70, "PF": 80, "C": 86}.get(pos, 65)
    attrs['strength'] = strg

    # 18. stamina
    stam = scale(mpg, 12, 36, 50, 95)
    if gp >= 70: stam += 3
    if gp < 40: stam -= 5
    attrs['stamina'] = stam

    # 19. offensive_consistency
    attrs['offensive_consistency'] = scale(ppg * 0.7 + fg_pct * 30 + three_pct * 10, 15, 45, 55, 97)

    # 20. defensive_consistency
    attrs['defensive_consistency'] = scale(mpg * 0.5 + spg * 8 + bpg * 6 + rpg * 0.8, 10, 35, 50, 95)

    return attrs

def calculate_tendencies(row):
    pos = row['position']
    mpg = float(row['mpg'])
    ppg = float(row['ppg'])
    rpg = float(row['rpg'])
    apg = float(row['apg'])
    spg = float(row['spg'])
    bpg = float(row['bpg'])
    fg_pct = float(row['fg_pct'])
    three_pct = float(row['three_pct'])
    ft_pct = float(row['ft_pct'])

    tends = {}

    tends['shot_tendency'] = scale(ppg, 4, 33, 35, 98)

    tt = scale(three_pct, 0.250, 0.430, 35, 90)
    if pos == "C" and three_pct < 0.350: tt -= 10
    if pos in ["C", "PF"] and ppg < 8 and three_pct >= 0.600:
        tt = min(tt, 25)
    tends['three_tendency'] = tt

    tpt = scale(ppg, 4, 30, 40, 85)
    if tt > 80: tpt -= 5
    tends['two_point_tendency'] = tpt

    dt = scale(ppg + ft_pct * 8, 8, 38, 35, 95)
    if pos in ["PG", "SG", "SF"]: dt += 5
    if pos == "C": dt -= 10
    tends['drive_tendency'] = dt

    base_post_tend = {"PG": 10, "SG": 15, "SF": 35, "PF": 60, "C": 70}.get(pos, 30)
    if row['player_name'] == "Nikola Jokic": base_post_tend += 15
    if row['player_name'] == "Kristaps Porzingis": base_post_tend += 8
    if row['player_name'] == "Draymond Green": base_post_tend += 5
    tends['post_tendency'] = base_post_tend

    cst = scale(fg_pct, 0.400, 0.650, 35, 90)
    if pos in ["PF", "C"]: cst += 10
    tends['close_shot_tendency'] = cst

    tends['pass_tendency'] = scale(apg, 0.5, 10.0, 35, 95)
    tends['draw_foul_tendency'] = scale(ppg * 0.7 + ft_pct * 12, 12, 40, 35, 95)
    tends['steal_tendency'] = scale(spg, 0.2, 1.8, 35, 95)
    tends['block_tendency'] = scale(bpg, 0.1, 2.2, 30, 95)

    ft = {"PG": 35, "SG": 38, "SF": 42, "PF": 50, "C": 55}.get(pos, 40)
    if row['player_name'] in ["Jrue Holiday", "Alex Caruso", "Al Horford"]: ft -= 5
    tends['foul_tendency'] = ft

    hdt = 0
    if row['player_name'] == "Draymond Green": hdt = 95
    elif row['player_name'] == "Jrue Holiday": hdt = 85
    elif row['player_name'] == "Derrick White": hdt = 82
    elif row['player_name'] == "Alex Caruso": hdt = 85
    elif row['player_name'] == "Chet Holmgren": hdt = 88
    elif row['player_name'] == "Al Horford": hdt = 82
    elif row['player_name'] == "Kristaps Porzingis": hdt = 80
    else:
        dc = scale(mpg * 0.5 + spg * 8 + bpg * 6 + rpg * 0.8, 10, 35, 50, 95)
        hdt = scale(dc, 50, 95, 45, 75)
    tends['help_defense_tendency'] = hdt

    ort = {"PG": 30, "SG": 30, "SF": 45, "PF": 70, "C": 70}.get(pos, 40)
    tends['offensive_rebound_tendency'] = ort
    tends['defensive_rebound_tendency'] = scale(rpg, 2.0, 12.0, 35, 95)

    return tends

def main(dry_run: bool):
    if not DB_PATH.exists():
        print(f"[ERROR] Database {DB_PATH} not found.")
        return

    if not CSV_PATH.exists():
        print(f"[ERROR] CSV {CSV_PATH} not found.")
        return

    if not dry_run:
        backup_path = DB_PATH.parent / "game.backup_before_real_stats.db"
        shutil.copy2(DB_PATH, backup_path)
        print(f"[INFO] Database backed up to {backup_path}")

    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()

    cur.execute("SELECT id, team_id, display_name, first_name, last_name FROM players")
    db_players = cur.fetchall()

    def match_player(team_id, d_name, f_name, l_name, p_name):
        # 1. Exact team_id + display_name
        for dbp in db_players:
            if dbp["team_id"] == team_id and dbp["display_name"] == d_name:
                return dbp["id"]
        # 2. Exact team_id + first_name + last_name
        for dbp in db_players:
            if dbp["team_id"] == team_id and dbp["first_name"] == f_name and dbp["last_name"] == l_name:
                return dbp["id"]
        # 3. Normalized name
        norm_p = normalize_name(p_name)
        for dbp in db_players:
            if dbp["team_id"] == team_id:
                norm_d = normalize_name(dbp["display_name"])
                norm_fl = normalize_name(f"{dbp['first_name']} {dbp['last_name']}")
                if norm_p == norm_d or norm_p == norm_fl:
                    return dbp["id"]
        return None

    csv_data = []
    with open(CSV_PATH, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        for row in reader:
            csv_data.append(row)

    processed = 0
    updated = 0
    warnings = []

    for row in csv_data:
        processed += 1
        team_id = row['team_id']
        p_name = row['player_name']
        f_name = row['first_name']
        l_name = row['last_name']
        d_name = row['display_name'] if row['display_name'] else p_name

        pid = match_player(team_id, d_name, f_name, l_name, p_name)
        if not pid:
            warnings.append(f"[WARN] Cannot find player {p_name} ({team_id}) in database.")
            continue

        attrs = calculate_attributes(row)
        tends = calculate_tendencies(row)
        
        apply_boosts(p_name, attrs, tends)

        for k in attrs: attrs[k] = clamp_int(attrs[k])
        for k in tends: tends[k] = clamp_int(tends[k])

        if dry_run:
            print(f"[DRY-RUN] Will update player {p_name} (ID: {pid})")
        else:
            # Update players table
            cur.execute("""
                UPDATE players SET
                    first_name = ?, last_name = ?, display_name = ?, name_order = ?,
                    nationality = ?, region_type = ?, position = ?,
                    height_cm = ?, weight_kg = ?, age = ?, jersey_number = ?
                WHERE id = ?
            """, (
                f_name, l_name, d_name, row['name_order'],
                row['nationality'], row['region_type'], row['position'],
                int(row['height_cm']), int(row['weight_kg']), int(row['age']), int(row['jersey_number']),
                pid
            ))

            # Update player_attributes
            attr_sql = "UPDATE player_attributes SET " + ", ".join(f"{k} = ?" for k in attrs.keys()) + " WHERE player_id = ?"
            attr_vals = list(attrs.values()) + [pid]
            cur.execute(attr_sql, attr_vals)

            # Update player_tendencies
            tend_sql = "UPDATE player_tendencies SET " + ", ".join(f"{k} = ?" for k in tends.keys()) + " WHERE player_id = ?"
            tend_vals = list(tends.values()) + [pid]
            cur.execute(tend_sql, tend_vals)

        updated += 1

    if not dry_run:
        conn.commit()

    conn.close()

    print("\n--- Summary ---")
    print(f"Processed from CSV: {processed}")
    print(f"Updated in DB: {updated}")
    for w in warnings:
        print(w)

    print("\n[INFO] Attributes updated. Overall cache may be stale until rating formula is recalculated.")
    if (PROJECT_ROOT / "tools" / "fit_overall_formula.py").exists():
        print("[INFO] Consider running 'python tools/fit_overall_formula.py' to recalculate overalls.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true", help="Run without modifying database")
    args = parser.parse_args()
    main(args.dry_run)
