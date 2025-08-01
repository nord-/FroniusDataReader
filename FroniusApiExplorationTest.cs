using System.Text.Json;
using Flurl;
using Xunit;

namespace FroniusDataReader.Tests;

public class FroniusApiExplorationTest
{
    [Fact]
    public async Task ExploreApiResponse()
    {
        // Arrange
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        
        var url = "http://192.168.2.31"
            .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
            .SetQueryParam("Scope", "System")
            .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
            .SetQueryParam("SeriesType", "DailySum")
            .SetQueryParam("StartDate", "01.07.2025")
            .SetQueryParam("EndDate", "02.07.2025");

        Console.WriteLine($"Testing URL: {url}");

        try
        {
            // Act
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Response Status: {response.StatusCode}");
            Console.WriteLine($"Content Length: {content.Length}");
            Console.WriteLine("Raw Response:");
            Console.WriteLine(content);

            // Try to parse as JSON for better formatting
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    using var jsonDoc = JsonDocument.Parse(content);
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var formattedJson = JsonSerializer.Serialize(jsonDoc, options);
                    Console.WriteLine("\nFormatted JSON:");
                    Console.WriteLine(formattedJson);
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"JSON parsing failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
            throw; // Re-throw to fail the test so we can see the error
        }
    }
}
