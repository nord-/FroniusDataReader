using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace FroniusDataReader.Tests;

public class FroniusJsonParsingTests
{
    private const string SampleJsonData = """
        {
        	"Body" : 
        	{
        		"Data" : 
        		{
        			"inverter/1" : 
        			{
        				"Data" : 
        				{
        					"EnergyReal_WAC_Sum_Produced" : 
        					{
        						"Unit" : "Wh",
        						"Values" : 
        						{
        							"0" : 64787.665555555555,
        							"86400" : 26981.127500000013
        						},
        						"_comment" : "channelId=67830024"
        					}
        				},
        				"DeviceType" : 123,
        				"End" : "2025-07-02T23:59:59+02:00",
        				"NodeType" : 97,
        				"Start" : "2025-07-01T00:00:00+02:00"
        			}
        		}
        	},
        	"Head" : 
        	{
        		"RequestArguments" : 
        		{
        			"Channel" : 
        			[
        				"EnergyReal_WAC_Sum_Produced"
        			],
        			"EndDate" : "2025-07-02T23:59:59+02:00",
        			"HumanReadable" : "True",
        			"Scope" : "System",
        			"SeriesType" : "DailySum",
        			"StartDate" : "2025-07-01T00:00:00+02:00"
        		},
        		"Status" : 
        		{
        			"Code" : 0,
        			"ErrorDetail" : 
        			{
        				"Nodes" : []
        			},
        			"Reason" : "",
        			"UserMessage" : ""
        		},
        		"Timestamp" : "2025-08-01T15:27:07+02:00"
        	}
        }
        """;

    [Fact]
    public void ParseFroniusResponse_ShouldExtractAllData_FromRealResponse()
    {
        // Act
        using var jsonDoc = JsonDocument.Parse(SampleJsonData);
        var root = jsonDoc.RootElement;

        // Assert - Verify structure exists
        root.TryGetProperty("Head", out var head).Should().BeTrue("Response should contain Head section");
        root.TryGetProperty("Body", out var body).Should().BeTrue("Response should contain Body section");

        // Verify Head section
        head.TryGetProperty("Status", out var status).Should().BeTrue("Head should contain Status");
        status.TryGetProperty("Code", out var statusCode).Should().BeTrue("Status should contain Code");
        statusCode.GetInt32().Should().Be(0, "Status code should be 0 for success");

        head.TryGetProperty("Timestamp", out var timestamp).Should().BeTrue("Head should contain Timestamp");
        var timestampStr = timestamp.GetString();
        timestampStr.Should().NotBeNullOrEmpty("Timestamp should not be empty");
        DateTime.TryParse(timestampStr, out _).Should().BeTrue("Timestamp should be a valid DateTime");

        // Verify Body structure
        body.TryGetProperty("Data", out var data).Should().BeTrue("Body should contain Data");
        data.TryGetProperty("inverter/1", out var inverterData).Should().BeTrue("Data should contain inverter/1");
        
        // Verify inverter metadata
        inverterData.TryGetProperty("DeviceType", out var deviceType).Should().BeTrue("Inverter should have DeviceType");
        deviceType.GetInt32().Should().Be(123, "DeviceType should match expected value");

        inverterData.TryGetProperty("NodeType", out var nodeType).Should().BeTrue("Inverter should have NodeType");
        nodeType.GetInt32().Should().Be(97, "NodeType should match expected value");

        inverterData.TryGetProperty("Start", out var startTime).Should().BeTrue("Inverter should have Start time");
        inverterData.TryGetProperty("End", out var endTime).Should().BeTrue("Inverter should have End time");

        // Verify energy data structure
        inverterData.TryGetProperty("Data", out var inverterEnergyData).Should().BeTrue("Inverter should contain Data section");
        inverterEnergyData.TryGetProperty("EnergyReal_WAC_Sum_Produced", out var energySection).Should().BeTrue("Should contain energy data");
        
        energySection.TryGetProperty("Unit", out var unit).Should().BeTrue("Energy section should have Unit");
        unit.GetString().Should().Be("Wh", "Unit should be Wh (Watt-hours)");

        energySection.TryGetProperty("Values", out var values).Should().BeTrue("Energy section should have Values");
        
        // Parse and verify specific energy values
        var energyReadings = new Dictionary<long, decimal>();
        foreach (var value in values.EnumerateObject())
        {
            long.TryParse(value.Name, out var timestampSeconds).Should().BeTrue($"Timestamp '{value.Name}' should be parseable as long");
            timestampSeconds.Should().BeGreaterOrEqualTo(0, "Timestamp should be non-negative");
            
            value.Value.TryGetDecimal(out var energyValue).Should().BeTrue("Energy value should be a decimal");
            energyValue.Should().BeGreaterThan(0, "Energy value should be positive");
            
            energyReadings[timestampSeconds] = energyValue;
        }

        // Verify expected data points from our sample
        energyReadings.Should().ContainKey(0L, "Should contain reading for timestamp 0 (start of first day)");
        energyReadings.Should().ContainKey(86400L, "Should contain reading for timestamp 86400 (start of second day)");
        
        energyReadings[0L].Should().BeApproximately(64787.67m, 0.01m, "First day energy should match expected value");
        energyReadings[86400L].Should().BeApproximately(26981.13m, 0.01m, "Second day energy should match expected value");

        // Verify total energy calculation
        var totalEnergy = energyReadings.Values.Sum();
        totalEnergy.Should().BeApproximately(91768.79m, 0.01m, "Total energy should be sum of daily readings");

        Console.WriteLine($"Successfully parsed {energyReadings.Count} energy readings");
        Console.WriteLine($"Total energy for period: {totalEnergy:F2} Wh");
        foreach (var reading in energyReadings.OrderBy(kvp => kvp.Key))
        {
            var dayOffset = reading.Key / 86400; // Convert seconds to days
            Console.WriteLine($"Day {dayOffset}: {reading.Value:F2} Wh");
        }
    }

