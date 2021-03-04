using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace FroniusDataReader
{
    public static class Clipboard
    {
        public static void SetText(string text)
        {
            new TextCopy.Clipboard().SetText(text);
        }

        public static string DictionaryToText(this IDictionary<DateTime, decimal> allDays) => string.Join('\n', allDays.OrderBy(p => p.Key).Select(p => $"{p.Key:d}\t{p.Value:F0}"));
    }
}
