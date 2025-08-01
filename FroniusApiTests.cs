using System.Text.Json;
using FluentAssertions;
using Flurl;
using Xunit;

namespace FroniusDataReader.Tests;

public class FroniusApiTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string InverterIp = "192.168.2.31";

    public FroniusApiTests()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    [Fact]
    public async Task GetArchiveData_ShouldReturnValidResponse()
    {
        // Arrange
        var url = $"http://{InverterIp}"
            .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
            .SetQueryParam("Scope", "System")
            .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
            .SetQueryParam("SeriesType", "DailySum")
            .SetQueryParam("StartDate", "2025-07-01")
            .SetQueryParam("EndDate", "2025-07-16");

        // Act
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue($"Expected successful response but got {response.StatusCode}");
        content.Should().NotBeNullOrEmpty("Response content should not be empty");

        // Parse JSON and analyze structure
        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        // Verify basic structure
        root.TryGetProperty("Head", out var head).Should().BeTrue("Response should contain 'Head' property");
        root.TryGetProperty("Body", out var body).Should().BeTrue("Response should contain 'Body' property");

        // Analyze Head section
        if (head.ValueKind == JsonValueKind.Object)
        {
            head.TryGetProperty("Status", out var status).Should().BeTrue("Head should contain Status");
            if (status.ValueKind == JsonValueKind.Object)
            {
                status.TryGetProperty("Code", out var code).Should().BeTrue("Status should contain Code");
                var statusCode = code.GetInt32();
                statusCode.Should().Be(0, "Status code should be 0 for success");
            }
        }

        // Analyze Body section
        if (body.ValueKind == JsonValueKind.Object)
        {
            body.TryGetProperty("Data", out var data).Should().BeTrue("Body should contain Data");
            
            if (data.ValueKind == JsonValueKind.Object)
            {
                // Check for inverter data
                var hasInverterData = false;
                foreach (var property in data.EnumerateObject())
                {
                    if (property.Name.StartsWith("inverter/"))
                    {
                        hasInverterData = true;
                        var inverterData = property.Value;
                        
                        inverterData.TryGetProperty("Data", out var inverterDataSection).Should().BeTrue($"Inverter {property.Name} should contain Data section");
                        
                        if (inverterDataSection.ValueKind == JsonValueKind.Object)
                        {
                            inverterDataSection.TryGetProperty("EnergyReal_WAC_Sum_Produced", out var energyData).Should().BeTrue("Should contain EnergyReal_WAC_Sum_Produced data");
                            
                            if (energyData.ValueKind == JsonValueKind.Object)
                            {
                                energyData.TryGetProperty("Values", out var values).Should().BeTrue("Energy data should contain Values");
                                
                                if (values.ValueKind == JsonValueKind.Object)
                                {
                                    var valueCount = values.EnumerateObject().Count();
                                    valueCount.Should().BeGreaterThan(0, "Should contain at least one energy value");
                                    
                                    // Analyze individual values
                                    foreach (var value in values.EnumerateObject())
                                    {
                                        // Property name should be a timestamp (seconds since start of period)
                                        long.TryParse(value.Name, out var timestamp).Should().BeTrue($"Value key '{value.Name}' should be a valid timestamp");
                                        timestamp.Should().BeGreaterOrEqualTo(0, "Timestamp should be non-negative (0 means start of period)");
                                        
                                        // Value should be a number (energy production)
                                        value.Value.ValueKind.Should().BeOneOf([JsonValueKind.Number, JsonValueKind.Null], "Energy value should be a number or null");
                                        
                                        if (value.Value.ValueKind == JsonValueKind.Number)
                                        {
                                            var energyValue = value.Value.GetDecimal();
                                            energyValue.Should().BeGreaterOrEqualTo(0, "Energy production should be non-negative");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                
                hasInverterData.Should().BeTrue("Response should contain at least one inverter data section");
            }
        }

        // Output summary for manual inspection
        Console.WriteLine($"Response received from {url}");
        Console.WriteLine($"Response status: {response.StatusCode}");
        Console.WriteLine($"Content length: {content.Length} characters");
        
        if (jsonDoc.RootElement.TryGetProperty("Body", out var bodyElement) &&
            bodyElement.TryGetProperty("Data", out var dataElement))
        {
            var inverterCount = dataElement.EnumerateObject().Count(p => p.Name.StartsWith("inverter/"));
            Console.WriteLine($"Number of inverters found: {inverterCount}");
            
            foreach (var inverter in dataElement.EnumerateObject().Where(p => p.Name.StartsWith("inverter/")))
            {
                if (inverter.Value.TryGetProperty("Data", out var invData) &&
                    invData.TryGetProperty("EnergyReal_WAC_Sum_Produced", out var energyData) &&
                    energyData.TryGetProperty("Values", out var values))
                {
                    var valueCount = values.EnumerateObject().Count();
                    var totalEnergy = values.EnumerateObject()
                        .Where(v => v.Value.ValueKind == JsonValueKind.Number)
                        .Sum(v => v.Value.GetDecimal());
                    
                    Console.WriteLine($"Inverter {inverter.Name}: {valueCount} daily values, total energy: {totalEnergy:F2} Wh");
                }
            }
        }
    }

    [Fact]
    public async Task GetArchiveData_ShouldHandleNetworkTimeout()
    {
        // Arrange
        using var timeoutClient = new HttpClient();
        timeoutClient.Timeout = TimeSpan.FromMilliseconds(1); // Very short timeout to force timeout
        
        var url = $"http://{InverterIp}"
            .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
            .SetQueryParam("Scope", "System")
            .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
            .SetQueryParam("SeriesType", "DailySum")
            .SetQueryParam("StartDate", "01.07.2025")
            .SetQueryParam("EndDate", "16.07.2025");

        // Act & Assert
        var act = async () => await timeoutClient.GetAsync(url);
        await act.Should().ThrowAsync<TaskCanceledException>("Network timeout should throw TaskCanceledException");
    }

    [Fact]
    public void GetArchiveData_ShouldValidateUrlConstruction()
    {
        // Arrange & Act
        var url = $"http://{InverterIp}"
            .AppendPathSegment("solar_api/v1/GetArchiveData.cgi")
            .SetQueryParam("Scope", "System")
            .SetQueryParam("Channel", "EnergyReal_WAC_Sum_Produced")
            .SetQueryParam("SeriesType", "DailySum")
            .SetQueryParam("StartDate", "2025-07-01")
            .SetQueryParam("EndDate", "2025-07-16");

        // Assert
        var expectedUrl = "http://192.168.2.31/solar_api/v1/GetArchiveData.cgi?Scope=System&Channel=EnergyReal_WAC_Sum_Produced&SeriesType=DailySum&StartDate=2025-07-01&EndDate=2025-07-16";
        url.ToString().Should().Be(expectedUrl, "URL should be constructed correctly");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
