# -*- coding: utf-8 -*-
import sqlite3, os

db_path = os.path.join(os.path.dirname(__file__),
                       r'..\Assets\StreamingAssets\game.db')
conn = sqlite3.connect(db_path)
conn.execute("PRAGMA foreign_keys = ON")
c = conn.cursor()

c.execute("""
CREATE TABLE IF NOT EXISTS traits (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name_key TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    category TEXT NOT NULL DEFAULT '',
    description_1 TEXT NOT NULL DEFAULT '',
    description_2 TEXT NOT NULL DEFAULT '',
    description_3 TEXT NOT NULL DEFAULT ''
)
""")

c.execute("""
CREATE TABLE IF NOT EXISTS player_traits (
    player_id INTEGER NOT NULL,
    trait_id  INTEGER NOT NULL,
    star_level INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (player_id, trait_id),
    FOREIGN KEY (player_id) REFERENCES players (id) ON UPDATE CASCADE ON DELETE CASCADE,
    FOREIGN KEY (trait_id)  REFERENCES traits  (id) ON UPDATE CASCADE ON DELETE CASCADE
)
""")

traits = [
    ("clutch_performer", "关键先生", "Scoring",
     "第四节剩余5分钟内分差<5分时，投篮命中率+1.5%",
     "第四节剩余5分钟内分差<5分时，投篮命中率+2.5%",
     "第四节剩余5分钟内分差<5分时，投篮命中率+4%"),
    ("catch_and_shoot", "接球即投", "Shooting",
     "接到队友传球后不运球直接出手，投篮命中率+1%",
     "接到队友传球后不运球直接出手，投篮命中率+2%",
     "接到队友传球后不运球直接出手，投篮命中率+3%"),
    ("needle_threader", "传切利器", "Playmaking",
     "在拥挤区域也能精准送出传球，个人失误率降低0.8%",
     "在拥挤区域也能精准送出传球，个人失误率降低1.5%",
     "在拥挤区域也能精准送出传球，个人失误率降低2%"),
    ("clamps", "封锁专家", "Defense",
     "防守对手运球出手时横向移动迅捷，对手有效防守属性×1.12，迫使对手命中率下降",
     "防守对手运球出手时横向移动迅捷，对手有效防守属性×1.22，迫使对手命中率下降",
     "防守对手运球出手时横向移动迅捷，对手有效防守属性×1.32，迫使对手命中率下降"),
    ("intimidator", "硬汉防守", "Defense",
     "强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低2%",
     "强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低3%",
     "强壮身躯令对手心理受压，防守篮下/低位/中距离时对手命中率上限降低4%"),
    ("volume_shooter", "量产型", "Shooting",
     "出手越多越进入状态：本场出手达10次后，每多出手1次命中率+0.1%，上限+1%",
     "出手越多越进入状态：本场出手达8次后，每多出手1次命中率+0.2%，上限+2%",
     "出手越多越进入状态：本场出手达6次后，每多出手1次命中率+0.3%，上限+3%"),
    ("comeback_kid", "慢热型", "Scoring",
     "球队落后5分以上时激发斗志，个人投篮命中率+1%",
     "球队落后5分以上时+1.5%，落后15分以上时+2.5%，越难越勇",
     "球队落后5分以上时+2%，落后15分以上时+3.5%，越难越勇"),
]

c.executemany("""
INSERT OR IGNORE INTO traits
    (name_key, display_name, category, description_1, description_2, description_3)
VALUES (?, ?, ?, ?, ?, ?)
""", traits)

conn.commit()

rows = c.execute("SELECT id, name_key, display_name FROM traits ORDER BY id").fetchall()
print(f"traits 表共 {len(rows)} 条：")
for row in rows:
    print(f"  [{row[0]:2d}] {row[1]:<20s}  {row[2]}")

conn.close()