    [Fact]
    public void ParseFroniusResponse_ShouldHandleTimestampConversion()
    {
        // Arrange
        using var jsonDoc = JsonDocument.Parse(SampleJsonData);
        var startDateStr = "2025-07-01T00:00:00+02:00";
        var baseDate = DateTime.Parse(startDateStr);

        // Act - Extract values and convert timestamps
        var root = jsonDoc.RootElement;
        var values = root.GetProperty("Body")
                        .GetProperty("Data")
                        .GetProperty("inverter/1")
                        .GetProperty("Data")
                        .GetProperty("EnergyReal_WAC_Sum_Produced")
                        .GetProperty("Values");

        var dailyReadings = new List<(DateTime Date, decimal Energy)>();
        foreach (var value in values.EnumerateObject())
        {
            var timestampSeconds = long.Parse(value.Name);
            var date = baseDate.AddSeconds(timestampSeconds);
            var energy = value.Value.GetDecimal();
            
            dailyReadings.Add((date, energy));
        }

        // Assert
        dailyReadings.Should().HaveCount(2, "Should have 2 daily readings");
        
        var firstReading = dailyReadings.First(r => r.Date == baseDate);
        firstReading.Energy.Should().BeApproximately(64787.67m, 0.01m);
        
        var secondReading = dailyReadings.First(r => r.Date == baseDate.AddDays(1));
        secondReading.Energy.Should().BeApproximately(26981.13m, 0.01m);

        Console.WriteLine($"Converted {dailyReadings.Count} timestamp-based readings to dates:");
        foreach (var reading in dailyReadings.OrderBy(r => r.Date))
        {
            Console.WriteLine($"{reading.Date:yyyy-MM-dd}: {reading.Energy:F2} Wh");
        }
    }

    [Fact]
    public void ParseFroniusResponse_ShouldMatchApplicationLogic()
    {
        // This test verifies that our parsing logic matches what the main application does
        
        // Arrange
        using var jsonDoc = JsonDocument.Parse(SampleJsonData);
        var allDays = new Dictionary<DateTime, decimal>();
        var start = new DateTime(2025, 7, 1); // Start date from the sample

        // Act - Simulate the parsing logic from Program.cs
        var valuesElement = jsonDoc.RootElement
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
                allDays.TryAdd(time, value);
            }
        }

        // Assert
        allDays.Should().HaveCount(2, "Should have parsed 2 daily values");
        allDays.Should().ContainKey(new DateTime(2025, 7, 1), "Should contain July 1st reading");
        allDays.Should().ContainKey(new DateTime(2025, 7, 2), "Should contain July 2nd reading");

        var july1Energy = allDays[new DateTime(2025, 7, 1)];
        var july2Energy = allDays[new DateTime(2025, 7, 2)];

        july1Energy.Should().BeApproximately(64787.67m, 0.01m, "July 1st energy should match");
        july2Energy.Should().BeApproximately(26981.13m, 0.01m, "July 2nd energy should match");

        // Test the DictionaryToText extension method format
        var textOutput = allDays.DictionaryToText();
        textOutput.Should().NotBeNullOrEmpty("Text output should not be empty");
        textOutput.Should().Contain("2025-07-01", "Should contain formatted date in yyyy-MM-dd format");
        textOutput.Should().Contain("64788", "Should contain rounded energy value");

        Console.WriteLine("Generated text output:");
        Console.WriteLine(textOutput);
    }
}
