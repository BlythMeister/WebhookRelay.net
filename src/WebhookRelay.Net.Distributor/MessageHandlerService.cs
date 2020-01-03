using log4net;
using Microsoft.Azure.ServiceBus;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebhookRelay.Net.Models;

namespace WebhookRelay.Net.Distributor
{
    public class MessageHandlerService
    {
        private readonly Config config;
        private readonly IQueueClient queueClient;
        private readonly HttpClient httpClient;
        private readonly ILog log = LogManager.GetLogger("MessageHandlerService");

        public MessageHandlerService()
        {
            var runningDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            var configPath = Path.Combine(runningDirectory, ConfigurationManager.AppSettings["config.json.relativePath"]);

            log.Debug($"Loading config.json from {configPath}");

            if (!File.Exists(configPath))
            {
                log.Debug("Unable to locate config.json, creating placeholder");
                try
                {
                    var configJson = JsonConvert.SerializeObject(new Config(string.Empty, string.Empty, false, string.Empty, new List<RouteConfig>()));
                    File.WriteAllText(configPath, configJson);
                }
                catch (Exception e)
                {
                    log.Fatal("Unable to locate config.json", e);
                    throw;
                }
            }

            try
            {
                var configJson = File.ReadAllText(configPath);
                config = JsonConvert.DeserializeObject<Config>(configJson);
            }
            catch (Exception e)
            {
                log.Fatal("Unable to deserialise config.json", e);
                throw;
            }

            try
            {
                queueClient = new QueueClient(config.ServiceBusConnectionString, config.QueueName);
            }
            catch (Exception e)
            {
                log.Fatal("Unable to create QueueClient", e);
                throw;
            }

            try
            {
                httpClient = new HttpClient(new HttpClientHandler { UseCookies = false });
            }
            catch (Exception e)
            {
                log.Fatal("Unable to create HttpClient", e);
                throw;
            }
        }

        public void Start()
        {
            log.Info("Starting");
            try
            {
                var messageHandlerOptions = new MessageHandlerOptions(ExceptionReceivedHandler)
                {
                    MaxConcurrentCalls = 1,
                    AutoComplete = false
                };

                queueClient.RegisterMessageHandler(ProcessMessagesAsync, messageHandlerOptions);
            }
            catch (Exception e)
            {
                log.Fatal("Unable to register message handler", e);
                throw;
            }
            log.Info("Started");
        }

        public async void Stop()
        {
            log.Info("Stopping");
            await queueClient.CloseAsync();
            httpClient.Dispose();
            log.Info("Stopped");
        }

        private async Task ProcessMessagesAsync(Message message, CancellationToken token)
        {
            try
            {
                var body = Encoding.UTF8.GetString(message.Body);
                var hookInfo = JsonConvert.DeserializeObject<HookInformation>(body);

                log.Debug($"Received message: SequenceNumber: {message.SystemProperties.SequenceNumber}, Hook Type: {hookInfo.Type}, Hook SubType: {hookInfo.SubType}");

                DebugOutput(message, hookInfo);

                var matchingRoutes = config.Routes.Where(x => IsMatch(x, message, hookInfo)).ToList();

                if (!matchingRoutes.Any())
                {
                    log.Warn($"No routes match SequenceNumber: {message.SystemProperties.SequenceNumber}, Hook Type: {hookInfo.Type}, Hook SubType: {hookInfo.SubType}");
                }
                else
                {
                    foreach (var route in matchingRoutes)
                    {
                        await ProcessRoute(message, token, route, hookInfo);
                    }
                }
            }
            catch (Exception e)
            {
                log.Error($"Error processing message SequenceNumber: {message.SystemProperties.SequenceNumber}", e);
            }

            log.Debug($"Completed message SequenceNumber: {message.SystemProperties.SequenceNumber}");
            await queueClient.CompleteAsync(message.SystemProperties.LockToken);
        }

        private void DebugOutput(Message message, HookInformation hookInfo)
        {
            if (!config.DebugOutput || string.IsNullOrWhiteSpace(config.DebugOutputPath)) return;

            var directory = Path.Combine(config.DebugOutputPath, DateTime.UtcNow.ToString("yy_MM_dd_HH"));
            Directory.CreateDirectory(directory);

            var fileName = $"{hookInfo.Type}_{hookInfo.SubType}_{message.SystemProperties.SequenceNumber}.json";
            File.WriteAllText(Path.Combine(directory, fileName), Encoding.UTF8.GetString(message.Body), Encoding.UTF8);
        }

        private bool IsMatch(RouteConfig routeConfig, Message message, HookInformation hookInfo)
        {
            try
            {
                if (!string.Equals(routeConfig.Type, hookInfo.Type, StringComparison.InvariantCultureIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(routeConfig.SubType))
                {
                    if (!string.Equals(routeConfig.SubType, hookInfo.SubType, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                log.Error($"Error checking validity of route {routeConfig.Name} on message {message.SystemProperties.SequenceNumber}", e);
                return false;
            }
        }

        private async Task ProcessRoute(Message message, CancellationToken token, RouteConfig route, HookInformation hookInfo)
        {
            try
            {
                log.Debug($"Found maching route {route.Name} - Posting payload to {route.PostDestinationHost}");
                var request = new HttpRequestMessage(HttpMethod.Post, route.PostDestination);

                foreach (var header in hookInfo.RequestHeaders)
                {
                    request.Headers.Add(header.Key, header.Value);
                }

                if (!string.IsNullOrWhiteSpace(route.AuthenticationScheme) && !string.IsNullOrWhiteSpace(route.AuthenticationHeader))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(route.AuthenticationScheme, route.AuthenticationHeader);
                }

                request.Content = new StringContent(hookInfo.JsonData);

                var response = await httpClient.SendAsync(request, token);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    log.Debug($"HTTP response from {route.PostDestinationHost}.  - Status: {response.StatusCode}, Content: {responseContent}");
                }
                else
                {
                    log.Error($"HTTP response from {route.PostDestinationHost}. - Status: {response.StatusCode}, Content: {responseContent}");
                }
            }
            catch (Exception e)
            {
                log.Error($"Error processing route {route.Name} for message {message.SystemProperties.SequenceNumber}.", e);
            }
        }

        private Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            log.Error("Message handler encountered an exception", exceptionReceivedEventArgs.Exception);
            return Task.CompletedTask;
        }
    }
}
