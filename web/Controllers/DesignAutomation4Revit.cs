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
using Autodesk.Forge.DesignAutomation.v3;
using Autodesk.Forge.Model;
using Autodesk.Forge.Model.DesignAutomation.v3;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using ActivitiesApi = Autodesk.Forge.DesignAutomation.v3.ActivitiesApi;
using Activity = Autodesk.Forge.Model.DesignAutomation.v3.Activity;
using WorkItem = Autodesk.Forge.Model.DesignAutomation.v3.WorkItem;
using WorkItemsApi = Autodesk.Forge.DesignAutomation.v3.WorkItemsApi;

namespace Inventor2Revit.Controllers
{
    public class DesignAutomation4Revit
    {
        private const string APPNAME = "UpdateFamilyApp";
        private const string APPBUNBLENAME = "UpdateFamilyAppBundle.zip";
        private const string ALIAS = "v1";
        private const string ACTIVITY_NAME = "UpdateFamilyActivity";
        private string ACTIVITY_NAME_FULL { get { return string.Format("{0}.{1}+{2}", Utils.NickName, ACTIVITY_NAME, ALIAS); } }

        private const string RFA_TEMPLATE = "MetricGenericModel.rft";

        private async Task EnsureAppBundle(string appAccessToken, string contentRootPath)
        {
            //List<string> apps = await da.GetAppBundles(nickName);
            AppBundlesApi appBundlesApi = new AppBundlesApi();
            appBundlesApi.Configuration.AccessToken = appAccessToken;

            // at this point we can either call get by alias/id and catch or get a list and check
            //dynamic appBundle = await appBundlesApi.AppbundlesByIdAliasesByAliasIdGetAsync(APPNAME, ALIAS);

            // or get the list and check for the name
            PageString appBundles = await appBundlesApi.AppBundlesGetItemsAsync();
            bool existAppBundle = false;
            foreach (string appName in appBundles.Data)
            {
                if (appName.Contains(string.Format("{0}.{1}+{2}", Utils.NickName, APPNAME, ALIAS)))
                {
                    existAppBundle = true;
                    continue;
                }
            }

            if (!existAppBundle)
            {
                // check if ZIP with bundle is here
                string packageZipPath = Path.Combine(contentRootPath, APPBUNBLENAME);
                if (!System.IO.File.Exists(packageZipPath)) throw new Exception("UpdateFamily appbundle not found at " + packageZipPath);

                // create bundle
                AppBundle appBundleSpec = new AppBundle(APPNAME, null, "Autodesk.Revit+2019", null, null, APPNAME, null, APPNAME);
                AppBundle newApp = await appBundlesApi.AppBundlesCreateItemAsync(appBundleSpec);
                if (newApp == null) throw new Exception("Cannot create new app");

                // create alias
                Alias aliasSpec = new Alias(1, null, ALIAS);
                Alias newAlias = await appBundlesApi.AppBundlesCreateAliasAsync(APPNAME, aliasSpec);

                // upload the zip with .bundle
                RestClient uploadClient = new RestClient(newApp.UploadParameters.EndpointURL);
                RestRequest request = new RestRequest(string.Empty, Method.POST);
                request.AlwaysMultipartFormData = true;
                foreach (KeyValuePair<string, object> x in newApp.UploadParameters.FormData)
                    request.AddParameter(x.Key, x.Value);
                request.AddFile("file", packageZipPath);
                request.AddHeader("Cache-Control", "no-cache");
                var res = await uploadClient.ExecuteTaskAsync(request);
            }
        }

