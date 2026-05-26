using System;
using BasketballManager.Core.Enums;
using BasketballManager.Core.Models;
using UnityEngine;

namespace BasketballManager.Core.Services
{
    public sealed class PlayerGenerator
    {
        private static readonly string[] ChineseSurnames =
        {
            "李", "王", "张", "刘", "陈", "杨", "黄", "赵", "吴", "周",
            "徐", "孙", "马", "朱", "胡", "郭", "何", "高", "林", "郑",
            "谢", "罗", "唐", "韩", "冯", "邓", "曹", "彭", "邹", "蒋"
        };

        private static readonly string[] ChineseGivenNames =
        {
            "伟", "涛", "杰", "强", "磊", "洋", "勇", "鹏", "超", "飞",
            "博", "明", "旭", "宇", "凯", "浩", "建", "军", "浩然", "志远",
            "嘉豪", "天航", "晓宇", "俊杰", "文博", "宸熙", "浩宸", "子轩", "宇轩", "俊宇"
        };

        private static readonly string[] EnglishFirstNames =
        {
            "James", "Kevin", "Anthony", "Damian", "Joel", "Jayson", "Luka", "Nikola",
            "Pascal", "Khris", "Bradley", "Rudy", "Karl", "Devin", "Donovan", "Zach",
            "Tyler", "CJ", "Draymond", "RJ", "Jaylen", "Scottie", "Shai", "Tyrese",
            "Jalen", "Miles", "Andrew", "Gordon", "Jrue", "Nicolas", "Brook", "Al",
            "Bobby", "Keldon", "Anfernee", "Jordan", "Desmond", "Darius", "Aaron",
            "Jerami", "Evan", "Matisse", "Garrison", "Cameron", "Justin", "Cole",
            "Duncan", "Obi", "Josh", "Isaiah", "Malachi", "Davion", "Alperen",
            "Cade", "Franz", "Ayo", "Tre", "Immanuel", "Keon", "Naz", "Coby",
            "Killian", "DeAndre", "Jarrett", "Christian", "Robert", "Marcus",
            "Trae", "Zion", "Paolo", "Victor", "Wembanyama", "Scoot", "Brandon",
            "Walker", "Jabari", "Jaden", "Dyson", "GG", "Gradey", "Jarace"
        };

        private static readonly string[] EnglishLastNames =
        {
            "Davis", "Durant", "Curry", "Leonard", "Lillard", "Embiid", "Tatum",
            "Doncic", "Jokic", "Adebayo", "Siakam", "Middleton", "Beal", "Gobert",
            "Towns", "Booker", "Mitchell", "LaVine", "Morant", "Herro", "McCollum",
            "Green", "Fox", "Barrett", "Brown", "Barnes", "Gilgeous-Alexander",
            "Maxey", "Williams", "Bridges", "Wiggins", "Hayward", "Holiday",
            "Batum", "Lopez", "Horford", "Vucevic", "Portis", "Garland",
            "Murray", "Grant", "Johnson", "Love", "McDaniels", "Hunter",
            "Vanderbilt", "Quickley", "Smith", "Reed", "Jordan", "Nnaji",
            "Hart", "Hampton", "Thompson", "Warren", "Rozier", "Toppin",
            "Reddish", "Boucher", "Nunn", "Porter", "Okeke", "Okoro",
            "Sexton", "Ball", "Banchero", "Henderson", "Miller", "Jackson",
            "Robinson", "Morris", "Walker", "Carter", "Taylor", "Harris"
        };

        private readonly System.Random _rng;

        public PlayerGenerator(System.Random rng = null)
        {
            _rng = rng ?? new System.Random();
        }

        /// <summary>
        /// 生成单个新秀。OVR 范围由调用方传入（按选秀顺位决定）。
        /// </summary>
        public Player Generate(string teamId, int ovrMin, int ovrMax, int draftYear)
        {
            bool isChinese = _rng.NextDouble() < 0.2;
            string firstName, lastName;
            NameOrder nameOrder;

            if (isChinese)
            {
                lastName  = ChineseSurnames[_rng.Next(ChineseSurnames.Length)];
                firstName = ChineseGivenNames[_rng.Next(ChineseGivenNames.Length)];
                nameOrder = NameOrder.EASTERN;
            }
            else
            {
                firstName = EnglishFirstNames[_rng.Next(EnglishFirstNames.Length)];
                lastName  = EnglishLastNames[_rng.Next(EnglishLastNames.Length)];
                nameOrder = NameOrder.WESTERN;
            }

            var positions = (Position[])Enum.GetValues(typeof(Position));
            var position  = positions[_rng.Next(positions.Length)];

            int ovr = _rng.Next(ovrMin, ovrMax + 1);
            int age = _rng.Next(18, 23);

            var player = new Player
            {
                TeamId    = teamId,
                FirstName = firstName,
                LastName  = lastName,
                NameOrder = nameOrder,
                Position  = position,
                HeightCm  = GetHeightForPosition(position),
                WeightKg  = GetWeightForPosition(position),
                Age       = age,
                JerseyNumber = _rng.Next(0, 100),
                IsCurrent = true,
                PotentialMin  = Mathf.Min(99, ovr + 3),
                PotentialMax  = Mathf.Min(99, ovr + 20),
                PeakAgeStart  = 25,
                PeakAgeEnd    = 30,
                ContractYears = 3,
            };

            player.Attributes = GenerateAttributes(position, ovr);
            player.Tendencies = GenerateTendencies(position);
            player.Overall    = ovr;

            return player;
        }

