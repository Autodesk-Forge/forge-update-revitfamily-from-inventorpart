using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Forge;
using Autodesk.Forge.DesignAutomation.v3;
using Autodesk.Forge.Model;
using Autodesk.Forge.Model.DesignAutomation.v3;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using RestSharp;
using System.IO;
using ActivitiesApi = Autodesk.Forge.DesignAutomation.v3.ActivitiesApi;
using Activity = Autodesk.Forge.Model.DesignAutomation.v3.Activity;
using WorkItem = Autodesk.Forge.Model.DesignAutomation.v3.WorkItem;
using WorkItemsApi = Autodesk.Forge.DesignAutomation.v3.WorkItemsApi;

namespace InventorRevitIO
{
    class Program
    {
        static string ConsumerKey = System.Environment.GetEnvironmentVariable("FORGE_CLIENT_ID");
        static string ConsumerSecret = System.Environment.GetEnvironmentVariable("FORGE_CLIENT_SECRET");
        static string EngineName = "Autodesk.Inventor+23";
        static string LocalAppPackageZip = @"C:\Temp\SATExportBundle.zip";
        static string InputPartFile = @"C:\Temp\Hairdryer.ipt";
        static string OutputSatFile = @"C:\Temp\export.sat";
        static string APPNAME = "IPTtoSAT";
        static string ACTIVITY_NAME = "IPTtoSATActivity";
        static string ALIAS = "v1";
        private static dynamic InternalToken { get; set; }

        public class Output
        {           
            public StatusEnum Status { get; set; }
            public string Message { get; set; }

            public Output(StatusEnum status, string message)
            {
                Status = status;               
                Message = message;
            }

            public enum StatusEnum
            {
                Error,
                Sucess
            }
        }

        static void Main(string[] args)
        {
            Task t = MainAsync(args);
            t.Wait();

        }

        private static async Task MainAsync(string[] args)
        {
            try
            {
                Console.WriteLine("Fetching internal token...");
                InternalToken = await GetInternalAsync();
                try
                {
                    Console.WriteLine("Creating bucket...");
                    dynamic bucket = await CreateBucket();
                    try
                    {
                        Console.WriteLine("Uploading Ipt file...");
                        dynamic uploadedobject = await UploadIptFile(bucket.bucketKey);
                        try
                        {
                            try
                            {
                                Console.WriteLine("Creating Activity...");
                                await CreateActivity();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Activity failed: " + ex.Message);
                            }
                            Console.WriteLine("Creating workitem...");
                            await CreateWorkItem(bucket.bucketKey);
                        }
                        catch (Exception ex) { Console.WriteLine("Workitem failed: " + ex.Message); }
                    }
                    catch (Exception ex) { Console.WriteLine("UploadIptFile failed: " + ex.Message); }
                }
                catch (Exception ex) { Console.WriteLine("CreateBucket failed: " + ex.Message); }
            }
            catch (Exception ex) { Console.WriteLine("GetInternalAsync failed: " + ex.Message); }
        }

