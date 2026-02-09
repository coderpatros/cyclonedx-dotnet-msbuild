using Newtonsoft.Json;

var data = new { Message = "Hello from SampleApp", Timestamp = DateTime.UtcNow };
var json = JsonConvert.SerializeObject(data, Formatting.Indented);

Console.WriteLine(json);
