using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;

namespace junie_des_1942stats.Prometheus
{
    public class PrometheusService
    {
        private readonly HttpClient _httpClient;
        private readonly string _prometheusUrl;

        public PrometheusService(HttpClient httpClient, string prometheusUrl)
        {
            _httpClient = httpClient;
            _prometheusUrl = prometheusUrl;
        }
        
        public async Task<PrometheusTimeseriesResult?> GetServerPlayersHistory(string serverName, string game, int days = 7, string step = "2h")
        {
            // Calculate time range
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-days);

            // Format dates for Prometheus
            var start = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var end = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Build the query with the proper format
            var metric = game == "bf1942" ? "bf1942_server_players" : "fh2_server_players";
            var query = $"sum without (pod, instance) ({metric}{{server_name=\"{serverName}\"}})";

            // Create query parameters using NameValueCollection
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["query"] = query;
            queryParams["start"] = start;
            queryParams["end"] = end;
            queryParams["step"] = step;
            
            // Build the full URL with properly encoded parameters
            var requestUrl = $"{_prometheusUrl}/query_range?{queryParams}";
            
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var result = JsonSerializer.Deserialize<PrometheusTimeseriesResult>(content, options);
            
            return result;
        }

        public class PrometheusTimeseriesResult
        {
            public string Status { get; set; }
            public PrometheusData Data { get; set; }
        }

        public class PrometheusData
        {
            public List<PrometheusResult> Result { get; set; }
        }

        public class PrometheusResult
        {
            [JsonConverter(typeof(TimeSeriesPointsConverter))]
            public List<TimeSeriesPoint> Values { get; set; }
        }

        public class TimeSeriesPoint
        {
            public double Timestamp { get; set; }
            public double Value { get; set; }
        }

        public class TimeSeriesPointsConverter : JsonConverter<List<TimeSeriesPoint>>
        {
            public override List<TimeSeriesPoint> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var points = new List<TimeSeriesPoint>();
                
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException("Expected start of an array for time series points");
                }
                
                reader.Read(); // Move to the first element or end of array
                
                while (reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("Expected start of a point array");
                    }
                    
                    reader.Read(); // Move to first element in point array (timestamp)
                    
                    double timestamp = 0;
                    double value = 0;
                    
                    if (reader.TokenType == JsonTokenType.Number)
                    {
                        timestamp = reader.GetDouble();
                    }
                    
                    reader.Read(); // Move to second element (value)
                    
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        if (double.TryParse(reader.GetString(), out double parsedValue))
                        {
                            value = parsedValue;
                        }
                    }
                    
                    reader.Read(); // Move to end of point array
                    if (reader.TokenType != JsonTokenType.EndArray)
                    {
                        throw new JsonException("Expected end of point array");
                    }
                    
                    points.Add(new TimeSeriesPoint
                    {
                        Timestamp = timestamp,
                        Value = value
                    });
                    
                    reader.Read(); // Move to next point array or end of points array
                }
                
                return points;
            }

            public override void Write(Utf8JsonWriter writer, List<TimeSeriesPoint> value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                
                foreach (var point in value)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(point.Timestamp);
                    writer.WriteStringValue(point.Value.ToString());
                    writer.WriteEndArray();
                }
                
                writer.WriteEndArray();
            }
        }
    }
}