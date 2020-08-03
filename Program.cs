using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FroniusDataReader
{
    class Program
    {
        private const string ApiUrl = @"http://192.168.1.189/solar_api/v1/GetArchiveData.cgi?Scope=System&StartDate=%startdate%&EndDate=%enddate%&Channel=EnergyReal_WAC_Sum_Produced&SeriesType=DailySum";
        private const string StartDate = "%startdate%";
        private const string EndDate = "%enddate%";
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

            var path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var outputFile = Path.Combine(path, $"fdr{fromDate:yyyyMMdd}.txt"); //Path.GetRandomFileName().Substring(0, 9) + "txt");
            using var textWriter = File.CreateText(outputFile);
            using var client = new HttpClient();
            for (var iterationStart = fromDate; iterationStart < toDate; iterationStart = iterationStart.AddDays(MaxDays+1))
            {
                var endDate = iterationStart.AddDays(MaxDays);
                endDate = endDate > toDate ? toDate : endDate;

                var localUrl = ApiUrl.Replace(StartDate, iterationStart.ToString("dd.MM.yyyy"))
                                     .Replace(EndDate, endDate.ToString("dd.MM.yyyy"));
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
                                                Console.WriteLine($"{start.AddSeconds(secs):d} {value:F0}");
                                                await textWriter.WriteLineAsync($"{start.AddSeconds(secs):d}\t{value:F0}");
                                            }
                                        }
                                        catch
                                        {
                                            Console.WriteLine();
                                            Console.WriteLine(json.ToString(Formatting.Indented));
                                            Console.WriteLine();
                                        }
                                    });

                Task.WaitAll(task);
            }

            textWriter.Close();
            Console.WriteLine(outputFile);
        }
    }
}
