import sqlite3
from pathlib import Path
import sys

PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"

ALLOWED_TEAMS = {"warriors_2022", "nuggets_2023", "celtics_2024", "thunder_2025"}
DISALLOWED_TEAMS = {"celtics_2022", "heat_2023", "mavericks_2024", "pacers_2025"}
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

    # 1-4. Check tables
    for table in ["teams", "players", "player_attributes", "player_tendencies"]:
        if not table_exists(conn, table):
            errors.append(f"Table '{table}' does not exist.")

    if errors:
        for err in errors:
            print(f"[ERROR] {err}")
        return False

    # 5. Team count
    cur.execute("SELECT id FROM teams")
    teams = cur.fetchall()
    team_ids = {row["id"] for row in teams}

    if len(team_ids) != 4:
        errors.append(f"Team count is {len(team_ids)}, expected 4.")

    # 6-7. Team ID rules
    for team_id in team_ids:
        if team_id not in ALLOWED_TEAMS:
            errors.append(f"Invalid team id '{team_id}'. Only {ALLOWED_TEAMS} are allowed.")
        if team_id in DISALLOWED_TEAMS:
            errors.append(f"Disallowed team id '{team_id}' found.")

    # Players check
    cur.execute("SELECT * FROM players ORDER BY id ASC")
    players = cur.fetchall()
    
    if not players:
        errors.append("No players found in the database.")
        return False

    player_count = len(players)
    min_id = players[0]["id"]
    max_id = players[-1]["id"]
    
    # 8. players.id is int (SQLite dynamic typing but we can check type)
    for p in players:
        if not isinstance(p["id"], int):
            errors.append(f"Player ID {p['id']} is not an integer.")

    # 9. Min player id
    if min_id < 1000:
        errors.append(f"Minimum player ID is {min_id}, expected >= 1000.")

    # 10. Contiguous IDs
    expected_ids = set(range(min_id, min_id + player_count))
    actual_ids = {p["id"] for p in players}
    if expected_ids != actual_ids:
        warnings.append("Player IDs are not contiguous.")

    # Team player count mapping
    players_per_team = {}
    
    for p in players:
        pid = p["id"]
        tid = p["team_id"]
        
        # 11. Valid team_id
        if tid not in team_ids:
            errors.append(f"Player {pid} has unknown team_id '{tid}'.")
            
        players_per_team[tid] = players_per_team.get(tid, 0) + 1

        # 16. Position
        if p["position"] not in ALLOWED_POSITIONS:
            errors.append(f"Player {pid} has invalid position '{p['position']}'.")
            
        # 17. Name order
        if p["name_order"] not in ALLOWED_NAME_ORDERS:
            errors.append(f"Player {pid} has invalid name_order '{p['name_order']}'.")
            
        # 18. Height
        if not (160 <= p["height_cm"] <= 230):
            warnings.append(f"Player {pid} has unusual height: {p['height_cm']} cm.")
            
        # 19. Weight
        if not (60 <= p["weight_kg"] <= 160):
            warnings.append(f"Player {pid} has unusual weight: {p['weight_kg']} kg.")
            
        # 20. Age
        if not (18 <= p["age"] <= 45):
            warnings.append(f"Player {pid} has unusual age: {p['age']}.")
            
        # 21. Jersey number
        if not (0 <= p["jersey_number"] <= 99):
            warnings.append(f"Player {pid} has unusual jersey number: {p['jersey_number']}.")

    # 22. Players per team >= 8
    for tid, count in players_per_team.items():
        if count < 8:
            errors.append(f"Team '{tid}' has only {count} players (minimum 8 expected).")

    # Attributes check
    cur.execute("SELECT * FROM player_attributes")
    attributes = cur.fetchall()
    attr_player_ids = {row["player_id"] for row in attributes}
    
    # 12. Every player has attributes
    for pid in actual_ids:
        if pid not in attr_player_ids:
            errors.append(f"Player {pid} is missing player_attributes.")
            
    for attr in attributes:
        pid = attr["player_id"]
        for key in attr.keys():
            if key == "player_id":
                continue
            val = attr[key]
            # 14. Attributes in 0~99
            if not isinstance(val, int) or not (0 <= val <= 99):
                warnings.append(f"Player {pid} attribute '{key}' = {val} is outside expected range 0~99.")

    # Tendencies check
    cur.execute("SELECT * FROM player_tendencies")
    tendencies = cur.fetchall()
    tend_player_ids = {row["player_id"] for row in tendencies}

    # 13. Every player has tendencies
    for pid in actual_ids:
        if pid not in tend_player_ids:
            errors.append(f"Player {pid} is missing player_tendencies.")
            
    for tend in tendencies:
        pid = tend["player_id"]
        for key in tend.keys():
            if key == "player_id":
                continue
            val = tend[key]
            # 15. Tendencies in 0~99
            if not isinstance(val, int) or not (0 <= val <= 99):
                warnings.append(f"Player {pid} tendency '{key}' = {val} is outside expected range 0~99.")

    conn.close()

    if errors:
        for err in errors:
            print(f"[ERROR] {err}")
    if warnings:
        for warn in warnings:
            print(f"[WARN] {warn}")

    if not errors:
        print("[PASS] Unity database validation passed.")

    print("\n--- Summary ---")
    print(f"Team count: {len(team_ids)}")
    print(f"Player count: {player_count}")
    print(f"Min player ID: {min_id}")
    print(f"Max player ID: {max_id}")
    print("Players per team:")
    for tid, count in players_per_team.items():
        print(f"  {tid}: {count}")

    return len(errors) == 0

if __name__ == "__main__":
    success = validate()
    sys.exit(0 if success else 1)
