import sqlite3
import csv
import argparse
import shutil
import re
import datetime
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"
CSV_PATH_1 = PROJECT_ROOT / "data" / "source" / "champion_player_stats.csv"
CSV_PATH_2 = PROJECT_ROOT / "data" / "source" / "historical_champion_player_stats.csv"
RATINGS_CSV_PATH = PROJECT_ROOT / "data" / "source" / "historical_champion_player_ratings.csv"

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

def clamp_int(val, min_val=0, max_val=99) -> int:
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
    if row.get('player_name') == "Nikola Jokic": base_post_tend += 15
    if row.get('player_name') == "Kristaps Porzingis": base_post_tend += 8
    if row.get('player_name') == "Draymond Green": base_post_tend += 5
    tends['post_tendency'] = base_post_tend

    cst = scale(fg_pct, 0.400, 0.650, 35, 90)
    if pos in ["PF", "C"]: cst += 10
    tends['close_shot_tendency'] = cst

    tends['pass_tendency'] = scale(apg, 0.5, 10.0, 35, 95)
    tends['draw_foul_tendency'] = scale(ppg * 0.7 + ft_pct * 12, 12, 40, 35, 95)
    tends['steal_tendency'] = scale(spg, 0.2, 1.8, 35, 95)
    tends['block_tendency'] = scale(bpg, 0.1, 2.2, 30, 95)

    ft = {"PG": 35, "SG": 38, "SF": 42, "PF": 50, "C": 55}.get(pos, 40)
    if row.get('player_name') in ["Jrue Holiday", "Alex Caruso", "Al Horford"]: ft -= 5
    tends['foul_tendency'] = ft

    hdt = 0
    if row.get('player_name') == "Draymond Green": hdt = 95
    elif row.get('player_name') == "Jrue Holiday": hdt = 85
    elif row.get('player_name') == "Derrick White": hdt = 82
    elif row.get('player_name') == "Alex Caruso": hdt = 85
    elif row.get('player_name') == "Chet Holmgren": hdt = 88
    elif row.get('player_name') == "Al Horford": hdt = 82
    elif row.get('player_name') == "Kristaps Porzingis": hdt = 80
    else:
        dc = scale(mpg * 0.5 + spg * 8 + bpg * 6 + rpg * 0.8, 10, 35, 50, 95)
        hdt = scale(dc, 50, 95, 45, 75)
    tends['help_defense_tendency'] = hdt

    ort = {"PG": 30, "SG": 30, "SF": 45, "PF": 70, "C": 70}.get(pos, 40)
    tends['offensive_rebound_tendency'] = ort
    tends['defensive_rebound_tendency'] = scale(rpg, 2.0, 12.0, 35, 95)

    return tends

def get_expected_fields():
    expected_attrs = {
        'two_point', 'three_point', 'layup', 'close_shot', 'post_scoring', 'free_throw',
        'passing', 'ball_handle', 'drive', 'draw_foul', 'offensive_consistency',
        'perimeter_defense', 'interior_defense', 'steal', 'block',
        'offensive_rebound', 'defensive_rebound', 'defensive_consistency',
        'speed', 'strength', 'stamina'
    }
    expected_tends = {
        'shot_tendency', 'three_tendency', 'two_point_tendency', 'drive_tendency',
        'post_tendency', 'close_shot_tendency', 'pass_tendency', 'draw_foul_tendency',
        'steal_tendency', 'block_tendency', 'foul_tendency', 'help_defense_tendency',
        'offensive_rebound_tendency', 'defensive_rebound_tendency'
    }
    return expected_attrs, expected_tends

def load_ratings_csv(path):
    if not path.exists():
        return {}

    expected_attrs, expected_tends = get_expected_fields()
    all_expected = expected_attrs.union(expected_tends)

    ratings_db = {}
    with open(path, 'r', encoding='utf-8-sig') as f:
        reader = csv.DictReader(f)
        headers = set(reader.fieldnames)
        
        # Check missing fields
        missing = all_expected - headers
        if missing:
            raise ValueError(f"Ratings CSV is missing expected fields: {missing}")

        # Check extra fields
        extra = headers - all_expected - {'player_id'}
        if extra:
            print(f"[WARNING] Ratings CSV contains extra unexpected fields: {extra} (will be ignored)")

        for row in reader:
            if not row.get('player_id'): continue
            pid = int(row['player_id'])
            
            attrs = {}
            for k in expected_attrs:
                attrs[k] = clamp_int(int(row[k]))

            tends = {}
            for k in expected_tends:
                tends[k] = clamp_int(int(row[k]))

            ratings_db[pid] = {'attributes': attrs, 'tendencies': tends}
            
    return ratings_db

