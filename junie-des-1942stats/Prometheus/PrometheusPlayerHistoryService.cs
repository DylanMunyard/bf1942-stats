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
        
        public async Task<PrometheusTimeseriesResult?> GetServerPlayersHistory(string serverName, string game, int days = 7, string step = "1h")
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

        public async Task<PrometheusVectorResult?> GetAveragePlayerCountChange(string serverName, string game, int days = 7)
        {
            // Build the query with the proper format
            var metric = game == "bf1942" ? "bf1942_server_players" : "fh2_server_players";
            var timeRange = $"{days}d";
            
            /* Compare the average player count over the last x days (7 by default), with the average player count over the x days before that */
            var query = $@"(
  avg_over_time({metric}{{server_name=""{serverName}""}}[{timeRange}]) - 
  avg_over_time({metric}{{server_name=""{serverName}""}}[{timeRange}] offset {timeRange})
) / 
avg_over_time({metric}{{server_name=""{serverName}""}}[{timeRange}] offset {timeRange}) * 100";

            // Create query parameters using NameValueCollection
            var queryParams = HttpUtility.ParseQueryString(string.Empty);
            queryParams["query"] = query;
            
            // Build the full URL with properly encoded parameters
            var requestUrl = $"{_prometheusUrl}/query?{queryParams}";
            
            var response = await _httpClient.GetAsync(requestUrl);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var result = JsonSerializer.Deserialize<PrometheusVectorResult>(content, options);
            
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

        // New classes for vector responses
        public class PrometheusVectorResult
        {
            public string Status { get; set; }
            public PrometheusVectorData Data { get; set; }
        }

        public class PrometheusVectorData
        {
            public string ResultType { get; set; }
            public List<PrometheusVectorResultItem> Result { get; set; }
        }

        public class PrometheusVectorResultItem
        {
            public Dictionary<string, string> Metric { get; set; }
            
            [JsonConverter(typeof(TimeSeriesPointConverter))]
            public TimeSeriesPoint Value { get; set; }
        }

        public class TimeSeriesPoint
        {
            public double Timestamp { get; set; }
            public double Value { get; set; }
        }

        // Converter for single time series points
        public class TimeSeriesPointConverter : JsonConverter<TimeSeriesPoint>
        {
            public override TimeSeriesPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return ReadSinglePoint(ref reader);
            }

            public override void Write(Utf8JsonWriter writer, TimeSeriesPoint value, JsonSerializerOptions options)
            {
                WriteSinglePoint(writer, value);
            }

            // Static helper methods for reuse
            public static TimeSeriesPoint ReadSinglePoint(ref Utf8JsonReader reader)
            {
                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException("Expected start of an array for time series point");
                }
                
                reader.Read(); // Move to first element (timestamp)
                
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
                
                reader.Read(); // Move to end of array
                if (reader.TokenType != JsonTokenType.EndArray)
                {
                    throw new JsonException("Expected end of point array");
                }
                
                return new TimeSeriesPoint
                {
                    Timestamp = timestamp,
                    Value = value
                };
            }

            public static void WriteSinglePoint(Utf8JsonWriter writer, TimeSeriesPoint point)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(point.Timestamp);
                writer.WriteStringValue(point.Value.ToString());
                writer.WriteEndArray();
            }
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
                    // Reuse the single point reading logic
                    var point = TimeSeriesPointConverter.ReadSinglePoint(ref reader);
                    points.Add(point);
                    
                    reader.Read(); // Move to next point array or end of points array
                }
                
                return points;
            }

            public override void Write(Utf8JsonWriter writer, List<TimeSeriesPoint> value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                
                foreach (var point in value)
                {
                    // Reuse the single point writing logic
                    TimeSeriesPointConverter.WriteSinglePoint(writer, point);
                }
                
                writer.WriteEndArray();
            }
        }
    }
}