        private PlayerAttributes GenerateAttributes(Position pos, int ovr)
        {
            int Base(int min, int spread) => Mathf.Clamp(min + _rng.Next(spread) + (ovr - 70), 40, 99);

            bool isG = pos == Position.PG || pos == Position.SG;
            bool isC = pos == Position.C;

            return new PlayerAttributes
            {
                TwoPoint     = Base(isC ? 58 : 50, 15),
                ThreePoint   = Base(isG ? 52 : 40, 18),
                Layup        = Base(isG ? 55 : 48, 15),
                CloseShot    = Base(isC ? 58 : 50, 15),
                PostScoring  = Base(isC ? 55 : 40, 15),
                FreeThrow    = Base(isG ? 52 : 44, 18),
                Passing      = Base(isG ? 55 : 46, 15),
                BallHandle   = Base(isG ? 58 : 44, 15),
                Drive        = Base(isG ? 55 : 46, 15),
                DrawFoul     = Base(50, 15),
                OffensiveConsistency = Base(52, 12),
                PerimeterDefense = Base(isG ? 52 : 44, 15),
                InteriorDefense  = Base(isC ? 55 : 44, 15),
                Steal        = Base(isG ? 50 : 44, 15),
                Block        = Base(isC ? 52 : 40, 15),
                OffensiveRebound = Base(isC ? 55 : 42, 15),
                DefensiveRebound = Base(isC ? 58 : 46, 15),
                DefensiveConsistency = Base(50, 12),
                Speed        = Base(isG ? 56 : 48, 15),
                Strength     = Base(isC ? 55 : 48, 15),
                Stamina      = Base(52, 12),
            };
        }

        private PlayerTendencies GenerateTendencies(Position pos)
        {
            bool isG = pos == Position.PG || pos == Position.SG;
            bool isC = pos == Position.C;
            int R(int v) => Mathf.Clamp(v + _rng.Next(-8, 9), 30, 90);

            return new PlayerTendencies
            {
                ShotTendency     = R(60),
                ThreeTendency    = R(isG ? 65 : 40),
                TwoPointTendency = R(isC ? 65 : 50),
                DriveTendency    = R(isG ? 65 : 50),
                PostTendency     = R(isC ? 65 : 40),
                CloseShotTendency = R(isC ? 60 : 50),
                PassTendency     = R(isG ? 65 : 55),
                DrawFoulTendency = R(55),
                StealTendency    = R(isG ? 60 : 50),
                BlockTendency    = R(isC ? 60 : 45),
                FoulTendency     = R(50),
                HelpDefenseTendency = R(55),
                OffensiveReboundTendency = R(isC ? 65 : 45),
                DefensiveReboundTendency = R(isC ? 68 : 50),
                ZoneThreeLeftCorner  = R(50),
                ZoneThreeRightCorner = R(50),
                ZoneThreeLeftWing    = R(isG ? 65 : 50),
                ZoneThreeRightWing   = R(isG ? 65 : 50),
                ZoneThreeTopKey      = R(isG ? 70 : 55),
                ZoneMidLeftCorner    = R(40),
                ZoneMidRightCorner   = R(40),
                ZoneMidLeftElbow     = R(55),
                ZoneMidRightElbow    = R(55),
                ZoneMidTopKey        = R(50),
                ZoneCloseLeft        = R(50),
                ZoneCloseCenter      = R(isC ? 75 : 60),
                ZoneCloseRight       = R(50),
            };
        }

        private int GetHeightForPosition(Position pos)
        {
            return pos switch
            {
                Position.PG => _rng.Next(178, 191),
                Position.SG => _rng.Next(188, 200),
                Position.SF => _rng.Next(196, 208),
                Position.PF => _rng.Next(203, 213),
                Position.C  => _rng.Next(208, 220),
                _           => 195
            };
        }

        private int GetWeightForPosition(Position pos)
        {
            return pos switch
            {
                Position.PG => _rng.Next(78, 90),
                Position.SG => _rng.Next(88, 100),
                Position.SF => _rng.Next(96, 110),
                Position.PF => _rng.Next(104, 118),
                Position.C  => _rng.Next(112, 128),
                _           => 96
            };
        }
    }
}
