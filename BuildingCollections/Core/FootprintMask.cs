using System.Collections.Generic;

namespace BuildingCollections.Core
{
    public static class FootprintMask
    {
        public const float StrictOverlap = 0.05f;

        public static int Apply(PlanGrid grid, PlanBlock block, IReadOnlyList<Quad> footprints, float overlap, int fromRow = 0)
        {
            if (footprints.Count == 0)
            {
                return 0;
            }

            int masked = 0;
            for (int y = fromRow; y < grid.Depth; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.Free[x, y])
                    {
                        continue;
                    }

                    var cell = block.Frame.Cell(block.AreaX + x, block.AreaY + y, overlap);
                    foreach (var fp in footprints)
                    {
                        if (Quad.Touch(fp, cell))
                        {
                            grid.Free[x, y] = false;
                            masked++;
                            break;
                        }
                    }
                }
            }

            return masked;
        }
    }
}
