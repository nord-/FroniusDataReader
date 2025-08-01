using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Flurl;
using FroniusDataReader;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

var settings = configuration.GetSection("FroniusSettings").Get<FroniusSettings>()
    ?? throw new InvalidOperationException("FroniusSettings configuration is missing");

var prevMonth = DateTime.Today.AddMonths(-1);
var fromDate = new DateTime(prevMonth.Year, prevMonth.Month, 1);
var toDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddDays(-1);

if (args.Length > 0)
{
    fromDate = DateTime.Parse(args[0]);
    toDate = DateTime.Parse(args[1]);
}

Console.WriteLine($"Start: {fromDate:d}, end: {toDate:d}");

var allDays = new Dictionary<DateTime, decimal>();
using var client = new HttpClient();

var tasks = new List<Task>();

for (var iterationStart = fromDate; iterationStart < toDate; iterationStart = iterationStart.AddDays(settings.MaxDays + 1))
{
    var endDate = iterationStart.AddDays(settings.MaxDays);
    endDate = endDate > toDate ? toDate : endDate;

    var localUrl = $"http://{settings.InverterIp}"
                   .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
                   .SetQueryParam("Scope", "System")
                   .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
                   .SetQueryParam("SeriesType", "DailySum")
                   .SetQueryParam("StartDate", iterationStart.ToString("yyyy-MM-dd"))
                   .SetQueryParam("EndDate", endDate.ToString("yyyy-MM-dd"));

    Console.WriteLine($"{localUrl}\n");

    var start = iterationStart;
    var task = ProcessDataAsync(client, localUrl, start, allDays);
    tasks.Add(task);
}

await Task.WhenAll(tasks);

var daysAsText = allDays.DictionaryToText();
await Clipboard.SetTextAsync(daysAsText);

static async Task ProcessDataAsync(HttpClient client, string url, DateTime start, Dictionary<DateTime, decimal> allDays)
{
    JsonDocument? json = null;
    try
    {
        var response = await client.GetStringAsync(url);
        json = JsonDocument.Parse(response);
        
        var valuesElement = json.RootElement
            .GetProperty("Body")
            .GetProperty("Data")
            .GetProperty("inverter/1")
            .GetProperty("Data")
            .GetProperty("EnergyReal_WAC_Sum_Produced")
            .GetProperty("Values");

        foreach (var property in valuesElement.EnumerateObject())
        {
            if (long.TryParse(property.Name, out var secs) && property.Value.TryGetDecimal(out var value))
            {
                var time = start.AddSeconds(secs);
                lock (allDays)
                {
                    allDays.TryAdd(time, value);
                }
                Console.WriteLine($"{time:d} {value:F0}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        if (json != null)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            Console.WriteLine(JsonSerializer.Serialize(json, options));
        }
        else
        {
            Console.WriteLine($"Error processing response: {ex.Message}");
        }
        Console.WriteLine();
    }
    finally
    {
        json?.Dispose();
    }
}