        private static async Task<dynamic> CreateWorkItem(String bucketkey)
        {
            string nickName = ConsumerKey;
            Bearer bearer = (await Get2LeggedTokenAsync(new Scope[] { Scope.CodeAll })).ToObject<Bearer>();
            string downloadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketkey, Path.GetFileName(InputPartFile));
            string uploadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketkey, "export.sat");
            JObject IptFile = new JObject
                {
                  new JProperty("url", downloadUrl),
                  new JProperty("headers",
                  new JObject{
                    new JProperty("Authorization", "Bearer " + InternalToken.access_token)
                  })
                };
            JObject resultSat = new JObject
                {
                  new JProperty("verb", "put"),
                  new JProperty("url", uploadUrl),
                  new JProperty("headers",
                  new JObject{
                    new JProperty("Authorization", "Bearer " + InternalToken.access_token)
                  })
                };
            WorkItem workItemSpec = new WorkItem(
              null, string.Format("{0}.{1}+{2}", nickName, ACTIVITY_NAME, ALIAS),
              new Dictionary<string, JObject>()
              {{ "HostDwg",  IptFile },{ "resultSAT", resultSat  }}, null);
            WorkItemsApi workItemApi = new WorkItemsApi();
            workItemApi.Configuration.AccessToken = bearer.AccessToken;
            WorkItemStatus newWorkItem = await workItemApi.WorkItemsCreateWorkItemsAsync(null, null, workItemSpec);
            // wait until is ready...
            for (int i = 0; i < 1000; i++)
            {
                System.Threading.Thread.Sleep(1000);
                WorkItemStatus workItemStatus = await workItemApi.WorkItemsGetWorkitemsStatusAsync(newWorkItem.Id);
                if (workItemStatus.Status == WorkItemStatus.StatusEnum.Pending || workItemStatus.Status == WorkItemStatus.StatusEnum.Inprogress) continue;
                break;
            }
            await DownloadToDocs(uploadUrl);
            return new Output(Output.StatusEnum.Sucess, "Activity created");
        }

        private static async Task<dynamic> CreateActivity()
        {
            Bearer bearer = (await Get2LeggedTokenAsync(new Scope[] { Scope.CodeAll })).ToObject<Bearer>();
            string nickName = ConsumerKey;
            ////  uncomment these lines to clear all appbundles & activities under your account
            //  Autodesk.Forge.DesignAutomation.v3.ForgeAppsApi forgeAppApi = new ForgeAppsApi();
            //  forgeAppApi.Configuration.AccessToken = bearer.AccessToken;
            //  await forgeAppApi.ForgeAppsDeleteUserAsync("me");          
            AppBundlesApi appBundlesApi = new AppBundlesApi();
            appBundlesApi.Configuration.AccessToken = bearer.AccessToken;
            PageString appBundles = await appBundlesApi.AppBundlesGetItemsAsync();
            bool existAppBundle = false;
            foreach (string appName in appBundles.Data)
            {
                if (appName.Contains(string.Format("{0}.{1}+{2}", nickName, APPNAME, ALIAS)))
                {
                    existAppBundle = true;
                    continue;
                }
            }
            if (!existAppBundle)
            {
                if (!System.IO.File.Exists(LocalAppPackageZip)) return new Output(Output.StatusEnum.Error, "Change Parameter bundle not found at " + LocalAppPackageZip);
                // create bundle
                AppBundle appBundleSpec = new AppBundle(APPNAME, null, "Autodesk.Inventor+23", null, null, APPNAME, null, APPNAME);
                AppBundle newApp = await appBundlesApi.AppBundlesCreateItemAsync(appBundleSpec);
                if (newApp == null) return new Output(Output.StatusEnum.Error, "Cannot create new app");
                // create alias
                Alias aliasSpec = new Alias(1, null, ALIAS);
                Alias newAlias = await appBundlesApi.AppBundlesCreateAliasAsync(APPNAME, aliasSpec);
                // upload the zip with .bundle
                RestClient uploadClient = new RestClient(newApp.UploadParameters.EndpointURL);
                RestRequest request = new RestRequest(string.Empty, Method.POST);
                request.AlwaysMultipartFormData = true;
                foreach (KeyValuePair<string, object> x in newApp.UploadParameters.FormData)
                    request.AddParameter(x.Key, x.Value);
                request.AddFile("file", LocalAppPackageZip);
                request.AddHeader("Cache-Control", "no-cache");
                var res = await uploadClient.ExecuteTaskAsync(request);
            }
            ActivitiesApi activitiesApi = new ActivitiesApi();
            activitiesApi.Configuration.AccessToken = bearer.AccessToken;
            PageString activities = await activitiesApi.ActivitiesGetItemsAsync();
            bool existActivity = false;
            foreach (string activity in activities.Data)
            {
                if (activity.Contains(string.Format("{0}.{1}+{2}", nickName, ACTIVITY_NAME, ALIAS)))
                {
                    existActivity = true;
                    continue;
                }
            }
            if (!existActivity)
            {
                // create activity
                string commandLine = string.Format(@"$(engine.path)\\inventorcoreconsole.exe /i $(args[HostDwg].path) /al $(appbundles[{0}].path)", APPNAME);
                ModelParameter iptFile = new ModelParameter(false, false, ModelParameter.VerbEnum.Get, "Input Ipt File", true, Path.GetFileName(InputPartFile));
                ModelParameter result = new ModelParameter(false, false, ModelParameter.VerbEnum.Put, "Resulting Ipt to SAT", true, "export.sat");

                Activity activitySpec = new Activity(
                 new List<string> { commandLine },
                  new Dictionary<string, ModelParameter>()
                  {
                         { "HostDwg", iptFile },
                         { "resultSAT",result},
                  },
                  EngineName,
                  new List<string>() { string.Format("{0}.{1}+{2}", nickName, APPNAME, ALIAS) },
                  null,
                  ACTIVITY_NAME,
                  null,
                  ACTIVITY_NAME
                );
                Activity newActivity = await activitiesApi.ActivitiesCreateItemAsync(activitySpec);
                Alias aliasSpec = new Alias(1, null, ALIAS);
                Alias newAlias = await activitiesApi.ActivitiesCreateAliasAsync(ACTIVITY_NAME, aliasSpec);
            }
            return new Output(Output.StatusEnum.Sucess, "Activity created");
        }
            
        private async static Task<dynamic> GetInternalAsync()
        {
            if (InternalToken == null || InternalToken.ExpiresAt < DateTime.UtcNow)
            {
                InternalToken = await Get2LeggedTokenAsync(new Scope[] { Scope.BucketCreate, Scope.BucketRead, Scope.DataRead, Scope.DataCreate });
                InternalToken.ExpiresAt = DateTime.UtcNow.AddSeconds(InternalToken.expires_in);
            }
            return InternalToken;
        }

        private async static Task<dynamic> Get2LeggedTokenAsync(Scope[] scopes)
        {
            TwoLeggedApi oauth = new TwoLeggedApi();
            string grantType = "client_credentials";
            dynamic bearer = await oauth.AuthenticateAsync(
             ConsumerKey,
             ConsumerSecret,
              grantType,
              scopes);
            return bearer;
        }
                      
        private async static Task<dynamic> CreateBucket()
        {
            string bucketKey = "inventorio" + Guid.NewGuid().ToString("N").ToLower();
            PostBucketsPayload postBucket = new PostBucketsPayload(bucketKey, null, PostBucketsPayload.PolicyKeyEnum.Transient);
            BucketsApi bucketsApi = new BucketsApi();
            bucketsApi.Configuration.AccessToken = InternalToken.access_token;  
            dynamic newBucket = await bucketsApi.CreateBucketAsync(postBucket);
            return newBucket;
        }

        private async static Task<dynamic> UploadIptFile(string bucketKey)
        {
            ObjectsApi objects = new ObjectsApi();
            objects.Configuration.AccessToken = InternalToken.access_token;
            dynamic uploadedObj = null;
            string filename = Path.GetFileName(InputPartFile);

            using (StreamReader streamReader = new StreamReader(InputPartFile))
            {
                uploadedObj = await objects.UploadObjectAsync(bucketKey,
                      filename, (int)streamReader.BaseStream.Length, streamReader.BaseStream,
                      "application/octet-stream");
            }
            return uploadedObj;
        }

        public static async Task<dynamic> DownloadToDocs(string url)
        {
            IRestClient client = new RestClient("https://developer.api.autodesk.com/");
            RestRequest request = new RestRequest(url, Method.GET);
            request.AddHeader("Authorization", "Bearer " + InternalToken.access_token);
            request.AddHeader("Accept-Encoding", "gzip, deflate");
            IRestResponse response = await client.ExecuteTaskAsync(request);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                return new Output(Output.StatusEnum.Error, "Not able to download to local drive");
            }
            else
            {
                File.WriteAllBytes(OutputSatFile, response.RawBytes);
                return new Output(Output.StatusEnum.Sucess, "Downloaded successfully");
            }           
        }
    }
}
