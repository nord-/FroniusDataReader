using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Flurl;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FroniusDataReader
{
    class Program
    {
        // http://192.168.1.189/solar_api/v1/GetArchiveData.cgi?Scope=System&StartDate=%startdate%&EndDate=%enddate%&Channel=EnergyReal_WAC_Sum_Produced&SeriesType=DailySum
        private const int MaxDays = 15;

        static void Main(string[] args)
        {
            var prevMonth = DateTime.Today.AddMonths(-1);
            var fromDate = new DateTime(prevMonth.Year, prevMonth.Month, 1);
            var toDate   = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);

            if (args.Length > 0)
            {
                fromDate = DateTime.Parse(args[0]);
                toDate = DateTime.Parse(args[1]);
            }

            Console.WriteLine($"Start: {fromDate:d}, end: {toDate:d}");

            var allDays = new Dictionary<DateTime, decimal>();
            using var client = new HttpClient();

            var taskList = new List<Task>();

            for (var iterationStart = fromDate; iterationStart < toDate; iterationStart = iterationStart.AddDays(MaxDays+1))
            {
                var endDate = iterationStart.AddDays(MaxDays);
                endDate = endDate > toDate ? toDate : endDate;

                var localUrl = $"http://{InverterIp}"
                               .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
                               .SetQueryParam("Scope", "System")
                               .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
                               .SetQueryParam("SeriesType", "DailySum")
                               .SetQueryParam("StartDate", iterationStart.ToString("dd.MM.yyyy"))
                               .SetQueryParam("EndDate", endDate.ToString("dd.MM.yyyy"));

                Console.WriteLine(localUrl + "\n");

                var start = iterationStart;
                var task = Task.Run(async () =>
                                    {
                                        JObject json = null;
                                        try
                                        {
                                            var response = await client.GetStringAsync(localUrl);
                                            json = JObject.Parse(response);
                                            var daysWithData = json["Body"]["Data"]["inverter/1"]["Data"]["EnergyReal_WAC_Sum_Produced"]["Values"]
                                                              .Children()
                                                              .ToDictionary(key => long.Parse(((JProperty)key).Name), value => (decimal)((JProperty)value).Value);

                                            foreach (var (secs, value) in daysWithData)
                                            {
                                                var d = new { Time = start.AddSeconds(secs), Value = value};
                                                allDays.Add(d.Time, d.Value);
                                                Console.WriteLine($"{d.Time:d} {d.Value:F0}");
                                            }
                                        }
                                        catch
                                        {
                                            Console.WriteLine();
                                            Console.WriteLine(json != null ? json.ToString(Formatting.Indented) : "No valid json in response...");
                                            Console.WriteLine();
                                        }
                                    });
                taskList.Add(task);
            }

            Task.WaitAll(taskList.ToArray());

            var daysAsText = allDays.DictionaryToText();
            Clipboard.SetText(daysAsText);
        }

        private static string InverterIp => ConfigurationManager.AppSettings.Get(nameof(InverterIp));
    }
}
