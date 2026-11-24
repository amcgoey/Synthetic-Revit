using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Synthetic.Features.SheetIndex.Services
{
    /// <summary>
    /// Natural alphanumeric string comparer for sorting sheet numbers (e.g. A1, A2, A10, A101, B1).
    /// </summary>
    public class AlphanumericComparer : IComparer<string>
    {
        /// <inheritdoc />
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            string[] xRegex = Regex.Split(x.Replace(" ", ""), "([0-9]+)");
            string[] yRegex = Regex.Split(y.Replace(" ", ""), "([0-9]+)");

            for (int i = 0; i < Math.Min(xRegex.Length, yRegex.Length); i++)
            {
                if (xRegex[i] != yRegex[i])
                {
                    if (long.TryParse(xRegex[i], out long xNum) && long.TryParse(yRegex[i], out long yNum))
                    {
                        return xNum.CompareTo(yNum);
                    }

                    return string.Compare(xRegex[i], yRegex[i], StringComparison.OrdinalIgnoreCase);
                }
            }

            return xRegex.Length.CompareTo(yRegex.Length);
        }
    }
}
