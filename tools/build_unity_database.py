from __future__ import annotations

import sqlite3
from pathlib import Path


PROJECT_ROOT = Path(__file__).resolve().parents[1]
DB_PATH = PROJECT_ROOT / "Assets" / "StreamingAssets" / "game.db"


def create_schema(conn: sqlite3.Connection) -> None:
    conn.executescript(
        """
        PRAGMA foreign_keys = OFF;
        DROP TABLE IF EXISTS player_tendencies;
        DROP TABLE IF EXISTS player_attributes;
        DROP TABLE IF EXISTS players;
        DROP TABLE IF EXISTS teams;

        CREATE TABLE teams (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            city TEXT DEFAULT '',
            era INTEGER DEFAULT 0,
            is_current INTEGER DEFAULT 0
        );

        CREATE TABLE players (
            id INTEGER PRIMARY KEY,
            team_id TEXT NOT NULL,

            first_name TEXT NOT NULL,
            last_name TEXT NOT NULL,
            display_name TEXT,
            name_order TEXT NOT NULL DEFAULT 'WESTERN',
            nationality TEXT,
            region_type TEXT,

            position TEXT NOT NULL,
            height_cm INTEGER NOT NULL,
            weight_kg INTEGER NOT NULL,
            age INTEGER NOT NULL,
            jersey_number INTEGER,
            overall INTEGER NOT NULL DEFAULT 70,

            FOREIGN KEY (team_id) REFERENCES teams (id) ON UPDATE CASCADE ON DELETE CASCADE
        );

        CREATE INDEX idx_players_team_id ON players (team_id);

        CREATE TABLE player_attributes (
            player_id INTEGER PRIMARY KEY,

            two_point INTEGER DEFAULT 60,
            three_point INTEGER DEFAULT 60,
            layup INTEGER DEFAULT 60,
            close_shot INTEGER DEFAULT 60,
            post_scoring INTEGER DEFAULT 60,
            free_throw INTEGER DEFAULT 60,

            passing INTEGER DEFAULT 60,
            ball_handle INTEGER DEFAULT 60,
            drive INTEGER DEFAULT 60,
            draw_foul INTEGER DEFAULT 60,

            offensive_consistency INTEGER DEFAULT 70,

            perimeter_defense INTEGER DEFAULT 60,
            interior_defense INTEGER DEFAULT 60,
            steal INTEGER DEFAULT 60,
            block INTEGER DEFAULT 60,

            offensive_rebound INTEGER DEFAULT 60,
            defensive_rebound INTEGER DEFAULT 60,

            defensive_consistency INTEGER DEFAULT 70,

            speed INTEGER DEFAULT 60,
            strength INTEGER DEFAULT 60,
            stamina INTEGER DEFAULT 60,

            FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
        );

        CREATE TABLE player_tendencies (
            player_id INTEGER PRIMARY KEY,

            shot_tendency INTEGER DEFAULT 60,
            three_tendency INTEGER DEFAULT 60,
            two_point_tendency INTEGER DEFAULT 60,
            drive_tendency INTEGER DEFAULT 60,
            post_tendency INTEGER DEFAULT 60,
            close_shot_tendency INTEGER DEFAULT 60,
            pass_tendency INTEGER DEFAULT 60,
            draw_foul_tendency INTEGER DEFAULT 60,

            steal_tendency INTEGER DEFAULT 60,
            block_tendency INTEGER DEFAULT 60,
            foul_tendency INTEGER DEFAULT 60,
            help_defense_tendency INTEGER DEFAULT 60,
            offensive_rebound_tendency INTEGER DEFAULT 60,
            defensive_rebound_tendency INTEGER DEFAULT 60,

            FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE
        );
        PRAGMA foreign_keys = ON;
        """
    )


def insert_team(conn: sqlite3.Connection, team: dict) -> None:
    conn.execute(
        """
        INSERT INTO teams (id, name, city, era, is_current)
        VALUES (?, ?, ?, ?, ?)
        """,
        (team["id"], team["name"], team["city"], team["era"], team["is_current"]),
    )


def insert_player_base(conn: sqlite3.Connection, player_id: int, player: dict) -> None:
    conn.execute(
        """
        INSERT INTO players (
            id, team_id, first_name, last_name, display_name, name_order, nationality, region_type,
            position, height_cm, weight_kg, age, jersey_number, overall
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            player_id,
            player["team_id"],
            player["first_name"],
            player["last_name"],
            player["display_name"] or None,
            player["name_order"],
            player["nationality"],
            player["region_type"],
            player["position"],
            player["height_cm"],
            player["weight_kg"],
            player["age"],
            player["jersey_number"],
            player["overall"],
        ),
    )


def insert_player_attributes(conn: sqlite3.Connection, player_id: int, attributes: dict) -> None:
    conn.execute(
        """
        INSERT INTO player_attributes (
            player_id, two_point, three_point, layup, close_shot, post_scoring, free_throw, passing,
            ball_handle, drive, draw_foul, offensive_consistency, perimeter_defense, interior_defense,
            steal, block, offensive_rebound, defensive_rebound, defensive_consistency, speed, strength, stamina
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            player_id,
            attributes["two_point"],
            attributes["three_point"],
            attributes["layup"],
            attributes["close_shot"],
            attributes["post_scoring"],
            attributes["free_throw"],
            attributes["passing"],
            attributes["ball_handle"],
            attributes["drive"],
            attributes["draw_foul"],
            attributes["offensive_consistency"],
            attributes["perimeter_defense"],
            attributes["interior_defense"],
            attributes["steal"],
            attributes["block"],
            attributes["offensive_rebound"],
            attributes["defensive_rebound"],
            attributes["defensive_consistency"],
            attributes["speed"],
            attributes["strength"],
            attributes["stamina"],
        ),
    )