        private async Task EnsureActivity(string appAccessToken)
        {
            ActivitiesApi activitiesApi = new ActivitiesApi();
            activitiesApi.Configuration.AccessToken = appAccessToken;
            PageString activities = await activitiesApi.ActivitiesGetItemsAsync();

            bool existActivity = false;
            foreach (string activity in activities.Data)
            {
                if (activity.Contains(ACTIVITY_NAME_FULL))
                {
                    existActivity = true;
                    continue;
                }
            }

            if (!existActivity)
            {
                // create activity
                string commandLine = string.Format(@"$(engine.path)\\revitcoreconsole.exe /i $(args[rvtFile].path) /al $(appbundles[{0}].path)", APPNAME);
                ModelParameter rvtFile = new ModelParameter(false, false, ModelParameter.VerbEnum.Get, "Input Revit File", true, "$(rvtFile)");
                ModelParameter satFile = new ModelParameter(false, false, ModelParameter.VerbEnum.Get, "Input SAT File", true, "InputGeometry.sat");
                ModelParameter rftFile = new ModelParameter(false, false, ModelParameter.VerbEnum.Get, "Input RFT File", true, "FamilyTemplate.rft");
                ModelParameter result = new ModelParameter(false, false, ModelParameter.VerbEnum.Put, "Resulting JSON File", true, "ResultModel.rvt");
                Activity activitySpec = new Activity(
                  new List<string>() { commandLine },
                  new Dictionary<string, ModelParameter>() {
                    { "rvtFile", rvtFile },
                    { "inputGeometry", satFile },
                    { "familyTemplate", rftFile },
                    { "result", result }
                  },
                  "Autodesk.Revit+2019",
                  new List<string>() { string.Format("{0}.{1}+{2}", Utils.NickName, APPNAME, ALIAS) },
                  null,
                  ACTIVITY_NAME,
                  null,
                  ACTIVITY_NAME);
                Activity newActivity = await activitiesApi.ActivitiesCreateItemAsync(activitySpec);

                // create alias
                Alias aliasSpec = new Alias(1, null, ALIAS);
                Alias newAlias = await activitiesApi.ActivitiesCreateAliasAsync(ACTIVITY_NAME, aliasSpec);
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

        private async Task<JObject> BuildBIM360DownloadURL(string userAccessToken, string projectId, string versionId)
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

            return new JObject
            {
                new JProperty("url", downloadUrl),
                new JProperty("headers",
                new JObject{
                    new JProperty("Authorization", "Bearer " + userAccessToken)
                })
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

        private async Task<JObject> BuildBIM360UploadURL(string userAccessToken, StorageInfo info)
        {
            return new JObject
            {
                new JProperty("verb", "PUT"),
                new JProperty("url", info.uploadUrl),
                new JProperty("headers",
                new JObject{
                    new JProperty("Authorization", "Bearer " + userAccessToken)
                })
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

        private async Task<JObject> BuildS3DownloadURL(string fileName)
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

            return new JObject
            {
                new JProperty("verb", "GET"),
                new JProperty("url", downloadFromS3.ToString())
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
            TwoLeggedApi oauth = new TwoLeggedApi();
            string appAccessToken = (await oauth.AuthenticateAsync(Credentials.GetAppSetting("FORGE_CLIENT_ID"), Credentials.GetAppSetting("FORGE_CLIENT_SECRET"), oAuthConstants.CLIENT_CREDENTIALS, new Scope[] { Scope.CodeAll })).ToObject<Bearer>().AccessToken;

            // uncomment these lines to clear all appbundles & activities under your account (for testing)
            //ForgeAppsApi forgeAppApi = new ForgeAppsApi();
            //forgeAppApi.Configuration.AccessToken = appAccessToken;
            //await forgeAppApi.ForgeAppsDeleteUserAsync("me");

            Credentials credentials = await Credentials.FromDatabaseAsync(userId);

            // find Revit files on the folder where the IPT is
            List<string> rvtFilesOnFolder = await GetRevitFileVersionId(projectId, Utils.Base64Decode(versionId), credentials.TokenInternal);

            // check Design Automation for Revit setup
            await EnsureAppBundle(appAccessToken, contentRootPath);
            await EnsureActivity(appAccessToken);
            await EnsureTemplateExists(contentRootPath);

            // at this point we're triggering one Design Automation workItem for each RVT file on the folder,
            // which can be expensive, so better to filter out... for this sample, let's just do it
            foreach (string fileInFolder in rvtFilesOnFolder)
            {
                StorageInfo info = await PreWorkNewVersion(credentials.TokenInternal, projectId, fileInFolder);
                string satFileName = versionId + ".sat";
                string callbackUrl = string.Format("{0}/api/forge/callback/designautomation/revit/{1}/{2}/{3}/{4}/{5}", Credentials.GetAppSetting("FORGE_WEBHOOK_CALLBACK_HOST"), userId, projectId, info.itemId.Base64Encode(), info.storageId.Base64Encode(), info.fileName.Base64Encode());

                try
                {
                    WorkItem workItemSpec = new WorkItem(
                      null,
                      ACTIVITY_NAME_FULL,
                      new Dictionary<string, JObject>()
                      {
                        { "rvtFile", await BuildBIM360DownloadURL(credentials.TokenInternal, projectId, fileInFolder) },
                        { "inputGeometry", await BuildS3DownloadURL(satFileName) },
                        { "familyTemplate", await BuildS3DownloadURL(RFA_TEMPLATE) },
                        { "result", await BuildBIM360UploadURL(credentials.TokenInternal, info)  },
                        { "onComplete", new JObject { new JProperty("verb", "POST"), new JProperty("url", callbackUrl) }}
                      },
                      null);

                    WorkItemsApi workItemApi = new WorkItemsApi();
                    workItemApi.Configuration.AccessToken = appAccessToken;
                    WorkItemStatus newWorkItem = await workItemApi.WorkItemsCreateWorkItemsAsync(null, null, workItemSpec);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }
    }
}