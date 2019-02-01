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
    public class DesignAutomation4Revit
    {
        private const string APPNAME = "UpdateFamilyApp";
        private const string APPBUNBLENAME = "UpdateFamily.zip";
        private const string ACTIVITY_NAME = "UpdateFamilyActivity";
        private const string ENGINE_NAME = "Autodesk.Revit+2019";
        private const string RFA_TEMPLATE = "MetricGenericModel.rft";

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

        public DesignAutomation4Revit()
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
                if (!File.Exists(packageZipPath)) throw new Exception("UpdateFamily appbundle not found at " + packageZipPath);

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
                string commandLine = string.Format(@"$(engine.path)\\revitcoreconsole.exe /i $(args[rvtFile].path) /al $(appbundles[{0}].path)", APPNAME);
                Activity activitySpec = new Activity()
                {
                    Id = ACTIVITY_NAME,
                    Appbundles = new List<string>() { AppBundleFullName },
                    CommandLine = new List<string>() { commandLine },
                    Engine = ENGINE_NAME,
                    Parameters = new Dictionary<string, Parameter>()
                    {
                        { "rvtFile", new Parameter() { Description = "Input Revit Model", LocalName = "$(rvtFile)", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "inputGeometry", new Parameter() { Description = "Input SAT File", LocalName = "InputGeometry.sat", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "familyTemplate", new Parameter() { Description = "Input RFT File", LocalName = "FamilyTemplate.rft", Ondemand = false, Required = true, Verb = Verb.Get, Zip = false } },
                        { "result", new Parameter() { Description = "Modifed Revit Model", LocalName = "ResultModel.rvt", Ondemand = false, Required = true, Verb = Verb.Put, Zip = false } }
                    }
                };
                Activity newActivity = await _designAutomation.CreateActivityAsync(activitySpec);

                // specify the alias for this Activity
                Alias aliasSpec = new Alias() { Id = Alias, Version = 1 };
                Alias newAlias = await _designAutomation.CreateActivityAliasAsync(ACTIVITY_NAME, aliasSpec);
            }
        }

        private async Task EnsureTemplateExists(string contentRootPath)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Credentials.GetAppSetting("AWS_ACCESS_KEY"), Credentials.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);
            var keys = await client.GetAllObjectKeysAsync(Utils.S3BucketName, null, null);
            if (keys.Contains(RFA_TEMPLATE)) return;

            // not there yet, let's create
            string rftPath = Path.Combine(contentRootPath, RFA_TEMPLATE);
            await client.UploadObjectFromFilePathAsync(Utils.S3BucketName, RFA_TEMPLATE, rftPath, null);
        }

        private async Task<XrefTreeArgument> BuildBIM360DownloadURL(string userAccessToken, string projectId, string versionId)
        {
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic version = await versionApi.GetVersionAsync(projectId, versionId);
            //dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

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

        private async Task<dynamic> PreWorkNewVersion(string userAccessToken, string projectId, string versionId)
        {
            // get version
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);

            // get item
            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = userAccessToken;
            string itemId = versionItem.data.id;
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            string folderId = item.data.relationships.parent.data.id;
            string fileName = item.data.attributes.displayName;

            // prepare storage
            ProjectsApi projectApi = new ProjectsApi();
            projectApi.Configuration.AccessToken = userAccessToken;
            StorageRelationshipsTargetData storageRelData = new StorageRelationshipsTargetData(StorageRelationshipsTargetData.TypeEnum.Folders, folderId);
            CreateStorageDataRelationshipsTarget storageTarget = new CreateStorageDataRelationshipsTarget(storageRelData);
            CreateStorageDataRelationships storageRel = new CreateStorageDataRelationships(storageTarget);
            BaseAttributesExtensionObject attributes = new BaseAttributesExtensionObject(string.Empty, string.Empty, new JsonApiLink(string.Empty), null);
            CreateStorageDataAttributes storageAtt = new CreateStorageDataAttributes(fileName, attributes);
            CreateStorageData storageData = new CreateStorageData(CreateStorageData.TypeEnum.Objects, storageAtt, storageRel);
            CreateStorage storage = new CreateStorage(new JsonApiVersionJsonapi(JsonApiVersionJsonapi.VersionEnum._0), storageData);
            dynamic storageCreated = await projectApi.PostStorageAsync(projectId, storage);

            string[] storageIdParams = ((string)storageCreated.data.id).Split('/');
            string[] bucketKeyParams = storageIdParams[storageIdParams.Length - 2].Split(':');
            string bucketKey = bucketKeyParams[bucketKeyParams.Length - 1];
            string objectName = storageIdParams[storageIdParams.Length - 1];

            string uploadUrl = string.Format("https://developer.api.autodesk.com/oss/v2/buckets/{0}/objects/{1}", bucketKey, objectName);

            return new StorageInfo
            {
                fileName = fileName,
                itemId = item.data.id,
                storageId = storageCreated.data.id,
                uploadUrl = uploadUrl
            };
        }

        private struct StorageInfo
        {
            public string fileName;
            public string itemId;
            public string storageId;
            public string uploadUrl;
        }

        private async Task<XrefTreeArgument> BuildBIM360UploadURL(string userAccessToken, StorageInfo info)
        {
            return new XrefTreeArgument()
            {
                Url = info.uploadUrl,
                Verb = Verb.Put
            };
        }

        private async Task<JObject> BuildS3UploadURL(string resultFilename)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Credentials.GetAppSetting("AWS_ACCESS_KEY"), Credentials.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName))
                await client.EnsureBucketExistsAsync(Utils.S3BucketName);

            Dictionary<string, object> props = new Dictionary<string, object>();
            props.Add("Verb", "PUT");
            Uri uploadToS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, resultFilename, DateTime.Now.AddMinutes(10), props));

            return new JObject
            {
                new JProperty("verb", "PUT"),
                new JProperty("url", uploadToS3.ToString())
            };
        }

        private async Task<XrefTreeArgument> BuildS3DownloadURL(string fileName)
        {
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(Credentials.GetAppSetting("AWS_ACCESS_KEY"), Credentials.GetAppSetting("AWS_SECRET_KEY"));
            IAmazonS3 client = new AmazonS3Client(awsCredentials, Amazon.RegionEndpoint.USWest2);

            if (!await client.DoesS3BucketExistAsync(Utils.S3BucketName))
            {
                throw new Exception("Bucket does not exist");
            }

            var keys = await client.GetAllObjectKeysAsync(Utils.S3BucketName, null, null);
            if (!keys.Contains(fileName))
            {
                throw new Exception("Object does not exist in bucket");
            }

            Uri downloadFromS3 = new Uri(client.GeneratePreSignedURL(Utils.S3BucketName, fileName, DateTime.Now.AddMinutes(5), null));

            return new XrefTreeArgument()
            {
                Url = downloadFromS3.ToString(),
                Verb = Verb.Get
            };
        }

        public async Task<string> GetFolderId(string projectId, string versionId, string userAccessToken)
        {
            VersionsApi versionApi = new VersionsApi();
            versionApi.Configuration.AccessToken = userAccessToken;
            dynamic versionItem = await versionApi.GetVersionItemAsync(projectId, versionId);
            string itemId = versionItem.data.id;

            ItemsApi itemApi = new ItemsApi();
            itemApi.Configuration.AccessToken = userAccessToken;
            dynamic item = await itemApi.GetItemAsync(projectId, itemId);
            string folderId = item.data.relationships.parent.data.id; ;

            return folderId;
        }

        public async Task<List<string>> GetRevitFileVersionId(string projectId, string versionId, string userAccessToken)
        {
            string folderId = await GetFolderId(projectId, versionId, userAccessToken);

            FoldersApi folderApi = new FoldersApi();
            folderApi.Configuration.AccessToken = userAccessToken;
            dynamic contents = await folderApi.SearchFolderContentsAsync(
                projectId, folderId, 0,
                new List<string>(new string[] { "rvt" }));

            if (contents.Data.included.Count == 0)
            {
                throw new Exception("No Revit file found in folder!");
            }

            List<string> versionIds = new List<string>();
            foreach (KeyValuePair<string, dynamic> includedItem in new DynamicDictionaryItems(contents.Data.included))
                if (includedItem.Value.attributes.hidden == false)
                    if (includedItem.Value.relationships.tip.data.type == "versions")
                        versionIds.Add(includedItem.Value.relationships.tip.data.id);

            return versionIds;
        }


        public async Task StartUploadFamily(string userId, string projectId, string versionId, string contentRootPath)
        {
            // uncomment these lines to clear all appbundles & activities under your account
            //await _designAutomation.DeleteForgeAppAsync("me");

            Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            // find Revit files on the folder where the IPT is
            List<string> rvtFilesOnFolder = await GetRevitFileVersionId(projectId, Utils.Base64Decode(versionId), credentials.TokenInternal);

            // check Design Automation for Revit setup
            await EnsureAppBundle(contentRootPath);
            await EnsureActivity();
            await EnsureTemplateExists(contentRootPath);

            // at this point we're triggering one Design Automation workItem for each RVT file on the folder,
            // which can be expensive, so better to filter out... for this sample, let's just do it
            foreach (string fileInFolder in rvtFilesOnFolder)
            {
                StorageInfo info = await PreWorkNewVersion(credentials.TokenInternal, projectId, fileInFolder);
                string satFileName = versionId + ".sat";
                string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/revit/{1}/{2}/{3}/{4}/{5}", Credentials.GetAppSetting("FORGE_WEBHOOK_CALLBACK_HOST"), userId, projectId, info.itemId.Base64Encode(), info.storageId.Base64Encode(), info.fileName.Base64Encode());

                WorkItem workItemSpec = new WorkItem()
                {
                    ActivityId = ActivityFullName,
                    Arguments = new Dictionary<string, IArgument>()
                    {
                        { "rvtFile", await BuildBIM360DownloadURL(credentials.TokenInternal, projectId, fileInFolder) },
                        { "inputGeometry", await BuildS3DownloadURL(satFileName) },
                        { "familyTemplate", await BuildS3DownloadURL(RFA_TEMPLATE) },
                        { "result",  await BuildBIM360UploadURL(credentials.TokenInternal, info)  },
                        { "onComplete", new XrefTreeArgument { Verb = Verb.Post, Url = callbackUrl } }
                    }
                };
                WorkItemStatus workItemStatus = await _designAutomation.CreateWorkItemsAsync(workItemSpec);
            }
        }
    }
}