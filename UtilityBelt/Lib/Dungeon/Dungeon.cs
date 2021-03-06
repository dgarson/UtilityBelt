using Decal.Adapter;
using Decal.Filters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UtilityBelt.Lib.Dungeon {
    public partial class Dungeon {
        public int Landcell;

        public int Landblock { get { return (int)(Landcell & 0xFFFF0000); } }
        private static Dictionary<uint, Dungeon> cache = new Dictionary<uint, Dungeon>();
        public Dictionary<int, DungeonLayer> ZLayers = new Dictionary<int, DungeonLayer>();
        public string Name { get; private set; } = "Unknown Dungeon";

        public int Width { get { return maxX - minX + 10; } }
        public int Height { get { return maxY - minY + 10; } }

        public int Depth { get { return maxZ - minZ; } }

        private List<string> filledCoords = new List<string>();
        public int minX = 0;
        public int maxX = 0;
        public int minY = 0;
        public int maxY = 0;
        public int minZ = 0;
        public int maxZ = 0;
        public int offsetX { get { return 0; } }
        public int offsetY { get { return 0; } }

        private static Dictionary<int, string> dungeonNames;
        private bool didLoadCells = false;

        public static Dictionary<int, string> DungeonNames {
            get {
                if (dungeonNames == null) {
                    string filePath = Path.Combine(Util.GetResourcesDirectory(), "dungeons.csv");
                    Stream fileStream = null;
                    if (File.Exists(filePath)) {
                        fileStream = new FileStream(filePath, FileMode.Open);
                    }
                    else {
                        fileStream = typeof(Dungeon).Assembly.GetManifestResourceStream($"UtilityBelt.Resources.dungeons.csv");
                    }

                    dungeonNames = new Dictionary<int, string>();

                    using (var reader = new StreamReader(fileStream, true)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            var parts = line.Split(',');
                            if (parts.Length != 2) continue;

                            if (int.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out int parsed)) {
                                if (!dungeonNames.ContainsKey(parsed)) {
                                    dungeonNames[parsed << 16] = parts[1];
                                }
                            }
                        }
                    }

                    fileStream.Dispose();
                }

                return dungeonNames;
            }
        }

        public static Dungeon GetCached(int landcell) {
            uint lb = (uint)landcell & 0xFFFF0000;
            if (cache.ContainsKey(lb))
                return cache[lb];
            cache.Add(lb, new Dungeon(landcell));
            return cache[lb];
        }

        public Dungeon(int landcell) {
            Landcell = landcell;

            LoadName();
        }

        private void LoadName() {
            if (DungeonNames.ContainsKey(Landblock)) {
                Name = DungeonNames[Landblock];
            }
            else {
                Name += $" {Landblock:X4}";
            }
        }

        internal void LoadCells() {
            try {
                if (didLoadCells)
                    return;
                didLoadCells = true;
                int cellCount;
                byte[] cellFile = Util.FileService.GetCellFile(65534 + Landblock);

                try {
                    cellCount = BitConverter.ToInt32(cellFile, 4);
                }
                catch {
                    return;
                }

                for (int index = 0; index < cellCount; ++index) {
                    var cell = new DungeonCell(index + Landblock + 256);

                    if (cell.Landcell != 0 && !filledCoords.Contains(cell.GetCoords())) {

                        //check valid tile, and precache i guess
                        if (TextureCache.GetTile(cell.EnvironmentId) == null) continue;

                        if (!ZLayers.ContainsKey(cell.Z)) {
                            ZLayers.Add(cell.Z, new DungeonLayer(this, cell.Z));
                            if (filledCoords.Count == 0) {
                                minZ = cell.Z;
                                maxZ = cell.Z;
                            }

                            if (cell.Z < minZ) minZ = cell.Z;
                            if (cell.Z > maxZ) maxZ = cell.Z;
                        }

                        if (filledCoords.Count == 0) {
                            minX = cell.X;
                            maxX = cell.X;
                            minY = cell.Y;
                            maxY = cell.Y;
                        }
                        else {
                            if (cell.X < minX) minX = cell.X;
                            if (cell.X > maxX) maxX = cell.X;
                            if (cell.Y < minY) minY = cell.Y;
                            if (cell.Y > maxY) maxY = cell.Y;
                        }
                        
                        filledCoords.Add(cell.GetCoords());

                        ZLayers[cell.Z].AddCell(cell);
                    }
                }
                cellFile = null;
            }
            catch (Exception ex) { Logger.LogException(ex); }
        }
    }
}
