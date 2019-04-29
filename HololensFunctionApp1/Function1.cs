using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data.SqlClient;
using Microsoft.Azure.Devices;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Globalization;

namespace HololensFunctionApp1
{
    public static class Function1
    {
        //[FunctionName("Function1")]
        //public static async Task<IActionResult> Run(
        //    [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
        //    ILogger log)
        //{
        //    log.LogInformation("C# HTTP trigger function processed a request.");

        //    string name = req.Query["name"];

        //    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        //    dynamic data = JsonConvert.DeserializeObject(requestBody);
        //    name = name ?? data?.name;

        //    return name != null
        //        ? (ActionResult)new OkObjectResult($"Hello, {name}")
        //        : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
        //}
        static ServiceClient serviceClient;
        static string connectionString = "HostName=james6342iot.azure-devices.net;SharedAccessKeyName=service;SharedAccessKey=RnzkBco8VRay0qFzzsVy4GFmpQF6uTDHVJInUD7VlQ4=";
        static string TestCmdString = "TEST ON";

        [FunctionName("Function1")]
        //public static async Task<IActionResult> Run(
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string name = req.Query["name"];
            string mac = req.Query["mac"];
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject<query_para>(requestBody);
            name = name ?? data?.name;
            mac = mac ?? data?.mac;
            log.LogInformation("????query: name = " + name + " mac = " + mac);
            //name = data.cmd;
            //mac_id = data.id;


            // Get the connection string from app settings and use it to create a connection.
            var str = Environment.GetEnvironmentVariable("sqldb_connection");
            string tmp_str = "";
            string jsonData = @"{
                'deviceID' : '',
                'timestamp' : '',
                'brightness' : 0,
                'vdd' : 0,
                'status' : 0
            }";
            var jsonToReturn = JObject.Parse(jsonData);

            using (SqlConnection conn = new SqlConnection(str))
            {
                conn.Open();
                //var text = "UPDATE SalesLT.SalesOrderHeader " +
                //        "SET [Status] = 5  WHERE ShipDate < GetDate();";
                var text = "SELECT TOP (1) MeasurementID, * " +
                        "FROM Measurement " +
                        "WHERE deviceID='" + mac + "' " +
                        "ORDER BY timestamp DESC;";

                using (SqlCommand cmd = new SqlCommand(text, conn))
                {
                    // Execute the command and log the # rows affected.

                    //var rows = await cmd.ExecuteNonQueryAsync();
                    SqlDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        log.LogInformation(reader["deviceID"].ToString() +"," + reader["timestamp"].ToString());
                        tmp_str += "Device ID : "+ reader["deviceID"].ToString() + "\n\r" +
                            "Time : " + reader["timestamp"].ToString() + "\n\r" +
                            "Brightness : " + reader["brightness"].ToString() + "\n\r" +
                            "Vdd : " + reader["vdd"].ToString() + "\n\r" +
                            "Test status : " + reader["status"].ToString();

                        jsonToReturn["deviceID"] = reader["deviceID"].ToString();
                        jsonToReturn["timestamp"] = reader["timestamp"].ToString();
                        jsonToReturn["brightness"] = (double)reader["brightness"];
                        jsonToReturn["vdd"] = (double)reader["vdd"];
                        jsonToReturn["status"] = (int)reader["status"];
                    }

                    reader.Close();
                }

                if (name == "JOB DONE")
                {
                    DateTime utcDate = DateTime.UtcNow;
                    text = "UPDATE Job " +
                        "SET Done = 1, CloseTime = '" + utcDate.ToString("yyyy-M-dd hh:mm:ss") + "' " +
                        "WHERE deviceID='" + mac + "' " +
                        "AND Done = 0;";
                    log.LogInformation(utcDate.ToString());

                    using (SqlCommand cmd = new SqlCommand(text, conn))
                    {
                        // Execute the command and log the # rows affected.

                        var rows = await cmd.ExecuteNonQueryAsync();
                        log.LogInformation($"{rows} rows updated in database.\n");
                    }

                }


                conn.Close();
            }
            
            if (name=="TEST ON")
            {
                TestCmdString = "TEST ON";
                log.LogInformation("#### Send Cloud-to-Device message test on ######\n");
                serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
                ReceiveFeedbackAsync();
                SendCloudToDeviceMessageAsync().Wait();
                Debug.WriteLine("#### Send Cloud-to-Device message OK! ######\n");
            }
            else if (name == "TEST OFF")
            {
                TestCmdString = "TEST OFF";
                log.LogInformation("#### Send Cloud-to-Device message test off ######\n");
                serviceClient = ServiceClient.CreateFromConnectionString(connectionString);
                ReceiveFeedbackAsync();
                SendCloudToDeviceMessageAsync().Wait();
                Debug.WriteLine("#### Send Cloud-to-Device message OK! ######\n");
            }


            //return mac != null
            //    ? (ActionResult)new OkObjectResult($"{tmp_str}")
            //    : new BadRequestObjectResult("Please pass a device mac on the query string or in the request body");
            //var jsonToReturn = JsonConvert.SerializeObject(new { deviceID = reader_return["deviceID"], 
            //    timestamp = reader_return["timestamp"],
            //    brightness = reader_return["brightness"],
            //    vdd = reader_return["vdd"],
            //    status = reader_return["status"] });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonToReturn.ToString(), Encoding.UTF8, "application/json")
            };

        }

        private async static Task SendCloudToDeviceMessageAsync()
        {
            var commandMessage = new
             Message(Encoding.ASCII.GetBytes(TestCmdString));
            commandMessage.Ack = DeliveryAcknowledgement.Full;
            await serviceClient.SendAsync("james6342IoTGateway", commandMessage);
        }

        private async static void ReceiveFeedbackAsync()
        {
            var feedbackReceiver = serviceClient.GetFeedbackReceiver();

            Debug.WriteLine("\n#####Receiving c2d feedback from service");
            while (true)
            {
                var feedbackBatch = await feedbackReceiver.ReceiveAsync();
                if (feedbackBatch == null) continue;

                Debug.WriteLine("#########Received feedback: {0}");


                await feedbackReceiver.CompleteAsync(feedbackBatch);
            }
        }

        public class query_para
        {
            public string name { get; set; }
            public string cmd { get; set; }

        }

        public class sensor_data
        {
            public string deviceID { get; set; }
            public string timestamp { get; set; }
            public double brightness { get; set; }
            public double vdd { get; set; }
            public int status { get; set; }
        }
    }
}
