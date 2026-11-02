namespace ShinyBotHunter.Games.Hgss
{
    // Primo's egg password: the four easy-chat words that his egg check (ov02_0224CBF8 in the
    // decomp's asm/overlay_02_02248728.s) accepts for a trainer ID and an egg (8 Mareep, 9 Wooper,
    // 10 Slugma). Encode runs the check's decode steps (docs/research/primo-slugma-egg.md, "The rule")
    // backwards.
    public static class PrimoPassword
    {
        // The easy-chat word ids of pret/pokeheartgold files/resource/eng/pms_aikotoba/pms_aikotoba.s,
        // in that file's order: a word's position here is its index in the game's password bank.
        internal static readonly ushort[] Bank =
        {
            964, 965, 966, 967, 968, 969, 970, 971, 972, 974, 975, 976,
            977, 979, 980, 981, 983, 984, 985, 986, 987, 988, 989, 990,
            991, 992, 993, 994, 995, 996, 997, 998, 999, 1000, 1001, 1002,
            1003, 1004, 1005, 1006, 1007, 1008, 1009, 1010, 1011, 1012, 1013, 1014,
            1015, 1016, 1017, 1018, 1019, 1020, 1021, 1022, 1023, 1024, 1025, 1026,
            1027, 1028, 1029, 1030, 1031, 1032, 1033, 1034, 1035, 1036, 1037, 1038,
            1039, 1040, 1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049, 1050,
            1051, 1052, 1053, 1054, 1055, 1056, 1057, 1058, 1059, 1060, 1061, 1062,
            1063, 1064, 1065, 1066, 1067, 1069, 1070, 1071, 1072, 1073, 1074, 1075,
            1076, 1077, 1078, 1079, 1080, 1081, 1082, 1083, 1084, 1085, 1086, 1087,
            1088, 1090, 1091, 1092, 1093, 1094, 1095, 1096, 1097, 1098, 1099, 1100,
            1101, 1102, 1104, 1106, 1107, 1108, 1109, 1110, 1111, 1112, 1113, 1114,
            1115, 1116, 1117, 1118, 1119, 1120, 1121, 1122, 1123, 1124, 1125, 1126,
            1127, 1128, 1129, 1130, 1131, 1132, 1133, 1134, 1135, 1136, 1137, 1138,
            1139, 1140, 1141, 1142, 1143, 1144, 1147, 1148, 1149, 1150, 1151, 1152,
            1153, 1154, 1155, 1156, 1157, 1158, 1159, 1160, 1161, 1162, 1163, 1164,
            1166, 1168, 1169, 1170, 1171, 1172, 1173, 1174, 1177, 1178, 1181, 1289,
            1290, 1291, 1292, 1293, 1294, 1295, 1296, 1297, 1298, 1299, 1300, 1301,
            1302, 1303, 1304, 1305, 1306, 1307, 1308, 1309, 1310, 1311, 1312, 1313,
            1314, 1315, 1316, 1317, 1318, 1319, 1320, 1321, 1322, 1323, 1324, 1325,
            1326, 1327, 1328, 1329, 1330, 1331, 1332, 1333, 1334, 1335, 1336, 1337,
            1338, 1339, 1340, 1341, 1342, 1343, 1344, 1345, 1346, 1347, 1348, 1349,
            1350, 1351, 1352, 1353, 1354, 1355, 1356, 1357, 1358, 1359, 1360, 1361,
            1362, 1363, 1364, 1365, 1366, 1367, 1368, 1369, 1370, 1371, 1372, 1373,
            1374, 1375, 1376, 1377, 1378, 1379, 1380, 1381, 1382, 1383, 1384, 1385,
            1386, 1387, 1388, 1389, 1390, 1391, 1392, 1393, 1394, 1395, 1396, 1397,
            1398, 1399, 1400, 1401, 1402, 1403, 1404, 1405, 1406, 1407, 1408, 1409,
            1410, 1411, 1412, 1413, 1414, 1415, 1416, 1417, 1418, 1419, 1420, 1421,
            1422, 1423, 1424, 1425, 1426, 1427, 1428, 1429, 1431, 1432, 1434, 1435,
            1436, 1438, 1439,
        };

        // Prompt 1 is words [0] and [1]; prompt 2 is words [2] and [3].
        public static ushort[] Encode(ushort trainerId, int egg)
        {
            int b0 = 0x60 | egg;
            int b1 = (trainerId >> 8) ^ b0;
            int b2 = (trainerId & 0xFF) ^ b0;
            int b3 = ((b0 + (trainerId >> 8)) * (trainerId & 0xFF)) & 0xFF;

            // Undoes step 5: the first three bytes rotated right by b3 & 0xF.
            uint first3 = (uint)(b0 << 16 | b1 << 8 | b2);
            int shift = b3 & 0xF;
            first3 = ((first3 << shift) | (first3 >> (24 - shift))) & 0xFFFFFF;

            // Undoes step 4: the first three bytes XORed with the mask from b3.
            uint mask = (uint)((b3 & 0xF0) | (b3 >> 4));
            first3 ^= mask << 16 | mask << 8 | mask;

            // Undoes step 3: all four bytes rotated right by 5.
            uint all4 = first3 << 8 | (uint)b3;
            all4 = (all4 << 5) | (all4 >> 27);

            // Undoes step 2: each byte after the first is its index minus the one before, modulo the bank size.
            int i0 = (int)(all4 >> 24);
            int i1 = (i0 + (int)((all4 >> 16) & 0xFF)) % Bank.Length;
            int i2 = (i1 + (int)((all4 >> 8) & 0xFF)) % Bank.Length;
            int i3 = (i2 + (int)(all4 & 0xFF)) % Bank.Length;

            // Undoes step 1: each index is a word's position in the bank.
            return new[] { Bank[i0], Bank[i1], Bank[i2], Bank[i3] };
        }
    }
}
