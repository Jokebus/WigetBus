using System;
using System.Collections.Generic;
using System.IO;

namespace WigetBus
{
    public class NameDayService
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>();

        public NameDayService()
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cz_namedays.csv");
            try
            {
                if (!File.Exists(filePath))
                    return;

                var lines = File.ReadAllLines(filePath);
                if (lines == null || lines.Length <= 1)
                    return;

                for (int i = 1; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',');
                    if (parts.Length < 3)
                        continue;

                    int month, day;
                    if (!int.TryParse(parts[0].Trim(), out month) || !int.TryParse(parts[1].Trim(), out day))
                        continue;

                    var names = string.Join(",", parts, 2, parts.Length - 2).Trim();
                    if (string.IsNullOrWhiteSpace(names))
                        continue;

                    var key = month.ToString("00") + "-" + day.ToString("00");
                    _map[key] = names;
                }
            }
            catch
            {
                // tichý catch - při chybě necháme prázdnou mapu
            }
        }

        public string GetNameDay(DateTime date)
        {
            string v;
            if (_map.TryGetValue(date.ToString("MM-dd"), out v))
                return v;
            return null;
        }
    }
}

