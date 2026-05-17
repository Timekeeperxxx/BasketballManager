import sqlite3
import csv
import re
from pathlib import Path
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"
CSV_PATH = PROJECT_ROOT / "data" / "source" / "champion_player_stats.csv"

ALLOWED_TEAMS = {"warriors_2022", "nuggets_2023", "celtics_2024", "thunder_2025"}
DISALLOWED_TEAMS = {"celtics_2022", "heat_2023", "mavericks_2024", "pacers_2025"}
ALLOWED_POSITIONS = {"PG", "SG", "SF", "PF", "C"}
ALLOWED_NAME_ORDERS = {"EASTERN", "WESTERN", "CUSTOM"}

def table_exists(conn: sqlite3.Connection, table_name: str) -> bool:
    cur = conn.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name=?", (table_name,))
    return cur.fetchone() is not None

def normalize_name(name: str) -> str:
    if not name: return ""
    n = name.lower()
    n = n.replace('.', '')
    n = n.replace('-', ' ')
    n = re.sub(r'\s+', ' ', n).strip()
    return n

def validate() -> bool:
    if not DB_PATH.exists():
        print(f"[ERROR] Database file not found at {DB_PATH}")
        return False

    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()

    errors = []
    warnings = []

    for table in ["teams", "players", "player_attributes", "player_tendencies"]:
        if not table_exists(conn, table):
            errors.append(f"Table '{table}' does not exist.")

    if errors:
        for err in errors: print(f"[ERROR] {err}")
        return False

    cur.execute("SELECT id FROM teams")
    teams = cur.fetchall()
    team_ids = {row["id"] for row in teams}

    if len(team_ids) != 4:
        errors.append(f"Team count is {len(team_ids)}, expected 4.")

    for team_id in team_ids:
        if team_id not in ALLOWED_TEAMS:
            errors.append(f"Invalid team id '{team_id}'. Only {ALLOWED_TEAMS} are allowed.")

    cur.execute("SELECT * FROM players ORDER BY id ASC")
    players = cur.fetchall()
    
    if not players:
        errors.append("No players found in the database.")
        return False

    player_count = len(players)
    min_id = players[0]["id"]
    max_id = players[-1]["id"]
    
    for p in players:
        if not isinstance(p["id"], int):
            errors.append(f"Player ID {p['id']} is not an integer.")
        if not p["display_name"]:
            errors.append(f"Player ID {p['id']} has empty display_name.")
        if p["position"] not in ALLOWED_POSITIONS:
            errors.append(f"Player {p['id']} has invalid position '{p['position']}'.")

    if min_id < 1000:
        errors.append(f"Minimum player ID is {min_id}, expected >= 1000.")

    players_per_team = {}
    db_player_match_keys = []
    
    for p in players:
        pid = p["id"]
        tid = p["team_id"]
        if tid not in team_ids:
            errors.append(f"Player {pid} has unknown team_id '{tid}'.")
        players_per_team[tid] = players_per_team.get(tid, 0) + 1
        
        db_player_match_keys.append({
            "id": pid,
            "team_id": tid,
            "display_name": p["display_name"],
            "first_name": p["first_name"],
            "last_name": p["last_name"],
            "matched": False
        })

    # Validate CSV matching
    if CSV_PATH.exists():
        with open(CSV_PATH, 'r', encoding='utf-8-sig') as f:
            reader = list(csv.DictReader(f))
            if len(reader) != 40:
                errors.append(f"CSV contains {len(reader)} players, expected 40.")
            
            for row in reader:
                tid = row['team_id']
                p_name = row['player_name']
                f_name = row['first_name']
                l_name = row['last_name']
                d_name = row['display_name'] if row['display_name'] else p_name
                
                matched = False
                for dbp in db_player_match_keys:
                    if dbp["team_id"] == tid:
                        if dbp["display_name"] == d_name or \
                           (dbp["first_name"] == f_name and dbp["last_name"] == l_name) or \
                           normalize_name(p_name) == normalize_name(dbp["display_name"]) or \
                           normalize_name(p_name) == normalize_name(f"{dbp['first_name']} {dbp['last_name']}"):
                            dbp["matched"] = True
                            matched = True
                            break
                if not matched:
                    errors.append(f"CSV Player {p_name} ({tid}) not found in database.")

        unmatched_db = [p["id"] for p in db_player_match_keys if not p["matched"]]
        if len(unmatched_db) > 0:
            errors.append(f"Database contains players without real data source: {unmatched_db}")

    cur.execute("SELECT * FROM player_attributes")
    attributes = cur.fetchall()
    attr_player_ids = {row["player_id"] for row in attributes}
    
    for attr in attributes:
        pid = attr["player_id"]
        for key in attr.keys():
            if key == "player_id": continue
            val = attr[key]
            if not isinstance(val, int) or not (25 <= val <= 99):
                errors.append(f"Player {pid} attribute '{key}' = {val} is outside expected range 25~99.")

    cur.execute("SELECT * FROM player_tendencies")
    tendencies = cur.fetchall()
    tend_player_ids = {row["player_id"] for row in tendencies}

    for tend in tendencies:
        pid = tend["player_id"]
        for key in tend.keys():
            if key == "player_id": continue
            val = tend[key]
            if not isinstance(val, int) or not (25 <= val <= 99):
                errors.append(f"Player {pid} tendency '{key}' = {val} is outside expected range 25~99.")

    # Small sample 3pt anomaly check
    for p in players:
        pid = p["id"]
        pos = p["position"]
        if pos in ["C", "PF"]:
            p_attr = next((a for a in attributes if a["player_id"] == pid), None)
            if p_attr and p_attr["three_point"] > 70:
                warnings.append(f"Player {pid} ({pos}) has unusually high three_point {p_attr['three_point']}.")

    conn.close()

    if errors:
        for err in errors: print(f"[ERROR] {err}")
    if warnings:
        for warn in warnings: print(f"[WARN] {warn}")

    if not errors:
        print("[PASS] Unity database validation passed.")

    # Averages printout
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cur = conn.cursor()
    print("\n--- Team Averages ---")
    for tid in team_ids:
        cur.execute("""
            SELECT AVG(a.three_point) as avg_3pt, AVG(a.passing) as avg_pass, 
                   AVG(a.defensive_consistency) as avg_def, AVG(p.overall) as avg_ovr
            FROM players p
            JOIN player_attributes a ON p.id = a.player_id
            WHERE p.team_id = ?
        """, (tid,))
        res = cur.fetchone()
        if res and res["avg_3pt"]:
            print(f"{tid}: OVR={res['avg_ovr']:.1f}, 3PT={res['avg_3pt']:.1f}, PASS={res['avg_pass']:.1f}, DEF_CON={res['avg_def']:.1f}")

    conn.close()
    
    print("\n--- Summary ---")
    print(f"Team count: {len(team_ids)}")
    print(f"Player count: {player_count}")

    return len(errors) == 0

if __name__ == "__main__":
    success = validate()
    sys.exit(0 if success else 1)