def main(dry_run: bool):
    if not DB_PATH.exists():
        print(f"[ERROR] Database {DB_PATH} not found.")
        return

    csv_paths = []
    if CSV_PATH_1.exists(): csv_paths.append(CSV_PATH_1)
    if CSV_PATH_2.exists(): csv_paths.append(CSV_PATH_2)

    if not csv_paths:
        print("[ERROR] No stats CSV files found.")
        return

    ratings_db = {}
    try:
        ratings_db = load_ratings_csv(RATINGS_CSV_PATH)
    except Exception as e:
        print(f"[ERROR] Failed to load ratings CSV: {e}")
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

    def match_player(row_pid, team_id, d_name, f_name, l_name):
        # 1. Exact player_id
        if row_pid:
            for dbp in db_players:
                if str(dbp["id"]) == str(row_pid):
                    return dbp["id"]
        # 2. Exact team_id + display_name
        for dbp in db_players:
            if dbp["team_id"] == team_id and dbp["display_name"] == d_name:
                return dbp["id"]
        # 3. Exact team_id + first_name + last_name
        for dbp in db_players:
            if dbp["team_id"] == team_id and dbp["first_name"] == f_name and dbp["last_name"] == l_name:
                return dbp["id"]
        return None

    csv_data = []
    for cp in csv_paths:
        with open(cp, 'r', encoding='utf-8-sig') as f:
            reader = csv.DictReader(f)
            for row in reader:
                csv_data.append(row)

    processed = 0
    updated = 0
    overrides_applied = 0
    warnings = []
    errors = []
    small_sample_logs = []

    for row in csv_data:
        processed += 1
        team_id = row['team_id']
        p_name = row.get('player_name') or row.get('display_name') or f"{row.get('first_name')} {row.get('last_name')}"
        f_name = row.get('first_name', '')
        l_name = row.get('last_name', '')
        d_name = row.get('display_name') if row.get('display_name') else p_name
        row_pid = row.get('player_id')

        pid = match_player(row_pid, team_id, d_name, f_name, l_name)
        if not pid:
            errors.append(f"[ERROR] Cannot find player {p_name} ({team_id}) in database. Skipping.")
            continue

        if pid in ratings_db:
            attrs = ratings_db[pid]['attributes']
            tends = ratings_db[pid]['tendencies']
            overrides_applied += 1
        else:
            if row_pid and int(row_pid) >= 1040:
                errors.append(f"[ERROR] Missing ratings override for historical player {p_name} (ID: {pid})")
                
            attrs = calculate_attributes(row)
            tends = calculate_tendencies(row)
            apply_boosts(p_name, attrs, tends)
            
        # small sample protection check
        if "small-sample 3P protected" in row.get('source_note', ''):
            small_sample_logs.append(f"Protected 3P small sample for {p_name}: three_point={attrs['three_point']}, three_tendency={tends['three_tendency']}")

        for k in attrs: attrs[k] = clamp_int(attrs[k])
        for k in tends: tends[k] = clamp_int(tends[k])

        if dry_run:
            print(f"[DRY-RUN] Will update player {p_name} (ID: {pid})")
        else:
            # Note: We won't update first_name/last_name here, we just update stats if it's historical, but for safety we can update position/height/weight.
            cur.execute("""
                UPDATE players SET
                    position = ?, height_cm = ?, weight_kg = ?, age = ?
                WHERE id = ?
            """, (
                row['position'],
                int(row['height_cm']), int(row['weight_kg']), int(row['age']),
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
    print(f"Ratings Overrides Applied: {overrides_applied}")
    for log in small_sample_logs:
        print(f"[PROTECT] {log}")
    for w in warnings:
        print(w)
    for e in errors:
        print(e)
        
    if errors:
        print("[FATAL] Errors occurred during stat generation. Check logs.")

    print("\n[INFO] Attributes updated. Remember to run tools/recalculate_overalls.py next.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true", help="Run without modifying database")
    args = parser.parse_args()
    main(args.dry_run)
