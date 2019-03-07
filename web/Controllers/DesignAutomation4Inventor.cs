/////////////////////////////////////////////////////////////////////
// Copyright (c) Autodesk, Inc. All rights reserved
// Written by Forge Partner Development
//
// Permission to use, copy, modify, and distribute this software in
// object code form for any purpose and without fee is hereby granted,
// provided that the above copyright notice appears in all copies and
// that both that copyright notice and the limited warranty and
// restricted rights notice below appear in all supporting
// documentation.
//
// AUTODESK PROVIDES THIS PROGRAM "AS IS" AND WITH ALL FAULTS.
// AUTODESK SPECIFICALLY DISCLAIMS ANY IMPLIED WARRANTY OF
// MERCHANTABILITY OR FITNESS FOR A PARTICULAR USE.  AUTODESK, INC.
// DOES NOT WARRANT THAT THE OPERATION OF THE PROGRAM WILL BE
// UNINTERRUPTED OR ERROR FREE.
/////////////////////////////////////////////////////////////////////

using Amazon.S3;
using Autodesk.Forge;
using Autodesk.Forge.Core;
using Autodesk.Forge.DesignAutomation;
using Autodesk.Forge.DesignAutomation.Model;
using Autodesk.Forge.Model;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Activity = Autodesk.Forge.DesignAutomation.Model.Activity;
using Alias = Autodesk.Forge.DesignAutomation.Model.Alias;
using AppBundle = Autodesk.Forge.DesignAutomation.Model.AppBundle;
using Parameter = Autodesk.Forge.DesignAutomation.Model.Parameter;
using WorkItem = Autodesk.Forge.DesignAutomation.Model.WorkItem;
using WorkItemStatus = Autodesk.Forge.DesignAutomation.Model.WorkItemStatus;

namespace Inventor2Revit.Controllers
{
    public class DesignAutomation4Inventor
    {
        private const string APPNAME = "ExportSAT";
        private const string APPBUNBLENAME = "ExportSAT.zip";
        private const string ACTIVITY_NAME = "ExportSAT";
        private const string ENGINE_NAME = "Autodesk.Inventor+23";

