using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AAAGame.Tilemap
{
    public static class LdtkDualGridControlBuilder
    {
        private static readonly Vector2 HalfCell = new Vector2(0.5f, 0.5f);
        private static readonly Vector2[] OutputOffsets =
        {
            new Vector2(-0.5f, -0.5f),
            new Vector2(-0.5f, 0.5f),
            new Vector2(0.5f, -0.5f),
            new Vector2(0.5f, 0.5f)
        };

        public static bool TryBuildExactControls(
            IEnumerable<Vector2> desiredCells,
            out HashSet<Vector2> controlCells,
            out HashSet<Vector2> missingCells,
            out HashSet<Vector2> extraCells,
            out string error)
        {
            controlCells = new HashSet<Vector2>();
            missingCells = new HashSet<Vector2>();
            extraCells = new HashSet<Vector2>();
            error = null;
            if (desiredCells == null)
            {
                error = "Desired platform cells are missing.";
                return false;
            }

            var desired = new HashSet<Vector2>();
            foreach (Vector2 cell in desiredCells)
            {
                if (!IsIntegerCell(cell))
                {
                    error = "Platform cell must use integer LDtk coordinates: " + FormatCell(cell) + ".";
                    return false;
                }

                desired.Add(cell);
            }

            foreach (Vector2 lowerLeft in desired)
            {
                if (desired.Contains(lowerLeft + Vector2.right) &&
                    desired.Contains(lowerLeft + Vector2.up) &&
                    desired.Contains(lowerLeft + Vector2.one))
                {
                    controlCells.Add(lowerLeft + HalfCell);
                }
            }

            var reconstructed = new HashSet<Vector2>();
            foreach (Vector2 controlCell in controlCells)
            {
                foreach (Vector2 offset in OutputOffsets)
                {
                    reconstructed.Add(controlCell + offset);
                }
            }

            missingCells.UnionWith(desired.Except(reconstructed));
            extraCells.UnionWith(reconstructed.Except(desired));
            return missingCells.Count == 0 && extraCells.Count == 0;
        }

        private static bool IsIntegerCell(Vector2 cell)
        {
            return Mathf.Approximately(cell.x, Mathf.Round(cell.x)) &&
                   Mathf.Approximately(cell.y, Mathf.Round(cell.y));
        }

        private static string FormatCell(Vector2 cell)
        {
            return "(" + cell.x.ToString("0.###") + "," + cell.y.ToString("0.###") + ")";
        }
    }
}
