import sqlite3
import csv
import re
from pathlib import Path
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"

REQUIRED_TEAMS = {
    "warriors_2022",
    "nuggets_2023",
    "celtics_2024",
    "thunder_2025",
    "warriors_2017",
    "lakers_2020",
    "bucks_2021",
    "raptors_2019"
}

ALLOWED_POSITIONS = {"PG", "SG", "SF", "PF", "C"}
ALLOWED_NAME_ORDERS = {"EASTERN", "WESTERN", "CUSTOM"}

def table_exists(conn: sqlite3.Connection, table_name: str) -> bool:
    cur = conn.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name=?", (table_name,))
    return cur.fetchone() is not None

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

    if len(team_ids) < 8:
        errors.append(f"Team count is {len(team_ids)}, expected at least 8.")

    for req_team in REQUIRED_TEAMS:
        if req_team not in team_ids:
            errors.append(f"Missing required team: {req_team}")

    for tid in team_ids:
        if tid in ["warriors_2017", "lakers_2020", "bucks_2021", "raptors_2019"]:
            if not re.match(r".+_\d{4}$", tid):
                errors.append(f"Historical team id '{tid}' must end with year.")

    cur.execute("SELECT * FROM players ORDER BY id ASC")
    players = cur.fetchall()
    
    if len(players) != 80:
        errors.append(f"Player count is {len(players)}, expected 80.")

    players_per_team = {}
    id_by_team = {}
    for p in players:
        pid = p["id"]
        tid = p["team_id"]
        if tid not in REQUIRED_TEAMS:
            errors.append(f"Player {pid} has unknown team_id '{tid}'.")
        players_per_team[tid] = players_per_team.get(tid, 0) + 1
        id_by_team.setdefault(tid, []).append(pid)
        
        if p["position"] not in ALLOWED_POSITIONS:
            errors.append(f"Player {pid} has invalid position '{p['position']}'.")

    for tid in REQUIRED_TEAMS:
        count = players_per_team.get(tid, 0)
        if count != 10:
            errors.append(f"Team {tid} has {count} players, expected exactly 10.")

    hist_ranges = {
        "warriors_2017": (1040, 1049),
        "lakers_2020": (1050, 1059),
        "bucks_2021": (1060, 1069),
        "raptors_2019": (1070, 1079),
    }

    for tid, (min_id, max_id) in hist_ranges.items():
        team_pids = id_by_team.get(tid, [])
        for pid in team_pids:
            if not (min_id <= pid <= max_id):
                errors.append(f"Player {pid} in {tid} is outside allowed ID range {min_id}-{max_id}.")

    cur.execute("SELECT * FROM player_attributes")
    attributes = cur.fetchall()
    for attr in attributes:
        pid = attr["player_id"]
        for key in attr.keys():
            if key == "player_id": continue
            val = attr[key]
            if not isinstance(val, int) or not (0 <= val <= 99):
                errors.append(f"Player {pid} attribute '{key}' = {val} is outside expected range 0~99.")

    cur.execute("SELECT * FROM player_tendencies")
    tendencies = cur.fetchall()
    for tend in tendencies:
        pid = tend["player_id"]
        for key in tend.keys():
            if key == "player_id": continue
            val = tend[key]
            if not isinstance(val, int) or not (0 <= val <= 99):
                errors.append(f"Player {pid} tendency '{key}' = {val} is outside expected range 0~99.")

    # Small sample 3pt anomaly check
    for p in players:
        pid = p["id"]
        pname = f"{p['first_name']} {p['last_name']}"
        if pname in ["Dwight Howard", "JaVale McGee"]:
            p_attr = next((a for a in attributes if a["player_id"] == pid), None)
            p_tend = next((t for t in tendencies if t["player_id"] == pid), None)
            if p_attr and p_attr["three_point"] > 30:
                errors.append(f"Player {pname} (ID: {pid}) has unusually high three_point {p_attr['three_point']}. Needs small sample protection.")
            if p_tend and p_tend["three_tendency"] > 10:
                errors.append(f"Player {pname} (ID: {pid}) has unusually high three_tendency {p_tend['three_tendency']}.")

    conn.close()

    if errors:
        for err in errors: print(f"[ERROR] {err}")
    if warnings:
        for warn in warnings: print(f"[WARN] {warn}")

    if not errors:
        print("[PASS] Unity database validation passed.")

    return len(errors) == 0

if __name__ == "__main__":
    success = validate()
    sys.exit(0 if success else 1)