        /// NickName.AppBundle+Alias
        private string AppBundleFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, APPNAME, Alias); } }
        /// NickName.Activity+Alias
        private string ActivityFullName { get { return string.Format("{0}.{1}+{2}", Utils.NickName, ACTIVITY_NAME, Alias); } }
        /// Prefix for AppBundles and Activities
        public static string NickName { get { return Credentials.GetAppSetting("FORGE_CLIENT_ID"); } }
        /// Alias for the app (e.g. DEV, STG, PROD). This value may come from an environment variable
        public static string Alias { get { return "dev"; } }
        // Design Automation v3 API
        private DesignAutomationClient _designAutomation;

        public DesignAutomation4Inventor()
        {
            // need to initialize manually as this class runs in background
            ForgeService service =
                new ForgeService(
                    new HttpClient(
                        new ForgeHandler(Microsoft.Extensions.Options.Options.Create(new ForgeConfiguration()
                        {
                            ClientId = Credentials.GetAppSetting("FORGE_CLIENT_ID"),
                            ClientSecret = Credentials.GetAppSetting("FORGE_CLIENT_SECRET")
                        }))
                        {
                            InnerHandler = new HttpClientHandler()
                        })
                );
            _designAutomation = new DesignAutomationClient(service);
        }

        public async Task EnsureAppBundle(string contentRootPath)
        {
            // get the list and check for the name
            Page<string> appBundles = await _designAutomation.GetAppBundlesAsync();
            bool existAppBundle = false;
            foreach (string appName in appBundles.Data)
            {
                if (appName.Contains(AppBundleFullName))
                {
                    existAppBundle = true;
                    continue;
                }
            }

            if (!existAppBundle)
            {
                // check if ZIP with bundle is here
                string packageZipPath = Path.Combine(contentRootPath + "/bundles/", APPBUNBLENAME);
                if (!File.Exists(packageZipPath)) throw new Exception("ExportSAT appbundle not found at " + packageZipPath);

                AppBundle appBundleSpec = new AppBundle()
                {
                    Package = APPNAME,
                    Engine = ENGINE_NAME,
                    Id = APPNAME,
                    Description = string.Format("Description for {0}", APPBUNBLENAME),

                };
                AppBundle newAppVersion = await _designAutomation.CreateAppBundleAsync(appBundleSpec);
                if (newAppVersion == null) throw new Exception("Cannot create new app");

                // create alias pointing to v1
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateAppBundleAliasAsync(APPNAME, aliasSpec);

                // upload the zip with .bundle
                RestClient uploadClient = new RestClient(newAppVersion.UploadParameters.EndpointURL);
                RestRequest request = new RestRequest(string.Empty, Method.POST);
                request.AlwaysMultipartFormData = true;
                foreach (KeyValuePair<string, string> x in newAppVersion.UploadParameters.FormData) request.AddParameter(x.Key, x.Value);
                request.AddFile("file", packageZipPath);
                request.AddHeader("Cache-Control", "no-cache");
                await uploadClient.ExecuteTaskAsync(request);
            }
        }

        private async Task EnsureActivity()
        {
            Page<string> activities = await _designAutomation.GetActivitiesAsync();

            bool existActivity = false;
            foreach (string activity in activities.Data)
            {
                if (activity.Contains(ActivityFullName))
                {
                    existActivity = true;
                    continue;
                }
            }

            if (!existActivity)
            {
                // create activity
                string commandLine = string.Format(@"$(engine.path)\\InventorCoreConsole.exe /i $(args[InventorDoc].path) /al $(appbundles[{0}].path)", APPNAME);
                Activity activitySpec = new Activity()
                {
                    Id = ACTIVITY_NAME,
                    Appbundles = new List<string>() { AppBundleFullName },
                    CommandLine = new List<string>() { commandLine },
                    Engine = ENGINE_NAME,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "InventorDoc", new Parameter() { Description = "Input IPT File", LocalName = "$(InventorDoc)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "export", new Parameter() { Description = "Resulting SAT File", LocalName = "export.sat", Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(ACTIVITY_NAME, aliasSpec);
            }
        }

        private async Task<XrefTreeArgument> BuildDownloadURL(string userAccessToken, string projectId, string versionId)
        {
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic version = await versionApi.GetVersionAsync(projectId, versionId);
            dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

            string[] versionItemParams = ((string)version.data.relationships.storage.data.id).Split('/');
            string[] bucketKeyParams = versionItemParams[versionItemParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = versionItemParams[versionItemParams.Length - 1];
            string downloadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

            return new XrefTreeArgument()
            {
                Url = downloadUrl,
                Verb = Verb.Get,
                Headers = new Dictionary<string, string>()
                {
                    { "Authorization", "Bearer " + userAccessToken }
                }
            };
        }

        private async Task<XrefTreeArgument> BuildUploadURL(string resultFilename)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Credentials.GetAppSetting("AWS_ACCESS_KEY"), Credentials.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName))
                await client.EnsureBucketExistsAsync(Utils.S3BucketName);

            Dictionary<string, object> props = new Dictionary<string, object>();
            props.Add("Verb", "PUT");
            Uri uploadToS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, resultFilename, DateTime.Now.AddMinutes(10), props));

            return new XrefTreeArgument()
            {
                Url = uploadToS3.ToString(),
                Verb = Verb.Put
            };
        }

        public async Task StartInventorIPT2SAT(string userId, string projectId, string versionId, string contentRootPath)
        {
            // uncomment these lines to clear all appbundles & activities under your account
            //await _designAutomation.DeleteForgeAppAsync("me");

            Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            await EnsureAppBundle(contentRootPath);
            await EnsureActivity();

            string resultFilename = versionId.Base64Encode() + ".sat";
            string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/inventor/{1}/{2}/{3}", Credentials.GetAppSetting("FORGE_WEBHOOK_URL"), userId, projectId, versionId.Base64Encode());

            WorkItem workItemSpec = new WorkItem()
            {
                ActivityId = ActivityFullName,
                Arguments = new Dictionary<string, IArgument>()
                {
                    { "InventorDoc", await BuildDownloadURL(credentials.TokenInternal, projectId, versionId) },
                    { "export", await BuildUploadURL(resultFilename)  },
                    { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                }
            };
            WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemsAsync(workItemSpec);
        }
    }
}