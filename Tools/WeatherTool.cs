
using AgentBot;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

using Polly;
using Polly.Retry;

namespace AgentBot.Tools
{
    /// <summary>
    /// Tool to get weather for a city using external API (e.g., OpenWeatherMap).
    /// Register in DI as AddTransient<IToolFunction, WeatherTool>().
    /// </summary>
    public class WeatherTool : IToolFunction
    {
        public string Name => "GetWeather";

        public string Description => "Get current weather for a city. Returns temperature, conditions, etc.";

        public Dictionary<string, string> Parameters => new()
        {
            { "city", "string" } // City name, required
        };

        private readonly HttpClient _httpClient;
        private readonly ILogger<WeatherTool> _logger;
        private readonly string _apiKey; // From config or secrets
        private readonly AsyncRetryPolicy _retryPolicy;

        public WeatherTool(IHttpClientFactory httpClientFactory, ILogger<WeatherTool> logger, IConfiguration config)
        {
            _httpClient = httpClientFactory.CreateClient();
            _logger = logger;
            _apiKey = config["WeatherApiKey"] ?? throw new ArgumentException("Weather API key not configured.");
            
            // Инициализация политики ретраев
            _retryPolicy = AgentBot.Infrastructure.RetryPolicies.CreateDefaultRetryPolicy(logger, "Weather API");
        }

        public async Task<string> ExecuteAsync(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("city", out var cityObj) || cityObj is not string city || string.IsNullOrWhiteSpace(city))
            {
                _logger.LogWarning("Invalid or missing 'city' parameter.");
                return JsonSerializer.Serialize(new { error = "City parameter is required." });
            }

            try
            {
                string url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&appid={_apiKey}&units=metric";
                var response = await _retryPolicy.ExecuteAsync(async () => 
                {
                    return await _httpClient.GetAsync(url);
                });
                response.EnsureSuccessStatusCode();
                string content = await response.Content.ReadAsStringAsync();

                // Parse and simplify (example: extract key fields)
                var json = JsonDocument.Parse(content);
                var weather = new
                {
                    City = json.RootElement.GetProperty("name").GetString(),
                    Temp = json.RootElement.GetProperty("main").GetProperty("temp").GetDouble(),
                    Description = json.RootElement.GetProperty("weather")[0].GetProperty("description").GetString()
                };

                _logger.LogInformation("Weather fetched for {City}: {Temp}°C, {Description}", weather.City, weather.Temp, weather.Description);
                return JsonSerializer.Serialize(weather);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching weather for {City}", city);
                return JsonSerializer.Serialize(new { error = "Failed to get weather data." });
            }
        }
    }
}