def insert_player_tendencies(conn: sqlite3.Connection, player_id: int, tendencies: dict) -> None:
    conn.execute(
        """
        INSERT INTO player_tendencies (
            player_id, shot_tendency, three_tendency, two_point_tendency, drive_tendency, post_tendency,
            close_shot_tendency, pass_tendency, draw_foul_tendency, steal_tendency, block_tendency,
            foul_tendency, help_defense_tendency, offensive_rebound_tendency, defensive_rebound_tendency
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (
            player_id,
            tendencies["shot_tendency"],
            tendencies["three_tendency"],
            tendencies["two_point_tendency"],
            tendencies["drive_tendency"],
            tendencies["post_tendency"],
            tendencies["close_shot_tendency"],
            tendencies["pass_tendency"],
            tendencies["draw_foul_tendency"],
            tendencies["steal_tendency"],
            tendencies["block_tendency"],
            tendencies["foul_tendency"],
            tendencies["help_defense_tendency"],
            tendencies["offensive_rebound_tendency"],
            tendencies["defensive_rebound_tendency"],
        ),
    )


def make_attributes(**overrides: int) -> dict:
    values = {
        "two_point": 70,
        "three_point": 70,
        "layup": 70,
        "close_shot": 70,
        "post_scoring": 55,
        "free_throw": 70,
        "passing": 68,
        "ball_handle": 68,
        "drive": 68,
        "draw_foul": 60,
        "offensive_consistency": 72,
        "perimeter_defense": 68,
        "interior_defense": 60,
        "steal": 62,
        "block": 50,
        "offensive_rebound": 58,
        "defensive_rebound": 62,
        "defensive_consistency": 70,
        "speed": 68,
        "strength": 68,
        "stamina": 75,
    }
    values.update(overrides)
    return values


def make_tendencies(**overrides: int) -> dict:
    values = {
        "shot_tendency": 65,
        "three_tendency": 55,
        "two_point_tendency": 65,
        "drive_tendency": 55,
        "post_tendency": 25,
        "close_shot_tendency": 52,
        "pass_tendency": 55,
        "draw_foul_tendency": 45,
        "steal_tendency": 40,
        "block_tendency": 25,
        "foul_tendency": 40,
        "help_defense_tendency": 55,
        "offensive_rebound_tendency": 35,
        "defensive_rebound_tendency": 45,
    }
    values.update(overrides)
    return values


def make_player(
    team_id: str,
    first_name: str,
    last_name: str,
    position: str,
    height_cm: int,
    weight_kg: int,
    age: int,
    jersey_number: int,
    overall: int,
    nationality: str = "USA",
    region_type: str = "NORTH_AMERICA",
    display_name: str = "",
    name_order: str = "WESTERN",
    attributes: dict | None = None,
    tendencies: dict | None = None,
) -> dict:
    return {
        "team_id": team_id,
        "first_name": first_name,
        "last_name": last_name,
        "display_name": display_name,
        "name_order": name_order,
        "nationality": nationality,
        "region_type": region_type,
        "position": position,
        "height_cm": height_cm,
        "weight_kg": weight_kg,
        "age": age,
        "jersey_number": jersey_number,
        "overall": overall,
        "attributes": attributes or make_attributes(),
        "tendencies": tendencies or make_tendencies(),
    }


def get_teams() -> list[dict]:
    return [
        {"id": "warriors_2022", "name": "Golden State Warriors", "city": "Golden State", "era": 2022, "is_current": 0},
        {"id": "nuggets_2023", "name": "Denver Nuggets", "city": "Denver", "era": 2023, "is_current": 0},
        {"id": "celtics_2024", "name": "Boston Celtics", "city": "Boston", "era": 2024, "is_current": 0},
        {"id": "thunder_2025", "name": "Oklahoma City Thunder", "city": "Oklahoma City", "era": 2025, "is_current": 0},
        {"id": "warriors_2017", "name": "2017 Golden State Warriors", "city": "Golden State", "era": 2017, "is_current": 0},
        {"id": "lakers_2020", "name": "2020 Los Angeles Lakers", "city": "Los Angeles", "era": 2020, "is_current": 0},
        {"id": "bucks_2021", "name": "2021 Milwaukee Bucks", "city": "Milwaukee", "era": 2021, "is_current": 0},
        {"id": "raptors_2019", "name": "2019 Toronto Raptors", "city": "Toronto", "era": 2019, "is_current": 0},
    ]


def get_players() -> list[dict]:
    return [
        make_player(
            "warriors_2022", "Stephen", "Curry", "PG", 188, 84, 34, 30, 96,
            attributes=make_attributes(
                two_point=88, three_point=99, layup=90, close_shot=84, post_scoring=48, free_throw=92,
                passing=90, ball_handle=97, drive=88, draw_foul=78, offensive_consistency=96,
                perimeter_defense=76, interior_defense=45, steal=74, block=35,
                offensive_rebound=42, defensive_rebound=58, defensive_consistency=72,
                speed=88, strength=62, stamina=95,
            ),
            tendencies=make_tendencies(
                shot_tendency=95, three_tendency=98, two_point_tendency=72, drive_tendency=78,
                post_tendency=8, close_shot_tendency=48, pass_tendency=78, draw_foul_tendency=62,
                steal_tendency=58, block_tendency=10, foul_tendency=35, help_defense_tendency=55,
                offensive_rebound_tendency=18, defensive_rebound_tendency=42,
            ),
        ),
        make_player("warriors_2022", "Klay", "Thompson", "SG", 198, 98, 32, 11, 85,
                    attributes=make_attributes(two_point=82, three_point=91, layup=72, close_shot=75, post_scoring=50, free_throw=88,
                                               passing=70, ball_handle=72, drive=68, draw_foul=46, offensive_consistency=84,
                                               perimeter_defense=78, interior_defense=48, steal=62, block=40,
                                               offensive_rebound=40, defensive_rebound=52, defensive_consistency=78,
                                               speed=70, strength=68, stamina=84),
                    tendencies=make_tendencies(shot_tendency=86, three_tendency=92, two_point_tendency=62, drive_tendency=45,
                                               post_tendency=10, close_shot_tendency=42, pass_tendency=42, draw_foul_tendency=28,
                                               steal_tendency=44, block_tendency=18, foul_tendency=34, help_defense_tendency=58,
                                               offensive_rebound_tendency=14, defensive_rebound_tendency=34)),
        make_player("warriors_2022", "Andrew", "Wiggins", "SF", 201, 89, 27, 22, 84, nationality="Canada",
                    attributes=make_attributes(two_point=84, three_point=79, layup=82, close_shot=80, post_scoring=63, free_throw=63,
                                               passing=68, ball_handle=74, drive=82, draw_foul=62, offensive_consistency=80,
                                               perimeter_defense=84, interior_defense=62, steal=68, block=55,
                                               offensive_rebound=58, defensive_rebound=72, defensive_consistency=82,
                                               speed=82, strength=74, stamina=88),
                    tendencies=make_tendencies(shot_tendency=78, three_tendency=62, two_point_tendency=74, drive_tendency=68,
                                               post_tendency=18, close_shot_tendency=55, pass_tendency=42, draw_foul_tendency=48,
                                               steal_tendency=52, block_tendency=32, foul_tendency=38, help_defense_tendency=68,
                                               offensive_rebound_tendency=32, defensive_rebound_tendency=58)),
        make_player("warriors_2022", "Draymond", "Green", "PF", 198, 104, 32, 23, 83,
                    attributes=make_attributes(two_point=72, three_point=62, layup=72, close_shot=76, post_scoring=68, free_throw=67,
                                               passing=86, ball_handle=78, drive=62, draw_foul=58, offensive_consistency=74,
                                               perimeter_defense=90, interior_defense=86, steal=82, block=68,
                                               offensive_rebound=72, defensive_rebound=84, defensive_consistency=92,
                                               speed=72, strength=84, stamina=86),
                    tendencies=make_tendencies(shot_tendency=42, three_tendency=24, two_point_tendency=44, drive_tendency=30,
                                               post_tendency=38, close_shot_tendency=36, pass_tendency=88, draw_foul_tendency=42,
                                               steal_tendency=72, block_tendency=48, foul_tendency=55, help_defense_tendency=92,
                                               offensive_rebound_tendency=52, defensive_rebound_tendency=78)),
        make_player("warriors_2022", "Kevon", "Looney", "C", 206, 100, 26, 5, 79,
                    attributes=make_attributes(two_point=72, three_point=25, layup=76, close_shot=78, post_scoring=64, free_throw=60,
                                               passing=58, ball_handle=42, drive=40, draw_foul=52, offensive_consistency=72,
                                               perimeter_defense=62, interior_defense=80, steal=48, block=62,
                                               offensive_rebound=88, defensive_rebound=90, defensive_consistency=84,
                                               speed=56, strength=82, stamina=82),
                    tendencies=make_tendencies(shot_tendency=36, three_tendency=1, two_point_tendency=44, drive_tendency=8,
                                               post_tendency=34, close_shot_tendency=48, pass_tendency=34, draw_foul_tendency=44,
                                               steal_tendency=18, block_tendency=42, foul_tendency=48, help_defense_tendency=72,
                                               offensive_rebound_tendency=84, defensive_rebound_tendency=88)),
        make_player("warriors_2022", "Jordan", "Poole", "SG", 193, 88, 23, 3, 82,
                    attributes=make_attributes(two_point=80, three_point=86, layup=84, close_shot=78, post_scoring=42, free_throw=88,
                                               passing=76, ball_handle=86, drive=84, draw_foul=72, offensive_consistency=78,
                                               perimeter_defense=56, interior_defense=36, steal=52, block=28,
                                               offensive_rebound=34, defensive_rebound=42, defensive_consistency=48,
                                               speed=84, strength=56, stamina=86),
                    tendencies=make_tendencies(shot_tendency=84, three_tendency=80, two_point_tendency=66, drive_tendency=76,
                                               post_tendency=4, close_shot_tendency=46, pass_tendency=54, draw_foul_tendency=64,
                                               steal_tendency=34, block_tendency=6, foul_tendency=42, help_defense_tendency=38,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=24)),
        make_player("warriors_2022", "Otto", "Porter Jr.", "SF", 203, 89, 29, 32, 78,
                    attributes=make_attributes(two_point=75, three_point=82, layup=72, close_shot=74, post_scoring=58, free_throw=79,
                                               passing=64, ball_handle=60, drive=58, draw_foul=44, offensive_consistency=76,
                                               perimeter_defense=74, interior_defense=60, steal=56, block=42,
                                               offensive_rebound=62, defensive_rebound=70, defensive_consistency=74,
                                               speed=62, strength=66, stamina=72),
                    tendencies=make_tendencies(shot_tendency=54, three_tendency=64, two_point_tendency=46, drive_tendency=28,
                                               post_tendency=14, close_shot_tendency=36, pass_tendency=36, draw_foul_tendency=26,
                                               steal_tendency=32, block_tendency=22, foul_tendency=36, help_defense_tendency=62,
                                               offensive_rebound_tendency=38, defensive_rebound_tendency=56)),
        make_player("warriors_2022", "Gary", "Payton II", "SG", 188, 88, 29, 0, 77,
                    attributes=make_attributes(two_point=72, three_point=74, layup=78, close_shot=74, post_scoring=40, free_throw=61,
                                               passing=62, ball_handle=70, drive=74, draw_foul=42, offensive_consistency=72,
                                               perimeter_defense=88, interior_defense=52, steal=82, block=48,
                                               offensive_rebound=46, defensive_rebound=52, defensive_consistency=84,
                                               speed=84, strength=68, stamina=84),
                    tendencies=make_tendencies(shot_tendency=48, three_tendency=38, two_point_tendency=54, drive_tendency=52,
                                               post_tendency=2, close_shot_tendency=42, pass_tendency=34, draw_foul_tendency=30,
                                               steal_tendency=74, block_tendency=24, foul_tendency=38, help_defense_tendency=70,
                                               offensive_rebound_tendency=24, defensive_rebound_tendency=36)),
        make_player("warriors_2022", "Andre", "Iguodala", "SF", 198, 98, 38, 9, 74,
                    attributes=make_attributes(two_point=70, three_point=70, layup=70, close_shot=72, post_scoring=55, free_throw=70,
                                               passing=76, ball_handle=72, drive=60, draw_foul=38, offensive_consistency=68,
                                               perimeter_defense=80, interior_defense=56, steal=70, block=38,
                                               offensive_rebound=40, defensive_rebound=58, defensive_consistency=80,
                                               speed=58, strength=70, stamina=60),
                    tendencies=make_tendencies(shot_tendency=30, three_tendency=28, two_point_tendency=34, drive_tendency=24,
                                               post_tendency=8, close_shot_tendency=30, pass_tendency=62, draw_foul_tendency=20,
                                               steal_tendency=52, block_tendency=18, foul_tendency=32, help_defense_tendency=72,
                                               offensive_rebound_tendency=14, defensive_rebound_tendency=34)),
        make_player("warriors_2022", "Nemanja", "Bjelica", "PF", 208, 106, 34, 8, 74, nationality="Serbia", region_type="EUROPE",
                    attributes=make_attributes(two_point=74, three_point=78, layup=68, close_shot=74, post_scoring=64, free_throw=76,
                                               passing=70, ball_handle=62, drive=46, draw_foul=42, offensive_consistency=70,
                                               perimeter_defense=54, interior_defense=64, steal=44, block=38,
                                               offensive_rebound=56, defensive_rebound=68, defensive_consistency=62,
                                               speed=50, strength=72, stamina=68),
                    tendencies=make_tendencies(shot_tendency=42, three_tendency=52, two_point_tendency=34, drive_tendency=14,
                                               post_tendency=20, close_shot_tendency=30, pass_tendency=48, draw_foul_tendency=26,
                                               steal_tendency=18, block_tendency=16, foul_tendency=34, help_defense_tendency=56,
                                               offensive_rebound_tendency=28, defensive_rebound_tendency=52)),
        make_player("nuggets_2023", "Nikola", "Jokic", "C", 211, 129, 28, 15, 98, nationality="Serbia", region_type="EUROPE",
                    attributes=make_attributes(two_point=96, three_point=83, layup=92, close_shot=98, post_scoring=97, free_throw=83,
                                               passing=99, ball_handle=86, drive=74, draw_foul=82, offensive_consistency=98,
                                               perimeter_defense=70, interior_defense=84, steal=72, block=58,
                                               offensive_rebound=92, defensive_rebound=96, defensive_consistency=84,
                                               speed=60, strength=90, stamina=94),
                    tendencies=make_tendencies(shot_tendency=88, three_tendency=40, two_point_tendency=82, drive_tendency=34,
                                               post_tendency=68, close_shot_tendency=88, pass_tendency=96, draw_foul_tendency=64,
                                               steal_tendency=42, block_tendency=22, foul_tendency=38, help_defense_tendency=70,
                                               offensive_rebound_tendency=74, defensive_rebound_tendency=90)),
        make_player("nuggets_2023", "Jamal", "Murray", "PG", 193, 98, 26, 27, 89,
                    attributes=make_attributes(two_point=86, three_point=88, layup=88, close_shot=82, post_scoring=48, free_throw=84,
                                               passing=84, ball_handle=90, drive=86, draw_foul=68, offensive_consistency=88,
                                               perimeter_defense=70, interior_defense=40, steal=58, block=30,
                                               offensive_rebound=40, defensive_rebound=52, defensive_consistency=66,
                                               speed=82, strength=68, stamina=90),
                    tendencies=make_tendencies(shot_tendency=84, three_tendency=72, two_point_tendency=70, drive_tendency=74,
                                               post_tendency=6, close_shot_tendency=46, pass_tendency=72, draw_foul_tendency=54,
                                               steal_tendency=34, block_tendency=8, foul_tendency=32, help_defense_tendency=48,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=32)),
        make_player("nuggets_2023", "Aaron", "Gordon", "PF", 203, 107, 27, 50, 84,
                    attributes=make_attributes(two_point=82, three_point=72, layup=88, close_shot=80, post_scoring=74, free_throw=61,
                                               passing=70, ball_handle=72, drive=82, draw_foul=60, offensive_consistency=80,
                                               perimeter_defense=80, interior_defense=74, steal=58, block=62,
                                               offensive_rebound=68, defensive_rebound=74, defensive_consistency=80,
                                               speed=80, strength=82, stamina=86),
                    tendencies=make_tendencies(shot_tendency=64, three_tendency=34, two_point_tendency=66, drive_tendency=62,
                                               post_tendency=28, close_shot_tendency=54, pass_tendency=40, draw_foul_tendency=48,
                                               steal_tendency=36, block_tendency=34, foul_tendency=42, help_defense_tendency=68,
                                               offensive_rebound_tendency=44, defensive_rebound_tendency=58)),
        make_player("nuggets_2023", "Michael", "Porter Jr.", "SF", 208, 99, 24, 1, 83,
                    attributes=make_attributes(two_point=80, three_point=86, layup=76, close_shot=80, post_scoring=62, free_throw=79,
                                               passing=60, ball_handle=60, drive=64, draw_foul=40, offensive_consistency=82,
                                               perimeter_defense=68, interior_defense=54, steal=46, block=46,
                                               offensive_rebound=58, defensive_rebound=74, defensive_consistency=66,
                                               speed=70, strength=68, stamina=80),
                    tendencies=make_tendencies(shot_tendency=76, three_tendency=72, two_point_tendency=54, drive_tendency=36,
                                               post_tendency=14, close_shot_tendency=38, pass_tendency=24, draw_foul_tendency=22,
                                               steal_tendency=22, block_tendency=20, foul_tendency=30, help_defense_tendency=54,
                                               offensive_rebound_tendency=30, defensive_rebound_tendency=52)),
        make_player("nuggets_2023", "Kentavious", "Caldwell-Pope", "SG", 196, 93, 30, 5, 80,
                    attributes=make_attributes(two_point=74, three_point=84, layup=74, close_shot=72, post_scoring=42, free_throw=80,
                                               passing=64, ball_handle=68, drive=68, draw_foul=38, offensive_consistency=76,
                                               perimeter_defense=84, interior_defense=44, steal=70, block=32,
                                               offensive_rebound=38, defensive_rebound=52, defensive_consistency=82,
                                               speed=78, strength=64, stamina=84),
                    tendencies=make_tendencies(shot_tendency=58, three_tendency=68, two_point_tendency=40, drive_tendency=34,
                                               post_tendency=4, close_shot_tendency=26, pass_tendency=28, draw_foul_tendency=18,
                                               steal_tendency=52, block_tendency=12, foul_tendency=34, help_defense_tendency=70,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=34)),
        make_player("nuggets_2023", "Bruce", "Brown", "SG", 193, 91, 26, 11, 80,
                    attributes=make_attributes(two_point=78, three_point=76, layup=80, close_shot=76, post_scoring=44, free_throw=76,
                                               passing=70, ball_handle=74, drive=76, draw_foul=52, offensive_consistency=78,
                                               perimeter_defense=80, interior_defense=50, steal=62, block=38,
                                               offensive_rebound=44, defensive_rebound=58, defensive_consistency=80,
                                               speed=80, strength=70, stamina=86),
                    tendencies=make_tendencies(shot_tendency=60, three_tendency=42, two_point_tendency=54, drive_tendency=58,
                                               post_tendency=4, close_shot_tendency=40, pass_tendency=40, draw_foul_tendency=34,
                                               steal_tendency=46, block_tendency=14, foul_tendency=34, help_defense_tendency=66,
                                               offensive_rebound_tendency=16, defensive_rebound_tendency=40)),
        make_player("nuggets_2023", "Christian", "Braun", "SG", 198, 100, 22, 0, 76,
                    attributes=make_attributes(two_point=72, three_point=74, layup=76, close_shot=72, post_scoring=46, free_throw=72,
                                               passing=62, ball_handle=64, drive=72, draw_foul=46, offensive_consistency=72,
                                               perimeter_defense=78, interior_defense=52, steal=56, block=34,
                                               offensive_rebound=46, defensive_rebound=56, defensive_consistency=76,
                                               speed=78, strength=72, stamina=82),
                    tendencies=make_tendencies(shot_tendency=46, three_tendency=34, two_point_tendency=46, drive_tendency=48,
                                               post_tendency=2, close_shot_tendency=36, pass_tendency=28, draw_foul_tendency=24,
                                               steal_tendency=34, block_tendency=12, foul_tendency=34, help_defense_tendency=62,
                                               offensive_rebound_tendency=18, defensive_rebound_tendency=34)),
        make_player("nuggets_2023", "Jeff", "Green", "PF", 203, 107, 36, 32, 74,
                    attributes=make_attributes(two_point=72, three_point=72, layup=74, close_shot=74, post_scoring=58, free_throw=74,
                                               passing=60, ball_handle=58, drive=60, draw_foul=38, offensive_consistency=70,
                                               perimeter_defense=60, interior_defense=60, steal=42, block=38,
                                               offensive_rebound=50, defensive_rebound=58, defensive_consistency=66,
                                               speed=64, strength=70, stamina=70),
                    tendencies=make_tendencies(shot_tendency=42, three_tendency=28, two_point_tendency=40, drive_tendency=24,
                                               post_tendency=14, close_shot_tendency=28, pass_tendency=24, draw_foul_tendency=18,
                                               steal_tendency=20, block_tendency=14, foul_tendency=32, help_defense_tendency=50,
                                               offensive_rebound_tendency=20, defensive_rebound_tendency=36)),
        make_player("nuggets_2023", "Reggie", "Jackson", "PG", 188, 94, 33, 7, 74,
                    attributes=make_attributes(two_point=74, three_point=76, layup=74, close_shot=70, post_scoring=38, free_throw=78,
                                               passing=68, ball_handle=76, drive=70, draw_foul=42, offensive_consistency=70,
                                               perimeter_defense=54, interior_defense=34, steal=42, block=18,
                                               offensive_rebound=28, defensive_rebound=36, defensive_consistency=52,
                                               speed=74, strength=56, stamina=72),
                    tendencies=make_tendencies(shot_tendency=54, three_tendency=44, two_point_tendency=42, drive_tendency=38,
                                               post_tendency=4, close_shot_tendency=30, pass_tendency=38, draw_foul_tendency=24,
                                               steal_tendency=20, block_tendency=2, foul_tendency=28, help_defense_tendency=34,
                                               offensive_rebound_tendency=8, defensive_rebound_tendency=22)),
        make_player("nuggets_2023", "DeAndre", "Jordan", "C", 211, 120, 34, 6, 73,
                    attributes=make_attributes(two_point=74, three_point=25, layup=80, close_shot=78, post_scoring=60, free_throw=48,
                                               passing=56, ball_handle=36, drive=34, draw_foul=50, offensive_consistency=68,
                                               perimeter_defense=44, interior_defense=74, steal=34, block=64,
                                               offensive_rebound=80, defensive_rebound=82, defensive_consistency=72,
                                               speed=54, strength=84, stamina=68),
                    tendencies=make_tendencies(shot_tendency=24, three_tendency=1, two_point_tendency=30, drive_tendency=4,
                                               post_tendency=24, close_shot_tendency=36, pass_tendency=18, draw_foul_tendency=24,
                                               steal_tendency=8, block_tendency=36, foul_tendency=44, help_defense_tendency=58,
                                               offensive_rebound_tendency=70, defensive_rebound_tendency=74)),
        make_player("celtics_2024", "Jayson", "Tatum", "SF", 203, 95, 26, 0, 95,
                    attributes=make_attributes(two_point=90, three_point=89, layup=88, close_shot=86, post_scoring=84, free_throw=85,
                                               passing=84, ball_handle=88, drive=88, draw_foul=78, offensive_consistency=94,
                                               perimeter_defense=86, interior_defense=68, steal=68, block=58,
                                               offensive_rebound=58, defensive_rebound=82, defensive_consistency=86,
                                               speed=84, strength=78, stamina=94),
                    tendencies=make_tendencies(shot_tendency=92, three_tendency=72, two_point_tendency=76, drive_tendency=74,
                                               post_tendency=28, close_shot_tendency=50, pass_tendency=68, draw_foul_tendency=62,
                                               steal_tendency=38, block_tendency=20, foul_tendency=34, help_defense_tendency=68,
                                               offensive_rebound_tendency=30, defensive_rebound_tendency=54)),
        make_player("celtics_2024", "Jaylen", "Brown", "SG", 198, 101, 27, 7, 93,
                    attributes=make_attributes(two_point=88, three_point=82, layup=90, close_shot=84, post_scoring=76, free_throw=74,
                                               passing=76, ball_handle=82, drive=90, draw_foul=76, offensive_consistency=90,
                                               perimeter_defense=82, interior_defense=60, steal=62, block=44,
                                               offensive_rebound=52, defensive_rebound=68, defensive_consistency=82,
                                               speed=86, strength=78, stamina=92),
                    tendencies=make_tendencies(shot_tendency=86, three_tendency=54, two_point_tendency=72, drive_tendency=82,
                                               post_tendency=16, close_shot_tendency=52, pass_tendency=44, draw_foul_tendency=58,
                                               steal_tendency=34, block_tendency=12, foul_tendency=34, help_defense_tendency=60,
                                               offensive_rebound_tendency=22, defensive_rebound_tendency=42)),
        make_player("celtics_2024", "Jrue", "Holiday", "PG", 193, 93, 34, 4, 87,
                    attributes=make_attributes(two_point=82, three_point=82, layup=80, close_shot=76, post_scoring=58, free_throw=82,
                                               passing=86, ball_handle=84, drive=78, draw_foul=56, offensive_consistency=84,
                                               perimeter_defense=92, interior_defense=58, steal=84, block=42,
                                               offensive_rebound=42, defensive_rebound=62, defensive_consistency=92,
                                               speed=76, strength=76, stamina=88),
                    tendencies=make_tendencies(shot_tendency=62, three_tendency=52, two_point_tendency=50, drive_tendency=46,
                                               post_tendency=8, close_shot_tendency=32, pass_tendency=70, draw_foul_tendency=34,
                                               steal_tendency=58, block_tendency=18, foul_tendency=34, help_defense_tendency=78,
                                               offensive_rebound_tendency=14, defensive_rebound_tendency=40)),
        make_player("celtics_2024", "Derrick", "White", "SG", 193, 86, 29, 9, 86,
                    attributes=make_attributes(two_point=82, three_point=84, layup=80, close_shot=78, post_scoring=42, free_throw=88,
                                               passing=78, ball_handle=80, drive=76, draw_foul=50, offensive_consistency=84,
                                               perimeter_defense=88, interior_defense=50, steal=72, block=52,
                                               offensive_rebound=36, defensive_rebound=56, defensive_consistency=88,
                                               speed=78, strength=62, stamina=90),
                    tendencies=make_tendencies(shot_tendency=68, three_tendency=58, two_point_tendency=48, drive_tendency=44,
                                               post_tendency=4, close_shot_tendency=30, pass_tendency=54, draw_foul_tendency=28,
                                               steal_tendency=46, block_tendency=22, foul_tendency=32, help_defense_tendency=76,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=36)),
        make_player("celtics_2024", "Kristaps", "Porzingis", "C", 221, 109, 28, 8, 88, nationality="Latvia", region_type="EUROPE",
                    attributes=make_attributes(two_point=84, three_point=82, layup=78, close_shot=86, post_scoring=82, free_throw=84,
                                               passing=66, ball_handle=58, drive=54, draw_foul=66, offensive_consistency=86,
                                               perimeter_defense=64, interior_defense=84, steal=44, block=82,
                                               offensive_rebound=62, defensive_rebound=78, defensive_consistency=82,
                                               speed=64, strength=76, stamina=80),
                    tendencies=make_tendencies(shot_tendency=68, three_tendency=48, two_point_tendency=58, drive_tendency=18,
                                               post_tendency=34, close_shot_tendency=42, pass_tendency=28, draw_foul_tendency=44,
                                               steal_tendency=18, block_tendency=48, foul_tendency=38, help_defense_tendency=72,
                                               offensive_rebound_tendency=28, defensive_rebound_tendency=56)),
        make_player("celtics_2024", "Al", "Horford", "C", 206, 109, 38, 42, 82,
                    attributes=make_attributes(two_point=76, three_point=80, layup=68, close_shot=76, post_scoring=72, free_throw=82,
                                               passing=72, ball_handle=52, drive=40, draw_foul=42, offensive_consistency=78,
                                               perimeter_defense=72, interior_defense=80, steal=44, block=62,
                                               offensive_rebound=56, defensive_rebound=78, defensive_consistency=84,
                                               speed=54, strength=78, stamina=74),
                    tendencies=make_tendencies(shot_tendency=46, three_tendency=42, two_point_tendency=36, drive_tendency=8,
                                               post_tendency=22, close_shot_tendency=26, pass_tendency=42, draw_foul_tendency=20,
                                               steal_tendency=16, block_tendency=28, foul_tendency=34, help_defense_tendency=76,
                                               offensive_rebound_tendency=20, defensive_rebound_tendency=58)),
        make_player("celtics_2024", "Payton", "Pritchard", "PG", 185, 88, 26, 11, 79,
                    attributes=make_attributes(two_point=76, three_point=84, layup=74, close_shot=72, post_scoring=35, free_throw=88,
                                               passing=74, ball_handle=80, drive=70, draw_foul=32, offensive_consistency=78,
                                               perimeter_defense=64, interior_defense=32, steal=50, block=20,
                                               offensive_rebound=28, defensive_rebound=42, defensive_consistency=62,
                                               speed=78, strength=52, stamina=84),
                    tendencies=make_tendencies(shot_tendency=58, three_tendency=62, two_point_tendency=38, drive_tendency=34,
                                               post_tendency=2, close_shot_tendency=26, pass_tendency=42, draw_foul_tendency=18,
                                               steal_tendency=28, block_tendency=2, foul_tendency=26, help_defense_tendency=44,
                                               offensive_rebound_tendency=8, defensive_rebound_tendency=22)),
        make_player("celtics_2024", "Sam", "Hauser", "SF", 201, 98, 26, 30, 78,
                    attributes=make_attributes(two_point=74, three_point=86, layup=68, close_shot=70, post_scoring=42, free_throw=80,
                                               passing=58, ball_handle=54, drive=48, draw_foul=28, offensive_consistency=76,
                                               perimeter_defense=72, interior_defense=44, steal=42, block=28,
                                               offensive_rebound=34, defensive_rebound=48, defensive_consistency=72,
                                               speed=62, strength=64, stamina=80),
                    tendencies=make_tendencies(shot_tendency=56, three_tendency=72, two_point_tendency=24, drive_tendency=16,
                                               post_tendency=4, close_shot_tendency=18, pass_tendency=20, draw_foul_tendency=12,
                                               steal_tendency=18, block_tendency=8, foul_tendency=28, help_defense_tendency=52,
                                               offensive_rebound_tendency=10, defensive_rebound_tendency=26)),
        make_player("celtics_2024", "Luke", "Kornet", "C", 216, 113, 28, 40, 75,
                    attributes=make_attributes(two_point=74, three_point=25, layup=78, close_shot=76, post_scoring=58, free_throw=70,
                                               passing=54, ball_handle=36, drive=30, draw_foul=38, offensive_consistency=70,
                                               perimeter_defense=42, interior_defense=72, steal=28, block=68,
                                               offensive_rebound=66, defensive_rebound=74, defensive_consistency=74,
                                               speed=48, strength=74, stamina=72),
                    tendencies=make_tendencies(shot_tendency=24, three_tendency=1, two_point_tendency=30, drive_tendency=4,
                                               post_tendency=18, close_shot_tendency=36, pass_tendency=16, draw_foul_tendency=18,
                                               steal_tendency=10, block_tendency=42, foul_tendency=42, help_defense_tendency=64,
                                               offensive_rebound_tendency=46, defensive_rebound_tendency=60)),
        make_player("celtics_2024", "Xavier", "Tillman", "PF", 203, 111, 25, 26, 75,
                    attributes=make_attributes(two_point=72, three_point=58, layup=72, close_shot=74, post_scoring=66, free_throw=64,
                                               passing=60, ball_handle=46, drive=42, draw_foul=42, offensive_consistency=70,
                                               perimeter_defense=66, interior_defense=74, steal=42, block=46,
                                               offensive_rebound=64, defensive_rebound=70, defensive_consistency=74,
                                               speed=58, strength=78, stamina=74),
                    tendencies=make_tendencies(shot_tendency=28, three_tendency=10, two_point_tendency=32, drive_tendency=10,
                                               post_tendency=24, close_shot_tendency=30, pass_tendency=22, draw_foul_tendency=18,
                                               steal_tendency=14, block_tendency=16, foul_tendency=38, help_defense_tendency=62,
                                               offensive_rebound_tendency=34, defensive_rebound_tendency=52)),
        make_player("thunder_2025", "Shai", "Gilgeous-Alexander", "PG", 198, 88, 27, 2, 97, nationality="Canada",
                    attributes=make_attributes(two_point=94, three_point=82, layup=96, close_shot=92, post_scoring=60, free_throw=90,
                                               passing=90, ball_handle=96, drive=96, draw_foul=92, offensive_consistency=98,
                                               perimeter_defense=82, interior_defense=56, steal=72, block=44,
                                               offensive_rebound=42, defensive_rebound=68, defensive_consistency=82,
                                               speed=90, strength=72, stamina=96),
                    tendencies=make_tendencies(shot_tendency=94, three_tendency=42, two_point_tendency=86, drive_tendency=94,
                                               post_tendency=6, close_shot_tendency=68, pass_tendency=70, draw_foul_tendency=92,
                                               steal_tendency=44, block_tendency=12, foul_tendency=30, help_defense_tendency=60,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=38)),
        make_player("thunder_2025", "Jalen", "Williams", "SF", 196, 95, 24, 8, 89,
                    attributes=make_attributes(two_point=86, three_point=82, layup=88, close_shot=84, post_scoring=66, free_throw=81,
                                               passing=80, ball_handle=82, drive=86, draw_foul=70, offensive_consistency=88,
                                               perimeter_defense=82, interior_defense=60, steal=66, block=50,
                                               offensive_rebound=50, defensive_rebound=66, defensive_consistency=82,
                                               speed=82, strength=72, stamina=90),
                    tendencies=make_tendencies(shot_tendency=78, three_tendency=50, two_point_tendency=66, drive_tendency=72,
                                               post_tendency=14, close_shot_tendency=48, pass_tendency=56, draw_foul_tendency=52,
                                               steal_tendency=36, block_tendency=16, foul_tendency=30, help_defense_tendency=64,
                                               offensive_rebound_tendency=18, defensive_rebound_tendency=40)),
        make_player("thunder_2025", "Chet", "Holmgren", "C", 216, 94, 23, 7, 89,
                    attributes=make_attributes(two_point=82, three_point=80, layup=82, close_shot=84, post_scoring=72, free_throw=82,
                                               passing=72, ball_handle=66, drive=62, draw_foul=58, offensive_consistency=84,
                                               perimeter_defense=72, interior_defense=88, steal=56, block=90,
                                               offensive_rebound=60, defensive_rebound=80, defensive_consistency=86,
                                               speed=76, strength=66, stamina=86),
                    tendencies=make_tendencies(shot_tendency=64, three_tendency=40, two_point_tendency=52, drive_tendency=20,
                                               post_tendency=26, close_shot_tendency=34, pass_tendency=38, draw_foul_tendency=34,
                                               steal_tendency=22, block_tendency=58, foul_tendency=40, help_defense_tendency=78,
                                               offensive_rebound_tendency=24, defensive_rebound_tendency=58)),
        make_player("thunder_2025", "Luguentz", "Dort", "SG", 193, 100, 26, 5, 81, nationality="Canada",
                    attributes=make_attributes(two_point=74, three_point=78, layup=74, close_shot=72, post_scoring=46, free_throw=78,
                                               passing=60, ball_handle=68, drive=68, draw_foul=42, offensive_consistency=74,
                                               perimeter_defense=92, interior_defense=56, steal=70, block=38,
                                               offensive_rebound=44, defensive_rebound=58, defensive_consistency=90,
                                               speed=78, strength=82, stamina=88),
                    tendencies=make_tendencies(shot_tendency=58, three_tendency=52, two_point_tendency=40, drive_tendency=36,
                                               post_tendency=4, close_shot_tendency=26, pass_tendency=22, draw_foul_tendency=20,
                                               steal_tendency=52, block_tendency=14, foul_tendency=42, help_defense_tendency=78,
                                               offensive_rebound_tendency=14, defensive_rebound_tendency=34)),
        make_player("thunder_2025", "Isaiah", "Hartenstein", "C", 213, 113, 27, 55, 83,
                    attributes=make_attributes(two_point=76, three_point=25, layup=78, close_shot=80, post_scoring=68, free_throw=70,
                                               passing=74, ball_handle=50, drive=36, draw_foul=54, offensive_consistency=76,
                                               perimeter_defense=58, interior_defense=82, steal=48, block=62,
                                               offensive_rebound=84, defensive_rebound=88, defensive_consistency=82,
                                               speed=56, strength=82, stamina=84),
                    tendencies=make_tendencies(shot_tendency=30, three_tendency=1, two_point_tendency=34, drive_tendency=4,
                                               post_tendency=24, close_shot_tendency=38, pass_tendency=42, draw_foul_tendency=28,
                                               steal_tendency=20, block_tendency=34, foul_tendency=42, help_defense_tendency=72,
                                               offensive_rebound_tendency=76, defensive_rebound_tendency=84)),
        make_player("thunder_2025", "Alex", "Caruso", "SG", 196, 84, 31, 9, 80,
                    attributes=make_attributes(two_point=72, three_point=78, layup=74, close_shot=70, post_scoring=38, free_throw=78,
                                               passing=72, ball_handle=74, drive=68, draw_foul=38, offensive_consistency=74,
                                               perimeter_defense=90, interior_defense=48, steal=84, block=34,
                                               offensive_rebound=32, defensive_rebound=50, defensive_consistency=92,
                                               speed=76, strength=60, stamina=84),
                    tendencies=make_tendencies(shot_tendency=44, three_tendency=42, two_point_tendency=30, drive_tendency=24,
                                               post_tendency=2, close_shot_tendency=22, pass_tendency=34, draw_foul_tendency=16,
                                               steal_tendency=72, block_tendency=10, foul_tendency=38, help_defense_tendency=80,
                                               offensive_rebound_tendency=8, defensive_rebound_tendency=28)),
        make_player("thunder_2025", "Cason", "Wallace", "PG", 193, 88, 21, 22, 79,
                    attributes=make_attributes(two_point=74, three_point=78, layup=74, close_shot=70, post_scoring=40, free_throw=78,
                                               passing=72, ball_handle=76, drive=70, draw_foul=36, offensive_consistency=76,
                                               perimeter_defense=82, interior_defense=44, steal=68, block=28,
                                               offensive_rebound=30, defensive_rebound=44, defensive_consistency=82,
                                               speed=78, strength=60, stamina=84),
                    tendencies=make_tendencies(shot_tendency=42, three_tendency=40, two_point_tendency=30, drive_tendency=28,
                                               post_tendency=2, close_shot_tendency=20, pass_tendency=42, draw_foul_tendency=14,
                                               steal_tendency=46, block_tendency=8, foul_tendency=28, help_defense_tendency=66,
                                               offensive_rebound_tendency=8, defensive_rebound_tendency=24)),
        make_player("thunder_2025", "Aaron", "Wiggins", "SF", 198, 86, 26, 21, 77,
                    attributes=make_attributes(two_point=74, three_point=76, layup=76, close_shot=72, post_scoring=44, free_throw=72,
                                               passing=58, ball_handle=60, drive=68, draw_foul=34, offensive_consistency=72,
                                               perimeter_defense=74, interior_defense=48, steal=50, block=26,
                                               offensive_rebound=34, defensive_rebound=50, defensive_consistency=74,
                                               speed=74, strength=62, stamina=80),
                    tendencies=make_tendencies(shot_tendency=44, three_tendency=38, two_point_tendency=34, drive_tendency=30,
                                               post_tendency=4, close_shot_tendency=24, pass_tendency=22, draw_foul_tendency=16,
                                               steal_tendency=24, block_tendency=6, foul_tendency=26, help_defense_tendency=54,
                                               offensive_rebound_tendency=12, defensive_rebound_tendency=28)),
        make_player("thunder_2025", "Isaiah", "Joe", "SG", 193, 75, 25, 11, 78,
                    attributes=make_attributes(two_point=74, three_point=86, layup=70, close_shot=68, post_scoring=36, free_throw=82,
                                               passing=58, ball_handle=64, drive=56, draw_foul=28, offensive_consistency=78,
                                               perimeter_defense=68, interior_defense=36, steal=44, block=20,
                                               offensive_rebound=22, defensive_rebound=38, defensive_consistency=68,
                                               speed=72, strength=50, stamina=82),
                    tendencies=make_tendencies(shot_tendency=58, three_tendency=72, two_point_tendency=22, drive_tendency=16,
                                               post_tendency=2, close_shot_tendency=16, pass_tendency=18, draw_foul_tendency=12,
                                               steal_tendency=18, block_tendency=4, foul_tendency=24, help_defense_tendency=46,
                                               offensive_rebound_tendency=6, defensive_rebound_tendency=18)),
        make_player("thunder_2025", "Jaylin", "Williams", "PF", 206, 109, 23, 6, 77,
                    attributes=make_attributes(two_point=72, three_point=74, layup=70, close_shot=72, post_scoring=58, free_throw=74,
                                               passing=68, ball_handle=52, drive=42, draw_foul=40, offensive_consistency=72,
                                               perimeter_defense=64, interior_defense=70, steal=42, block=42,
                                               offensive_rebound=56, defensive_rebound=68, defensive_consistency=72,
                                               speed=58, strength=74, stamina=78),
                    tendencies=make_tendencies(shot_tendency=32, three_tendency=20, two_point_tendency=28, drive_tendency=12,
                                               post_tendency=18, close_shot_tendency=24, pass_tendency=34, draw_foul_tendency=18,
                                               steal_tendency=16, block_tendency=14, foul_tendency=30, help_defense_tendency=60,
                                               offensive_rebound_tendency=24, defensive_rebound_tendency=44)),
    ]


import csv

def seed_data(conn: sqlite3.Connection) -> tuple[int, int]:
    teams = get_teams()
    players = get_players()

    for team in teams:
        insert_team(conn, team)

    next_player_id = 1000
    for player in players:
        insert_player_base(conn, next_player_id, player)
        insert_player_attributes(conn, next_player_id, player["attributes"])
        insert_player_tendencies(conn, next_player_id, player["tendencies"])
        next_player_id += 1

    # Insert historical players
    hist_csv_path = PROJECT_ROOT / "data" / "source" / "historical_champion_player_stats.csv"
    hist_players_count = 0
    if hist_csv_path.exists():
        with open(hist_csv_path, 'r', encoding='utf-8-sig') as f:
            reader = csv.DictReader(f)
            for row in reader:
                player_id = int(row['player_id'])
                hp = make_player(
                    team_id=row['team_id'],
                    first_name=row['first_name'],
                    last_name=row['last_name'],
                    position=row['position'],
                    height_cm=int(row['height_cm']),
                    weight_kg=int(row['weight_kg']),
                    age=int(row['age']),
                    jersey_number=0,
                    overall=70,
                    display_name=row['display_name']
                )
                insert_player_base(conn, player_id, hp)
                insert_player_attributes(conn, player_id, hp["attributes"])
                insert_player_tendencies(conn, player_id, hp["tendencies"])
                hist_players_count += 1

    return len(teams), len(players) + hist_players_count


def main() -> None:
    DB_PATH.parent.mkdir(parents=True, exist_ok=True)

    conn = sqlite3.connect(DB_PATH)
    try:
        conn.execute("PRAGMA foreign_keys = ON;")
        create_schema(conn)
        team_count, player_count = seed_data(conn)
        conn.commit()
    finally:
        conn.close()

    print(f"Unity database generated: {DB_PATH}")
    print(f"Teams inserted: {team_count}")
    print(f"Players inserted: {player_count}")
    print("Player ID range: 1000 - {0}".format(1000 + player_count - 1))
    print("\n--- Running Validation ---")
    
    try:
        import validate_unity_database
        validate_unity_database.validate()
    except ImportError:
        print("[WARN] validate_unity_database.py not found. Skipping validation.")


if __name__ == "__main__":
    main()
