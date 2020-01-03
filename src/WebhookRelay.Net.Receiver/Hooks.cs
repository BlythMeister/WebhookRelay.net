using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WebhookRelay.Net.Models;

namespace WebhookRelay.Net.Receiver
{
    public static class Hooks
    {
        [FunctionName("Status")]
        public static Task<HttpResponseMessage> RunStatus([HttpTrigger(AuthorizationLevel.Anonymous, "GET")]HttpRequestMessage req, TraceWriter log)
        {
            log.Info("Status Function Triggered");
            return Task.FromResult(req.CreateResponse(HttpStatusCode.OK));
        }

        [FunctionName("GitHub")]
        public static Task<HttpResponseMessage> RunGitHub([HttpTrigger("POST", WebHookType = "github")]HttpRequestMessage req, TraceWriter log, IBinder binder)
        {
            return Run("GitHub", req, log, binder);
        }

        [FunctionName("Slack")]
        public static Task<HttpResponseMessage> RunSlack([HttpTrigger("POST", WebHookType = "slack")]HttpRequestMessage req, TraceWriter log, IBinder binder)
        {
            return Run("Slack", req, log, binder);
        }

        [FunctionName("Generic")]
        public static Task<HttpResponseMessage> RunGeneric([HttpTrigger(AuthorizationLevel.Function, "POST", WebHookType = "genericJson")]HttpRequestMessage req, TraceWriter log, IBinder binder)
        {
            return Run("Generic", req, log, binder);
        }

        private static async Task<HttpResponseMessage> Run(string type, HttpRequestMessage req, TraceWriter log, IBinder binder)
        {
            try
            {
                log.Info($"{type} WebHook Received");

                var queryParams = req.GetQueryNameValuePairs().ToList();
                var tenant = queryParams.FirstOrDefault(x => string.Equals(x.Key, "tenant", StringComparison.OrdinalIgnoreCase)).Value;

                if (string.IsNullOrWhiteSpace(tenant))
                {
                    log.Error($"{type} - Missing tenant parameter");
                    return req.CreateErrorResponse(HttpStatusCode.BadRequest, "Please provide tenant parameter");
                }

                var validTenants = Environment.GetEnvironmentVariable("ValidTenants")?.Split(';');

                if (validTenants == null || !validTenants.Any(x => string.Equals(x, tenant, StringComparison.InvariantCultureIgnoreCase)))
                {
                    log.Error($"{type} - Tenant {tenant} is not allowed");
                    return req.CreateErrorResponse(HttpStatusCode.Forbidden, "Please provide valid tenant parameter");
                }

                var subType = queryParams.FirstOrDefault(x => string.Equals(x.Key, "subType", StringComparison.OrdinalIgnoreCase)).Value;

                if (string.IsNullOrWhiteSpace(subType))
                {
                    log.Error($"{type} - Missing subType parameter");
                    return req.CreateErrorResponse(HttpStatusCode.BadRequest, "Please provide subType parameter");
                }

                log.Info($"{type} WebHook (tenant: {tenant} | subType: {subType})");

                var jsonData = await req.Content.ReadAsStringAsync();

                var hookInformation = new HookInformation { Type = type, SubType = subType, JsonData = jsonData, RequestHeaders = req.Headers.ToList() };

                var serviceBusQueueAttribute = new ServiceBusAttribute($"hooks_{tenant}", AccessRights.Manage) { Connection = "AzureServiceBus" };

                var queueMessageJson = JsonConvert.SerializeObject(hookInformation);
                var outputMessages = await binder.BindAsync<IAsyncCollector<string>>(serviceBusQueueAttribute);
                await outputMessages.AddAsync(queueMessageJson);

                return req.CreateResponse(HttpStatusCode.OK);
            }
            catch (Exception e)
            {
                log.Error($"Error Processing {type} Hook", e);
                return req.CreateErrorResponse(HttpStatusCode.InternalServerError, e);
            }
        }
    }
}

